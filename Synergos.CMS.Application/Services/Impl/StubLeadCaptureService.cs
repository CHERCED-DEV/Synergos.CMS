using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Default <see cref="ILeadCaptureService"/> — captura de leads + mini-CRM del agente
/// STUB del portal inmobiliario (doc propiedades-app-spec §4 + §6). Genera un lead a
/// partir del listado de interés + contacto + mensaje, lo persiste, y expone la cara
/// de agente: tablero kanban por estado (<c>Nuevo→Contactado→Visita→Cerrado</c>) +
/// avance auditado. El lead es el caso degenerado del "confirmar" transaccional
/// (captura de intención sin slot ni pago).
/// </summary>
/// <remarks>
/// Lógica pura en <c>Synergos.CMS.Application</c> — cero dependencia de
/// Umbraco/AspNetCore (ADR 0002). REUSA (no crea seams nuevos):
/// <see cref="IAuditTrailWriter"/> (ADR 0037) deja rastro append-only forense del lead
/// y de cada avance, e <see cref="IAnalyticsTracker"/> (ADR 0067) emite el evento de
/// negocio <c>realty.lead-captured</c>. Para poblar el kanban del agente, COMPONE
/// (DIP, opcional) <see cref="IPropertyCatalogProvider"/> y resuelve el agente dueño
/// del inmueble de interés al capturar el lead; si no se inyecta el catálogo, el lead
/// se asigna al agente por defecto. Todos los seams del ctor son opcionales
/// (null = no-op / default) para que los tests del seam corran aislados sin mocks
/// obligatorios. <see cref="AdvanceLeadAsync"/> es idempotente por estado. ADR 0075.
///
/// <para><b>Durabilidad.</b> Los leads viven detrás de
/// <see cref="IJsonEntityStore"/> (ADR 0105) y ya no en un diccionario del proceso:
/// un reinicio borraba el embudo entero del agente — contactos que un interesado
/// había dejado y trabajo de seguimiento ya hecho — mientras el resto del sistema
/// seguía en disco.</para>
///
/// <para>La familia de entidades es PROPIA de este seam (<c>realty-leads</c>):
/// compartir espacio con visitas o búsquedas guardadas haría que un documento se
/// leyera contra la forma de otro dominio, y una deserialización que "casi" encaja
/// no falla — devuelve otra cosa en silencio.</para>
///
/// <para>La siembra de demo del kanban (solo cuando hay catálogo compuesto) pasó a
/// ser PEREZOSA: cero I/O en el ctor (ADR 0013), y no pisa un lead sembrado que el
/// agente ya avanzó en una corrida anterior — solo escribe el que no existe.</para>
/// </remarks>
public sealed class StubLeadCaptureService : ILeadCaptureService
{
    private const string DefaultAgent = "agente-desconocido";

    /// <summary>Familia de entidades en el store genérico (→ App_Data/syn-realty-leads/).</summary>
    private const string DefaultResourceType = "realty-leads";

    private static readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly IAuditTrailWriter? _audit;
    private readonly IAnalyticsTracker? _analytics;
    private readonly IPropertyCatalogProvider? _catalog;
    private readonly Func<DateTimeOffset> _now;
    private readonly IJsonEntityStore _store;
    private readonly string _resourceType;

    // Serializa el read-modify-write del avance y la siembra perezosa. lock{} no
    // sirve: el cuerpo hace await. No es reentrante — nadie lo toma dos veces.
    private readonly SemaphoreSlim _mutate = new(1, 1);
    private volatile bool _seeded;

    public StubLeadCaptureService()
        : this(null, null, null, null)
    {
    }

    /// <summary>
    /// Ctor con los seams reusados opcionales (audit + analytics) y time source
    /// inyectable para determinismo en tests. Null en cualquiera = no-op.
    /// </summary>
    public StubLeadCaptureService(IAuditTrailWriter? audit, IAnalyticsTracker? analytics, Func<DateTimeOffset>? now)
        : this(audit, analytics, null, now)
    {
    }

    /// <summary>
    /// Ctor con catálogo — además del audit/analytics, compone el catálogo (opcional)
    /// para resolver el agente dueño del inmueble al capturar el lead y sembrar la
    /// demo del kanban. Null en el catálogo = leads asignados al agente por defecto.
    /// </summary>
    public StubLeadCaptureService(
        IAuditTrailWriter? audit,
        IAnalyticsTracker? analytics,
        IPropertyCatalogProvider? catalog,
        Func<DateTimeOffset>? now)
        : this(audit, analytics, catalog, now, null, null)
    {
    }

    /// <summary>
    /// Ctor completo: <paramref name="store"/> es el backing store durable
    /// (FileSystem en Web, InMemory en tests) y <paramref name="storeNamespace"/>
    /// separa los leads de las demás familias del portal.
    /// </summary>
    /// <param name="audit">Rastro forense append-only. Null = sin rastro.</param>
    /// <param name="analytics">Evento de negocio del marketplace. Null = no-op.</param>
    /// <param name="catalog">Catálogo para resolver el agente dueño. Null = agente por defecto.</param>
    /// <param name="now">Reloj inyectable para los tests. Null usa el del sistema.</param>
    /// <param name="store">Backing store durable. Null deja los leads en memoria.</param>
    /// <param name="storeNamespace">Familia de entidades de ESTA instancia.
    ///   Null usa <c>realty-leads</c>. Cambiarlo re-ubica los datos.</param>
    public StubLeadCaptureService(
        IAuditTrailWriter? audit,
        IAnalyticsTracker? analytics,
        IPropertyCatalogProvider? catalog,
        Func<DateTimeOffset>? now,
        IJsonEntityStore? store,
        string? storeNamespace)
    {
        _audit = audit;
        _analytics = analytics;
        _catalog = catalog;
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _store = store ?? new InMemoryJsonEntityStore();
        _resourceType = string.IsNullOrWhiteSpace(storeNamespace) ? DefaultResourceType : storeNamespace;
    }

    public async Task<LeadResult> CaptureAsync(string listingId, VisitContact contact, string message, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(listingId))
        {
            throw new ArgumentException("El listado es obligatorio.", nameof(listingId));
        }
        if (contact is null || string.IsNullOrWhiteSpace(contact.Name) || string.IsNullOrWhiteSpace(contact.Email))
        {
            throw new ArgumentException("El nombre y el email del interesado son obligatorios.", nameof(contact));
        }
        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException("El mensaje es obligatorio.", nameof(message));
        }

        await EnsureSeededAsync(cancellationToken).ConfigureAwait(false);

        var listing = listingId.Trim();
        var leadId = $"lead_{Guid.NewGuid():N}";
        var occurred = _now();
        var agentId = await ResolveAgentAsync(listing, cancellationToken).ConfigureAwait(false);

        await SaveAsync(
            new LeadRecord(
                leadId, agentId, listing, contact.Name.Trim(), contact.Email.Trim(),
                contact.Phone?.Trim(), message.Trim(), LeadStatus.Nuevo, occurred),
            cancellationToken).ConfigureAwait(false);

        // Audit append-only (forense) — opcional.
        if (_audit is not null)
        {
            // best-effort: el lead YA está persistido.
            await BestEffort.RunAsync(() => _audit.WriteAsync(
                    new AuditEvent(
                        Id: leadId,
                        OccurredAtUtc: occurred.UtcDateTime,
                        ActorEmail: contact.Email.Trim(),
                        ActorName: contact.Name.Trim(),
                        Action: "realty.lead-captured",
                        Resource: listing,
                        Outcome: "success",
                        Detail: $"Lead generado para el listado {listing}."),
                    cancellationToken), cancellationToken);
        }

        // Evento de negocio (KPI del marketplace) — opcional, fire-and-forget.
        _analytics?.Track("realty.lead-captured", new Dictionary<string, object?>
        {
            ["leadId"] = leadId,
            ["listingId"] = listing,
            ["agentId"] = agentId,
        });

        return new LeadResult(leadId);
    }

    public async Task<IReadOnlyList<AgentLead>> GetForAgentAsync(string agentId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(agentId))
        {
            return Array.Empty<AgentLead>();
        }

        await EnsureSeededAsync(cancellationToken).ConfigureAwait(false);

        var key = agentId.Trim();
        var all = await LoadAllAsync(cancellationToken).ConfigureAwait(false);
        return all
            .Where(l => string.Equals(l.AgentId, key, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(l => l.CreatedAt)
            .ThenBy(l => l.LeadId, StringComparer.Ordinal)
            .Select(l => new AgentLead(
                l.LeadId, l.AgentId, l.ListingId, l.Name, l.Email, l.Phone, l.Message, l.Status, l.CreatedAt))
            .ToList();
    }

    public async Task<LeadAdvanceResult> AdvanceLeadAsync(string leadId, LeadStatus status, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(leadId))
        {
            throw new ArgumentException("El id del lead es obligatorio.", nameof(leadId));
        }

        // Fuera del semáforo: EnsureSeededAsync lo toma, y no es reentrante.
        await EnsureSeededAsync(cancellationToken).ConfigureAwait(false);

        var id = leadId.Trim();
        LeadRecord current;
        await _mutate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            current = await LoadAsync(id, cancellationToken).ConfigureAwait(false)
                ?? throw new ArgumentException($"Lead '{id}' no encontrado.", nameof(leadId));

            // Idempotente: avanzar al estado actual no re-audita ni cambia nada.
            if (current.Status == status)
            {
                return new LeadAdvanceResult(id, status);
            }

            await SaveAsync(current with { Status = status }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _mutate.Release();
        }

        if (_audit is not null)
        {
            // best-effort: el lead YA está persistido.
            await BestEffort.RunAsync(() => _audit.WriteAsync(
                    new AuditEvent(
                        Id: $"{id}:advance:{status}",
                        OccurredAtUtc: _now().UtcDateTime,
                        ActorEmail: string.Empty,
                        ActorName: current.AgentId,
                        Action: "realty.lead-advanced",
                        Resource: id,
                        Outcome: "success",
                        Detail: $"Lead {id} avanzó de {current.Status} a {status}."),
                    cancellationToken), cancellationToken);
        }

        return new LeadAdvanceResult(id, status);
    }

    private async Task<string> ResolveAgentAsync(string listingId, CancellationToken cancellationToken)
    {
        if (_catalog is null)
        {
            return DefaultAgent;
        }

        var detail = await _catalog.GetListingAsync(listingId, cancellationToken);
        return detail is null || string.IsNullOrWhiteSpace(detail.AgentName)
            ? DefaultAgent
            : SlugifyAgent(detail.AgentName);
    }

    // ── Persistencia ────────────────────────────────────────────────────────

    private Task SaveAsync(LeadRecord lead, CancellationToken cancellationToken)
        => _store.WriteAsync(_resourceType, lead.LeadId, JsonSerializer.Serialize(lead, _json), cancellationToken);

    private async Task<LeadRecord?> LoadAsync(string leadId, CancellationToken cancellationToken)
    {
        var json = await _store.ReadAsync(_resourceType, leadId, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }
        try
        {
            var lead = JsonSerializer.Deserialize<LeadRecord>(json, _json);
            return lead is null || string.IsNullOrWhiteSpace(lead.LeadId) ? null : lead;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Lee TODOS los leads del store, saltando lo ilegible: un documento corrupto
    /// no puede tumbar el embudo completo del agente.
    /// </summary>
    /// <remarks>
    /// <see cref="IJsonEntityStore.ListAsync"/> devuelve los DOCUMENTOS, no las
    /// claves — pasarle uno a <c>ReadAsync</c> devolvería null para siempre y en
    /// silencio.
    /// </remarks>
    private async Task<List<LeadRecord>> LoadAllAsync(CancellationToken cancellationToken)
    {
        var raws = await _store.ListAsync(_resourceType, cancellationToken).ConfigureAwait(false);
        var leads = new List<LeadRecord>(raws.Count);
        foreach (var json in raws)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                continue;
            }
            try
            {
                var lead = JsonSerializer.Deserialize<LeadRecord>(json, _json);
                if (lead is not null && !string.IsNullOrWhiteSpace(lead.LeadId))
                {
                    leads.Add(lead);
                }
            }
            catch (JsonException)
            {
                // un lead corrupto no puede tumbar el kanban entero
            }
        }
        return leads;
    }

    // ── Siembra de demo (perezosa) ──────────────────────────────────────────

    // Siembra un par de leads de demo para que el kanban del agente no arranque
    // vacío. Solo cuando hay catálogo compuesto (contexto real de la app); en
    // tests aislados (ctor sin catálogo) no se siembra, para no ensuciar los
    // conteos canónicos. Perezosa: cero I/O en el ctor (ADR 0013).
    private async Task EnsureSeededAsync(CancellationToken cancellationToken)
    {
        if (_catalog is null || _seeded)
        {
            return;
        }

        await _mutate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_seeded)
            {
                return;
            }

            var baseTime = _now();
            await SeedAsync("laura-gomez", "prop-001", "Mariana Ortiz", "mariana@example.co", "+57 300 111 2233",
                "¿Sigue disponible el apartamento en Chicó?", LeadStatus.Nuevo, baseTime.AddHours(-2), cancellationToken)
                .ConfigureAwait(false);
            await SeedAsync("laura-gomez", "prop-006", "Julián Pérez", "julian@example.co", "+57 300 444 5566",
                "Quisiera ver la casa en Cedritos este fin de semana.", LeadStatus.Contactado, baseTime.AddDays(-1), cancellationToken)
                .ConfigureAwait(false);
            await SeedAsync("andres-mejia", "prop-002", "Sofía Ramírez", "sofia@example.co", "+57 300 777 8899",
                "Me interesa la casa en Laureles, ¿acepta crédito?", LeadStatus.Visita, baseTime.AddDays(-3), cancellationToken)
                .ConfigureAwait(false);

            _seeded = true;
        }
        finally
        {
            _mutate.Release();
        }
    }

    // Escribe SOLO si no existe: tras un reinicio, un lead de demo que el agente
    // ya avanzó no puede volver a "Nuevo" — la siembra no pisa trabajo hecho.
    private async Task SeedAsync(string agentId, string listingId, string name, string email, string? phone,
        string message, LeadStatus status, DateTimeOffset at, CancellationToken cancellationToken)
    {
        var leadId = $"lead_seed_{agentId}_{listingId}";
        if (await LoadAsync(leadId, cancellationToken).ConfigureAwait(false) is not null)
        {
            return;
        }
        await SaveAsync(
            new LeadRecord(leadId, agentId, listingId, name, email, phone, message, status, at),
            cancellationToken).ConfigureAwait(false);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    // El agentId estable es el slug del nombre del agente (los inmuebles del stub
    // repiten agentes, así el kanban agrupa por agente sin un padrón aparte). Se
    // remueven diacríticos para que "Laura Gómez" → "laura-gomez" (id ascii estable).
    private static string SlugifyAgent(string agentName)
    {
        var stripped = RemoveDiacritics(agentName.Trim().ToLowerInvariant());
        var chars = stripped.Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray();
        var slug = new string(chars);
        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }
        return slug.Trim('-');
    }

    private static string RemoveDiacritics(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(c);
            }
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    private sealed record LeadRecord(
        string LeadId,
        string AgentId,
        string ListingId,
        string Name,
        string Email,
        string? Phone,
        string Message,
        LeadStatus Status,
        DateTimeOffset CreatedAt);
}
