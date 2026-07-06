using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Default <see cref="ILeadCaptureService"/> — captura de leads + mini-CRM del agente
/// STUB del portal inmobiliario (doc propiedades-app-spec §4 + §6). Genera un lead a
/// partir del listado de interés + contacto + mensaje, lo persiste en memoria del
/// proceso, y expone la cara de agente: tablero kanban por estado
/// (<c>Nuevo→Contactado→Visita→Cerrado</c>) + avance auditado. El lead es el caso
/// degenerado del "confirmar" transaccional (captura de intención sin slot ni pago).
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
/// </remarks>
public sealed class StubLeadCaptureService : ILeadCaptureService
{
    private const string DefaultAgent = "agente-desconocido";

    private readonly IAuditTrailWriter? _audit;
    private readonly IAnalyticsTracker? _analytics;
    private readonly IPropertyCatalogProvider? _catalog;
    private readonly Func<DateTimeOffset> _now;
    private readonly ConcurrentDictionary<string, LeadRecord> _leads = new(StringComparer.Ordinal);

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
    /// Ctor completo — además del audit/analytics, compone el catálogo (opcional) para
    /// resolver el agente dueño del inmueble al capturar el lead y sembrar la demo del
    /// kanban. Null en el catálogo = leads asignados al agente por defecto.
    /// </summary>
    public StubLeadCaptureService(
        IAuditTrailWriter? audit,
        IAnalyticsTracker? analytics,
        IPropertyCatalogProvider? catalog,
        Func<DateTimeOffset>? now)
    {
        _audit = audit;
        _analytics = analytics;
        _catalog = catalog;
        _now = now ?? (() => DateTimeOffset.UtcNow);
        SeedDemoLeads();
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

        var listing = listingId.Trim();
        var leadId = $"lead_{Guid.NewGuid():N}";
        var occurred = _now();
        var agentId = await ResolveAgentAsync(listing, cancellationToken);

        _leads[leadId] = new LeadRecord(
            leadId, agentId, listing, contact.Name.Trim(), contact.Email.Trim(),
            contact.Phone?.Trim(), message.Trim(), LeadStatus.Nuevo, occurred);

        // Audit append-only (forense) — opcional.
        if (_audit is not null)
        {
            await _audit.WriteAsync(
                new AuditEvent(
                    Id: leadId,
                    OccurredAtUtc: occurred.UtcDateTime,
                    ActorEmail: contact.Email.Trim(),
                    ActorName: contact.Name.Trim(),
                    Action: "realty.lead-captured",
                    Resource: listing,
                    Outcome: "success",
                    Detail: $"Lead generado para el listado {listing}."),
                cancellationToken);
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

    public Task<IReadOnlyList<AgentLead>> GetForAgentAsync(string agentId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(agentId))
        {
            return Task.FromResult<IReadOnlyList<AgentLead>>(Array.Empty<AgentLead>());
        }

        var key = agentId.Trim();
        var list = _leads.Values
            .Where(l => string.Equals(l.AgentId, key, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(l => l.CreatedAt)
            .ThenBy(l => l.LeadId, StringComparer.Ordinal)
            .Select(l => new AgentLead(
                l.LeadId, l.AgentId, l.ListingId, l.Name, l.Email, l.Phone, l.Message, l.Status, l.CreatedAt))
            .ToList();
        return Task.FromResult<IReadOnlyList<AgentLead>>(list);
    }

    public async Task<LeadAdvanceResult> AdvanceLeadAsync(string leadId, LeadStatus status, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(leadId))
        {
            throw new ArgumentException("El id del lead es obligatorio.", nameof(leadId));
        }

        var id = leadId.Trim();
        if (!_leads.TryGetValue(id, out var current))
        {
            throw new ArgumentException($"Lead '{id}' no encontrado.", nameof(leadId));
        }

        // Idempotente: avanzar al estado actual no re-audita ni cambia nada.
        if (current.Status == status)
        {
            return new LeadAdvanceResult(id, status);
        }

        _leads[id] = current with { Status = status };

        if (_audit is not null)
        {
            await _audit.WriteAsync(
                new AuditEvent(
                    Id: $"{id}:advance:{status}",
                    OccurredAtUtc: _now().UtcDateTime,
                    ActorEmail: string.Empty,
                    ActorName: current.AgentId,
                    Action: "realty.lead-advanced",
                    Resource: id,
                    Outcome: "success",
                    Detail: $"Lead {id} avanzó de {current.Status} a {status}."),
                cancellationToken);
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

    // Siembra un par de leads de demo para que el kanban del agente no arranque vacío.
    // Solo cuando hay catálogo compuesto (contexto real de la app); en tests aislados
    // (ctor sin catálogo) no se siembra, para no ensuciar los conteos canónicos.
    private void SeedDemoLeads()
    {
        if (_catalog is null)
        {
            return;
        }

        var baseTime = _now();
        Seed("laura-gomez", "prop-001", "Mariana Ortiz", "mariana@example.co", "+57 300 111 2233",
            "¿Sigue disponible el apartamento en Chicó?", LeadStatus.Nuevo, baseTime.AddHours(-2));
        Seed("laura-gomez", "prop-006", "Julián Pérez", "julian@example.co", "+57 300 444 5566",
            "Quisiera ver la casa en Cedritos este fin de semana.", LeadStatus.Contactado, baseTime.AddDays(-1));
        Seed("andres-mejia", "prop-002", "Sofía Ramírez", "sofia@example.co", "+57 300 777 8899",
            "Me interesa la casa en Laureles, ¿acepta crédito?", LeadStatus.Visita, baseTime.AddDays(-3));
    }

    private void Seed(string agentId, string listingId, string name, string email, string? phone,
        string message, LeadStatus status, DateTimeOffset at)
    {
        var leadId = $"lead_seed_{agentId}_{listingId}";
        _leads[leadId] = new LeadRecord(leadId, agentId, listingId, name, email, phone, message, status, at);
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
