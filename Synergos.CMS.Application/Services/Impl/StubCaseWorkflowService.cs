using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Default <see cref="ICaseWorkflowService"/> — máquina de estados STUB del expediente
/// del vertical Gobierno (doc gobierno-app-spec §4, cara de funcionario):
/// <c>radicado → en-revisión → subsanación → resuelto / rechazado</c>. Valida que cada
/// transición sea legal (tabla explícita, State pattern data-driven), es IDEMPOTENTE
/// (transicionar al estado actual devuelve ese estado sin efecto ni re-auditoría) y
/// asienta CADA transición legal como evento append-only en
/// <see cref="IAuditTrailWriter"/> — el rastro forense es el diferenciador del dominio.
/// </summary>
/// <remarks>
/// Lógica pura en <c>Synergos.CMS.Application</c> — cero dependencia de
/// Umbraco/AspNetCore (ADR 0002). NO duplica estado: muta el agregado de
/// <see cref="StubApplicationService"/> por composición (DIP, mismo patrón
/// <c>StubEventManagementService</c> → <c>StubEventTicketingService</c>). Transiciones
/// legales: Radicado→EnRevision; EnRevision→Subsanacion|Resuelto|Rechazado;
/// Subsanacion→EnRevision (el ciudadano responde y vuelve a revisión); Resuelto y
/// Rechazado son terminales. Todo lo demás lanza
/// <see cref="InvalidOperationException"/>. ADR 0075 (idempotent es el caso central).
/// </remarks>
public sealed class StubCaseWorkflowService : ICaseWorkflowService
{
    /// <summary>Actor de demo de la cara de funcionario (la UI conmuta rol, no identidad).</summary>
    private const string OfficerActor = "funcionario@entidad.gov.co";

    private static readonly IReadOnlyDictionary<CaseStatus, CaseStatus[]> LegalTransitions =
        new Dictionary<CaseStatus, CaseStatus[]>
        {
            [CaseStatus.Radicado] = new[] { CaseStatus.EnRevision },
            [CaseStatus.EnRevision] = new[] { CaseStatus.Subsanacion, CaseStatus.Resuelto, CaseStatus.Rechazado },
            [CaseStatus.Subsanacion] = new[] { CaseStatus.EnRevision },
            [CaseStatus.Resuelto] = Array.Empty<CaseStatus>(),
            [CaseStatus.Rechazado] = Array.Empty<CaseStatus>(),
        };

    private readonly StubApplicationService _cases;
    private readonly IAuditTrailWriter? _audit;
    private readonly Func<DateTimeOffset> _now;

    public StubCaseWorkflowService(StubApplicationService cases)
        : this(cases, null, null)
    {
    }

    /// <summary>
    /// Ctor configurable: audit opcional (null = no-op, tests aislados) + time source
    /// inyectable para determinismo en tests (ADR 0002).
    /// </summary>
    public StubCaseWorkflowService(StubApplicationService cases, IAuditTrailWriter? audit, Func<DateTimeOffset>? now)
    {
        _cases = cases ?? throw new ArgumentNullException(nameof(cases));
        _audit = audit;
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task<CaseTransitionResult> TransitionAsync(
        string caseId,
        CaseStatus to,
        string note,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(caseId))
        {
            throw new ArgumentException("El expediente es obligatorio.", nameof(caseId));
        }

        var current = _cases.FindCase(caseId)
            ?? throw new ArgumentException($"Expediente '{caseId.Trim()}' no encontrado.", nameof(caseId));

        // Idempotente: transicionar al estado actual devuelve ese estado sin
        // re-auditar ni tocar el timeline (núcleo anti-doble-efecto del dominio).
        if (current.Status == to)
        {
            return new CaseTransitionResult(current.Status);
        }

        if (!LegalTransitions.TryGetValue(current.Status, out var allowed) || !allowed.Contains(to))
        {
            throw new InvalidOperationException(
                $"Transición ilegal: el expediente {current.Radicado} está en '{current.Status}' y no puede pasar a '{to}'.");
        }

        var occurred = _now();
        var cleanNote = string.IsNullOrWhiteSpace(note) ? $"Transición a {to}." : note.Trim();
        var resulting = _cases.ApplyTransition(current.CaseId, to, OfficerActor, cleanNote, occurred);

        // CADA transición legal = evento append-only (ADR 0037). Id único por
        // (case, destino) mantiene el dedupe del writer sin colisionar entre pasos.
        if (_audit is not null)
        {
            await _audit.WriteAsync(
                new AuditEvent(
                    Id: $"{current.CaseId}:{to}",
                    OccurredAtUtc: occurred.UtcDateTime,
                    ActorEmail: OfficerActor,
                    ActorName: "Funcionario de ventanilla",
                    Action: "gov.case-transition",
                    Resource: current.Radicado,
                    Outcome: "success",
                    Detail: $"{current.Status} → {to}: {cleanNote}"),
                cancellationToken);
        }

        return new CaseTransitionResult(resulting);
    }
}
