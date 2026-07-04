using System.Collections.Concurrent;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Default <see cref="IApplicationService"/> — agregado raíz STUB del expediente del
/// vertical Gobierno (doc gobierno-app-spec §4), calcando <c>StubEventTicketingService</c>
/// de Eventos. Radica solicitudes (valida el trámite contra
/// <see cref="ITramiteCatalogProvider"/>, exige los campos requeridos del formulario
/// dinámico, calcula la tasa con <see cref="IGovFeeCalculator"/> y abre la sesión de
/// pago con <see cref="IPaymentProvider"/> solo si la tasa aplica) y es la FUENTE DE
/// VERDAD del estado de los expedientes: <see cref="ICaseTrackingProvider"/> /
/// <see cref="ICaseWorkflowService"/> / <see cref="IDocumentUploadService"/> lo leen y
/// mutan por composición (DIP), sin duplicar estado.
/// </summary>
/// <remarks>
/// Lógica pura en <c>Synergos.CMS.Application</c> — cero dependencia de
/// Umbraco/AspNetCore (ADR 0002). REUSA el motor (no lo reinventa): la tasa es un pago
/// OPCIONAL vía <see cref="IPaymentProvider"/> (trámite gratuito = paso de pago OFF,
/// caso ya validado en Propiedades) y cada radicación deja un evento append-only
/// <c>gov.case-radicado</c> en <see cref="IAuditTrailWriter"/> (ADR 0037 — el rastro
/// forense es el diferenciador del dominio). <see cref="RadicarAsync"/> NO es
/// idempotente por diseño (cada radicación crea un expediente nuevo, como una orden);
/// la idempotencia vive en las transiciones (<c>StubCaseWorkflowService</c>). Siembra
/// expedientes en varios estados para que la demo muestre el ciclo de vida completo
/// (bandeja ciudadano + cola funcionario) sin radicar primero. Estado en memoria del
/// proceso (Singleton). ADR 0075.
/// </remarks>
public sealed class StubApplicationService : IApplicationService
{
    private readonly ITramiteCatalogProvider _catalog;
    private readonly IGovFeeCalculator _fees;
    private readonly IPaymentProvider _payments;
    private readonly IAuditTrailWriter? _audit;
    private readonly Func<DateTimeOffset> _now;
    private readonly ConcurrentDictionary<string, CaseState> _cases = new(StringComparer.OrdinalIgnoreCase);
    private int _radicadoSequence = 1005;

    public StubApplicationService(
        ITramiteCatalogProvider catalog,
        IGovFeeCalculator fees,
        IPaymentProvider payments)
        : this(catalog, fees, payments, null, null)
    {
    }

    /// <summary>
    /// Ctor configurable: audit opcional (null = no-op, tests aislados sin mock
    /// obligatorio) + time source inyectable para determinismo en tests (ADR 0002).
    /// </summary>
    public StubApplicationService(
        ITramiteCatalogProvider catalog,
        IGovFeeCalculator fees,
        IPaymentProvider payments,
        IAuditTrailWriter? audit,
        Func<DateTimeOffset>? now)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _fees = fees ?? throw new ArgumentNullException(nameof(fees));
        _payments = payments ?? throw new ArgumentNullException(nameof(payments));
        _audit = audit;
        _now = now ?? (() => DateTimeOffset.UtcNow);
        Seed();
    }

    public async Task<RadicarResult> RadicarAsync(
        string tramiteId,
        IReadOnlyDictionary<string, string> formData,
        GovCitizen citizen,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tramiteId))
        {
            throw new ArgumentException("El trámite es obligatorio.", nameof(tramiteId));
        }
        if (formData is null)
        {
            throw new ArgumentException("Las respuestas del formulario son obligatorias.", nameof(formData));
        }
        if (citizen is null || string.IsNullOrWhiteSpace(citizen.Name) || string.IsNullOrWhiteSpace(citizen.Email))
        {
            throw new ArgumentException("El nombre y el email del solicitante son obligatorios.", nameof(citizen));
        }

        var detail = await _catalog.GetAsync(tramiteId.Trim(), cancellationToken)
            ?? throw new ArgumentException($"Trámite '{tramiteId.Trim()}' no encontrado.", nameof(tramiteId));

        // Formulario dinámico data-driven: exigir cada campo requerido de la definición.
        foreach (var field in detail.FormDefinition.Fields)
        {
            if (field.Required
                && (!formData.TryGetValue(field.Key, out var value) || string.IsNullOrWhiteSpace(value)))
            {
                throw new ArgumentException(
                    $"El campo requerido '{field.Label}' ({field.Key}) está vacío.", nameof(formData));
            }
        }

        var occurred = _now();
        var quote = _fees.Calculate(detail.Summary.Id, detail.Fee, detail.Currency);
        var caseId = $"case_{Guid.NewGuid():N}";
        var radicado = NextRadicado(occurred);

        // Tasa OPCIONAL: solo abre sesión de pago si el trámite cobra (Fee > 0).
        string? paymentSessionId = null;
        if (!quote.Exempt && quote.Amount > 0m)
        {
            var session = await _payments.CreateSessionAsync(
                new PaymentSessionRequest(
                    OrderReference: radicado,
                    Amount: quote.Amount,
                    Currency: quote.Currency,
                    Items: new[]
                    {
                        new PaymentLineItem(
                            Sku: detail.Summary.Id,
                            Description: $"Tasa — {detail.Summary.Name}",
                            UnitPrice: quote.Amount,
                            Quantity: 1),
                    },
                    CustomerEmail: citizen.Email.Trim()),
                cancellationToken);
            paymentSessionId = session.SessionId;
        }

        var cleanCitizen = new GovCitizen(
            citizen.Name.Trim(), citizen.Email.Trim(), citizen.DocumentId?.Trim(), citizen.Phone?.Trim());

        _cases[caseId] = new CaseState(
            CaseId: caseId,
            Radicado: radicado,
            TramiteId: detail.Summary.Id,
            TramiteName: detail.Summary.Name,
            Citizen: cleanCitizen,
            FormData: new Dictionary<string, string>(formData, StringComparer.Ordinal),
            Documents: Array.Empty<CitizenDocumentRef>(),
            Status: CaseStatus.Radicado,
            Fee: quote.Amount,
            Currency: quote.Currency,
            RadicadoAt: occurred,
            Timeline: new[]
            {
                new CaseTimelineEntry(CaseStatus.Radicado, occurred, cleanCitizen.Email, "Solicitud radicada en línea."),
            });

        // Rastro forense append-only del primer estado del expediente (ADR 0037).
        if (_audit is not null)
        {
            await _audit.WriteAsync(
                new AuditEvent(
                    Id: caseId,
                    OccurredAtUtc: occurred.UtcDateTime,
                    ActorEmail: cleanCitizen.Email,
                    ActorName: cleanCitizen.Name,
                    Action: "gov.case-radicado",
                    Resource: radicado,
                    Outcome: "success",
                    Detail: $"Expediente radicado para el trámite {detail.Summary.Id} (tasa {quote.Amount} {quote.Currency})."),
                cancellationToken);
        }

        return new RadicarResult(caseId, radicado, quote.Amount, quote.Currency, paymentSessionId);
    }

    // ── Superficie de composición (DIP) para tracking / workflow / documentos ──
    // El agregado es la fuente de verdad; los stubs hermanos leen/mutan por aquí
    // sin duplicar estado (mismo patrón StubEventTicketingService → Management).

    /// <summary>
    /// Devuelve el expediente por id de caso O por número de radicado, o null si no
    /// existe. Búsqueda case-insensitive.
    /// </summary>
    public CaseDetail? FindCase(string caseIdOrRadicado)
    {
        var state = Resolve(caseIdOrRadicado);
        return state is null ? null : ToDetail(state);
    }

    /// <summary>Devuelve todos los expedientes (para armar las bandejas).</summary>
    public IReadOnlyList<CaseDetail> ListCases()
        => _cases.Values.Select(ToDetail).ToList();

    /// <summary>
    /// Aplica una transición YA VALIDADA por la máquina de estados
    /// (<c>StubCaseWorkflowService</c> es quien decide legalidad e idempotencia):
    /// cambia el estado y agrega la entrada al timeline. Devuelve el estado resultante.
    /// Lanza <see cref="ArgumentException"/> si el expediente no existe.
    /// </summary>
    public CaseStatus ApplyTransition(string caseIdOrRadicado, CaseStatus to, string actor, string note, DateTimeOffset occurredAt)
    {
        var state = Resolve(caseIdOrRadicado)
            ?? throw new ArgumentException($"Expediente '{caseIdOrRadicado}' no encontrado.", nameof(caseIdOrRadicado));

        var timeline = state.Timeline.ToList();
        timeline.Add(new CaseTimelineEntry(to, occurredAt, actor, note));
        _cases[state.CaseId] = state with { Status = to, Timeline = timeline };
        return to;
    }

    /// <summary>
    /// Adjunta al expediente las referencias de documentos ACEPTADAS (las rechazadas
    /// no se adjuntan — solo viajan en el resultado del upload). Lanza
    /// <see cref="ArgumentException"/> si el expediente no existe.
    /// </summary>
    public void AttachDocuments(string caseIdOrRadicado, IReadOnlyList<CitizenDocumentRef> accepted)
    {
        var state = Resolve(caseIdOrRadicado)
            ?? throw new ArgumentException($"Expediente '{caseIdOrRadicado}' no encontrado.", nameof(caseIdOrRadicado));

        if (accepted.Count == 0)
        {
            return;
        }

        var docs = state.Documents.ToList();
        docs.AddRange(accepted);
        _cases[state.CaseId] = state with { Documents = docs };
    }

    private CaseState? Resolve(string caseIdOrRadicado)
    {
        if (string.IsNullOrWhiteSpace(caseIdOrRadicado))
        {
            return null;
        }
        var key = caseIdOrRadicado.Trim();
        if (_cases.TryGetValue(key, out var byId))
        {
            return byId;
        }
        return _cases.Values.FirstOrDefault(c =>
            string.Equals(c.Radicado, key, StringComparison.OrdinalIgnoreCase));
    }

    private static CaseDetail ToDetail(CaseState s) => new(
        CaseId: s.CaseId,
        Radicado: s.Radicado,
        TramiteId: s.TramiteId,
        TramiteName: s.TramiteName,
        Citizen: s.Citizen,
        FormData: s.FormData,
        Documents: s.Documents,
        Status: s.Status,
        Fee: s.Fee,
        Currency: s.Currency,
        RadicadoAt: s.RadicadoAt,
        Timeline: s.Timeline);

    private string NextRadicado(DateTimeOffset occurredAt)
        => $"RAD-{occurredAt.Year}-{Interlocked.Increment(ref _radicadoSequence):D4}";

    // ── Seed: expedientes en varios estados (demo del ciclo de vida completo) ──

    private void Seed()
    {
        var baseDate = new DateTimeOffset(2026, 6, 20, 9, 0, 0, TimeSpan.FromHours(-5));
        const string officer = "funcionario@entidad.gov.co";

        // 1) Radicado — recién llega a la cola.
        SeedCase(
            caseId: "case-1001",
            radicado: "RAD-2026-1001",
            tramiteId: "trm-certificado-residencia",
            tramiteName: "Certificado de residencia",
            citizen: new GovCitizen("María Fernanda López", "maria.lopez@correo.co", "52841937", "+57 310 555 0101"),
            formData: new Dictionary<string, string>
            {
                ["nombreCompleto"] = "María Fernanda López",
                ["cedula"] = "52841937",
                ["direccion"] = "Cra 15 # 82-33",
                ["barrio"] = "El Lago",
                ["correo"] = "maria.lopez@correo.co",
            },
            fee: 0m,
            radicadoAt: baseDate.AddDays(8),
            timeline: new[]
            {
                new CaseTimelineEntry(CaseStatus.Radicado, baseDate.AddDays(8), "maria.lopez@correo.co", "Solicitud radicada en línea."),
            });

        // 2) En revisión — el funcionario la tomó.
        SeedCase(
            caseId: "case-1002",
            radicado: "RAD-2026-1002",
            tramiteId: "trm-licencia-conduccion",
            tramiteName: "Renovación de licencia de conducción",
            citizen: new GovCitizen("Carlos Andrés Pérez", "carlos.perez@correo.co", "80234561", "+57 315 555 0202"),
            formData: new Dictionary<string, string>
            {
                ["nombreCompleto"] = "Carlos Andrés Pérez",
                ["cedula"] = "80234561",
                ["categoria"] = "B1",
                ["examenMedico"] = "true",
                ["correo"] = "carlos.perez@correo.co",
            },
            fee: 95_000m,
            radicadoAt: baseDate.AddDays(5),
            timeline: new[]
            {
                new CaseTimelineEntry(CaseStatus.Radicado, baseDate.AddDays(5), "carlos.perez@correo.co", "Solicitud radicada en línea."),
                new CaseTimelineEntry(CaseStatus.EnRevision, baseDate.AddDays(6), officer, "Expediente asignado para revisión."),
            });

        // 3) Subsanación — falta un documento; el ciudadano debe responder.
        SeedCase(
            caseId: "case-1003",
            radicado: "RAD-2026-1003",
            tramiteId: "trm-pasaporte",
            tramiteName: "Expedición de pasaporte",
            citizen: new GovCitizen("Juliana Ríos", "juliana.rios@correo.co", "1020456789", "+57 300 555 0303"),
            formData: new Dictionary<string, string>
            {
                ["nombreCompleto"] = "Juliana Ríos",
                ["cedula"] = "1020456789",
                ["fechaNacimiento"] = "1994-03-12",
                ["ciudad"] = "Bogotá",
                ["correo"] = "juliana.rios@correo.co",
            },
            fee: 189_000m,
            radicadoAt: baseDate.AddDays(2),
            timeline: new[]
            {
                new CaseTimelineEntry(CaseStatus.Radicado, baseDate.AddDays(2), "juliana.rios@correo.co", "Solicitud radicada en línea."),
                new CaseTimelineEntry(CaseStatus.EnRevision, baseDate.AddDays(3), officer, "Expediente asignado para revisión."),
                new CaseTimelineEntry(CaseStatus.Subsanacion, baseDate.AddDays(4), officer, "La foto no cumple el fondo blanco — adjuntar una nueva."),
            },
            documents: new[]
            {
                new CitizenDocumentRef(
                    Id: "doc-1003-01",
                    CaseId: "case-1003",
                    FileName: "cedula.pdf",
                    ContentType: "application/pdf",
                    SizeBytes: 412_884,
                    ValidationStatus: "accepted",
                    SecureUrl: "/api/gov/case/case-1003/document/doc-1003-01"),
            });

        // 4) Resuelto — ciclo completo favorable.
        SeedCase(
            caseId: "case-1004",
            radicado: "RAD-2026-1004",
            tramiteId: "trm-registro-mercantil",
            tramiteName: "Registro mercantil de empresa",
            citizen: new GovCitizen("Andrés Felipe Torres", "andres.torres@correo.co", "900123456", "+57 320 555 0404"),
            formData: new Dictionary<string, string>
            {
                ["razonSocial"] = "Café La Palma S.A.S.",
                ["nit"] = "900123456",
                ["actividad"] = "5613 — Expendio de comidas",
                ["representante"] = "Andrés Felipe Torres",
                ["correo"] = "andres.torres@correo.co",
            },
            fee: 320_000m,
            radicadoAt: baseDate.AddDays(-6),
            timeline: new[]
            {
                new CaseTimelineEntry(CaseStatus.Radicado, baseDate.AddDays(-6), "andres.torres@correo.co", "Solicitud radicada en línea."),
                new CaseTimelineEntry(CaseStatus.EnRevision, baseDate.AddDays(-5), officer, "Expediente asignado para revisión."),
                new CaseTimelineEntry(CaseStatus.Resuelto, baseDate.AddDays(-3), officer, "Registro mercantil expedido — certificado disponible."),
            },
            documents: new[]
            {
                new CitizenDocumentRef(
                    Id: "doc-1004-01",
                    CaseId: "case-1004",
                    FileName: "acta-constitucion.pdf",
                    ContentType: "application/pdf",
                    SizeBytes: 1_204_113,
                    ValidationStatus: "accepted",
                    SecureUrl: "/api/gov/case/case-1004/document/doc-1004-01"),
            });

        // 5) Rechazado — ciclo completo desfavorable.
        SeedCase(
            caseId: "case-1005",
            radicado: "RAD-2026-1005",
            tramiteId: "trm-afiliacion-salud",
            tramiteName: "Afiliación al régimen subsidiado de salud",
            citizen: new GovCitizen("Luz Dary Gómez", "luz.gomez@correo.co", "39765432", "+57 311 555 0505"),
            formData: new Dictionary<string, string>
            {
                ["nombreCompleto"] = "Luz Dary Gómez",
                ["cedula"] = "39765432",
                ["sisben"] = "C5",
                ["eps"] = "Nueva EPS",
                ["correo"] = "luz.gomez@correo.co",
            },
            fee: 0m,
            radicadoAt: baseDate.AddDays(-10),
            timeline: new[]
            {
                new CaseTimelineEntry(CaseStatus.Radicado, baseDate.AddDays(-10), "luz.gomez@correo.co", "Solicitud radicada en línea."),
                new CaseTimelineEntry(CaseStatus.EnRevision, baseDate.AddDays(-9), officer, "Expediente asignado para revisión."),
                new CaseTimelineEntry(CaseStatus.Rechazado, baseDate.AddDays(-7), officer, "Puntaje SISBÉN fuera del rango del régimen subsidiado."),
            });
    }

    private void SeedCase(
        string caseId,
        string radicado,
        string tramiteId,
        string tramiteName,
        GovCitizen citizen,
        IReadOnlyDictionary<string, string> formData,
        decimal fee,
        DateTimeOffset radicadoAt,
        IReadOnlyList<CaseTimelineEntry> timeline,
        IReadOnlyList<CitizenDocumentRef>? documents = null)
    {
        _cases[caseId] = new CaseState(
            CaseId: caseId,
            Radicado: radicado,
            TramiteId: tramiteId,
            TramiteName: tramiteName,
            Citizen: citizen,
            FormData: formData,
            Documents: documents ?? Array.Empty<CitizenDocumentRef>(),
            Status: timeline[^1].Status,
            Fee: fee,
            Currency: "COP",
            RadicadoAt: radicadoAt,
            Timeline: timeline);
    }

    private sealed record CaseState(
        string CaseId,
        string Radicado,
        string TramiteId,
        string TramiteName,
        GovCitizen Citizen,
        IReadOnlyDictionary<string, string> FormData,
        IReadOnlyList<CitizenDocumentRef> Documents,
        CaseStatus Status,
        decimal Fee,
        string Currency,
        DateTimeOffset RadicadoAt,
        IReadOnlyList<CaseTimelineEntry> Timeline);
}
