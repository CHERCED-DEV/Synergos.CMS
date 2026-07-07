using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Default <see cref="ICaseWorkflowService"/> — máquina de estados STUB del expediente
/// del vertical Gobierno (doc gobierno.md §4, cara de funcionario). Resuelve el
/// <c>outcome</c> del funcionario (<c>approve | reject | request-info</c>) a la
/// transición correspondiente, valida que sea legal (tabla explícita, State pattern),
/// es IDEMPOTENTE sobre el estado destino y asienta CADA decisión legal como evento
/// append-only en <see cref="IAuditTrailWriter"/> — el rastro forense es el
/// diferenciador del dominio.
/// </summary>
/// <remarks>
/// Lógica pura en <c>Synergos.CMS.Application</c> — cero dependencia de
/// Umbraco/AspNetCore (ADR 0002). NO duplica estado: muta el agregado de
/// <see cref="StubApplicationService"/> por composición (DIP, mismo patrón
/// <c>StubEventManagementService</c> → <c>StubEventTicketingService</c>). Outcomes:
/// <c>approve</c> (→ Resuelto) y <c>reject</c> (→ Rechazado) legales desde Radicado o
/// EnRevision o Subsanacion; <c>request-info</c> (→ Subsanacion) legal desde Radicado o
/// EnRevision. Aplicar un outcome cuyo estado destino YA es el actual es idempotente
/// (no re-audita ni toca el historial). Los estados terminales (Resuelto/Rechazado) no
/// admiten nuevas decisiones ⇒ <see cref="InvalidOperationException"/>. ADR 0075
/// (idempotent es el caso central).
/// </remarks>
public sealed class StubCaseWorkflowService : ICaseWorkflowService
{
    /// <summary>Actor de demo de la cara de funcionario (la UI conmuta rol, no identidad).</summary>
    private const string OfficerActor = "funcionario@entidad.gov.co";
    private const string OfficerName = "Funcionario de ventanilla";

    // outcome → (estado destino, estados de origen legales).
    private static readonly IReadOnlyDictionary<string, (CaseStatus To, CaseStatus[] From)> Outcomes =
        new Dictionary<string, (CaseStatus, CaseStatus[])>(StringComparer.OrdinalIgnoreCase)
        {
            ["approve"] = (CaseStatus.Resuelto, new[] { CaseStatus.Radicado, CaseStatus.EnRevision, CaseStatus.Subsanacion }),
            ["reject"] = (CaseStatus.Rechazado, new[] { CaseStatus.Radicado, CaseStatus.EnRevision, CaseStatus.Subsanacion }),
            ["request-info"] = (CaseStatus.Subsanacion, new[] { CaseStatus.Radicado, CaseStatus.EnRevision }),
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

    public async Task<CaseDetail> DecideAsync(
        string caseId,
        string outcome,
        string note,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(caseId))
        {
            throw new ArgumentException("El expediente es obligatorio.", nameof(caseId));
        }
        if (string.IsNullOrWhiteSpace(outcome) || !Outcomes.TryGetValue(outcome.Trim(), out var rule))
        {
            throw new ArgumentException(
                "outcome es requerido: approve | reject | request-info.", nameof(outcome));
        }

        var current = _cases.FindCase(caseId)
            ?? throw new ArgumentException($"Expediente '{caseId.Trim()}' no encontrado.", nameof(caseId));

        // Idempotente: si el expediente YA está en el estado destino, devolverlo sin
        // re-auditar ni tocar el historial (núcleo anti-doble-efecto del dominio).
        if (current.Status == rule.To)
        {
            return current;
        }

        if (!rule.From.Contains(current.Status))
        {
            throw new InvalidOperationException(
                $"Decisión ilegal: el expediente {current.Radicado} está en '{GovStatusSlugs.ToSlug(current.Status)}' y no admite '{outcome}'.");
        }

        var occurred = _now();
        var cleanNote = string.IsNullOrWhiteSpace(note) ? DefaultNote(rule.To) : note.Trim();

        // La decisión terminal (approve/reject) deja registro de resolución; request-info no.
        var decision = rule.To is CaseStatus.Resuelto or CaseStatus.Rechazado
            ? new CaseDecision(outcome.Trim().ToLowerInvariant(), cleanNote, occurred, OfficerActor)
            : null;

        var updated = _cases.ApplyDecision(current.CaseId, rule.To, OfficerActor, cleanNote, occurred, decision);

        // CADA decisión legal = evento append-only (ADR 0037). Id único por
        // (case, destino) mantiene el dedupe del writer sin colisionar entre pasos.
        if (_audit is not null)
        {
            await _audit.WriteAsync(
                new AuditEvent(
                    Id: $"{current.CaseId}:{rule.To}",
                    OccurredAtUtc: occurred.UtcDateTime,
                    ActorEmail: OfficerActor,
                    ActorName: OfficerName,
                    Action: "gov.case-decision",
                    Resource: current.Radicado,
                    Outcome: "success",
                    Detail: $"{GovStatusSlugs.ToSlug(current.Status)} → {GovStatusSlugs.ToSlug(rule.To)} ({outcome.Trim()}): {cleanNote}"),
                cancellationToken);
        }

        return updated;
    }

    private static string DefaultNote(CaseStatus to) => to switch
    {
        CaseStatus.Resuelto => "Solicitud aprobada.",
        CaseStatus.Rechazado => "Solicitud rechazada.",
        CaseStatus.Subsanacion => "Se solicita información adicional.",
        _ => $"Transición a {GovStatusSlugs.ToSlug(to)}.",
    };
}
