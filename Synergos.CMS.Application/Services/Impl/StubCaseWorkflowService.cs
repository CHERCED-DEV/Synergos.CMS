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
    // outcome → (estado destino, estados de origen legales).
    //
    // ESTA TABLA ES LO QUE SE MUDA A Api.Workflow (HU #44). Vive acá y sólo acá mientras el
    // modo sea Stub: tenerla en los dos sitios haría que un trámite avanzara distinto según
    // por dónde se pregunte, que es peor que no haberla mudado.
    private static readonly IReadOnlyDictionary<string, (CaseStatus To, CaseStatus[] From)> Outcomes =
        new Dictionary<string, (CaseStatus, CaseStatus[])>(StringComparer.OrdinalIgnoreCase)
        {
            ["approve"] = (CaseStatus.Resuelto, new[] { CaseStatus.Radicado, CaseStatus.EnRevision, CaseStatus.Subsanacion }),
            ["reject"] = (CaseStatus.Rechazado, new[] { CaseStatus.Radicado, CaseStatus.EnRevision, CaseStatus.Subsanacion }),
            ["request-info"] = (CaseStatus.Subsanacion, new[] { CaseStatus.Radicado, CaseStatus.EnRevision }),
        };

    private readonly GovCaseDecisionRecorder _recorder;
    private readonly Func<DateTimeOffset> _now;

    public StubCaseWorkflowService(StubApplicationService cases)
        : this(cases, null, null)
    {
    }

    /// <summary>
    /// Ctor configurable: audit opcional (null = no-op, tests aislados) + time source
    /// inyectable para determinismo en tests (ADR 0002) + notifier opcional (T4) ÚLTIMO:
    /// null = no notificar (comportamiento pre-T4), cero call-sites rotos.
    /// </summary>
    public StubCaseWorkflowService(
        StubApplicationService cases,
        IAuditTrailWriter? audit,
        Func<DateTimeOffset>? now,
        ITransactionalNotifier? notifier = null)
    {
        _recorder = new GovCaseDecisionRecorder(cases, audit, notifier);
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

        var current = _recorder.Find(caseId)
            ?? throw new ArgumentException($"Expediente '{caseId.Trim()}' no encontrado.", nameof(caseId));

        // Idempotente: si el expediente YA está en el estado destino, devolverlo sin
        // re-auditar ni tocar el historial (núcleo anti-doble-efecto del dominio).
        if (current.Status == rule.To)
        {
            // Re-emite: el ledger del dispatcher deduplica por (caso, transición), así que
            // es inofensivo, y rescata el caso en que la primera decisión no llegó a
            // notificar (notificaciones apagadas entonces, destinatario inválido, etc.).
            await _recorder.NotifyAsync(current, rule.To, _now(), cancellationToken);
            return current;
        }

        if (!rule.From.Contains(current.Status))
        {
            throw new InvalidOperationException(
                $"Decisión ilegal: el expediente {current.Radicado} está en '{GovStatusSlugs.ToSlug(current.Status)}' y no admite '{outcome}'.");
        }

        return await _recorder.RecordAsync(current, outcome, rule.To, note, _now(), cancellationToken);
    }
}
