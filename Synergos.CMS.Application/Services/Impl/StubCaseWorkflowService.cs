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
    private readonly ITransactionalNotifier? _notifier;
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
        _cases = cases ?? throw new ArgumentNullException(nameof(cases));
        _audit = audit;
        _notifier = notifier;
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
            // Re-emite: el ledger del dispatcher deduplica por (caso, transición), así que
            // es inofensivo, y rescata el caso en que la primera decisión no llegó a
            // notificar (notificaciones apagadas entonces, destinatario inválido, etc.).
            await EmitDecidedAsync(current, rule.To, _now(), cancellationToken);
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
            // best-effort: la decisión YA está aplicada y persistida.
            await BestEffort.RunAsync(() => _audit.WriteAsync(
                    new AuditEvent(
                        Id: $"{current.CaseId}:{rule.To}",
                        OccurredAtUtc: occurred.UtcDateTime,
                        ActorEmail: OfficerActor,
                        ActorName: OfficerName,
                        Action: "gov.case-decision",
                        Resource: current.Radicado,
                        Outcome: "success",
                        Detail: $"{GovStatusSlugs.ToSlug(current.Status)} → {GovStatusSlugs.ToSlug(rule.To)} ({outcome.Trim()}): {cleanNote}"),
                    cancellationToken), cancellationToken);
        }

        // Avisarle al ciudadano el resultado de la decisión (T4). Best-effort: un email
        // caído JAMÁS puede tumbar una decisión ya aplicada y persistida.
        await EmitDecidedAsync(updated, rule.To, occurred, cancellationToken);

        return updated;
    }

    /// <summary>
    /// El hecho "expediente decidido" para T4.
    /// <para>
    /// <b>DedupeKey con la transición</b> (<c>gov.case.decided:{radicado}:{statusSlug}</c>):
    /// el SubjectId NO identifica el hecho — un mismo expediente pasa por varias
    /// transiciones (revisión → subsanación → aprobado) y CADA una merece su aviso. Con el
    /// default (<c>{Type}:{SubjectId}</c>) el ciudadano solo se enteraría de la primera.
    /// </para>
    /// <para>
    /// <b>Destinatario ausente:</b> el ciudadano se resuelve de las respuestas del
    /// formulario y su email puede venir vacío. En ese caso NO se emite (ni se inventa un
    /// placeholder): un evento sin destinatario real solo ensuciaría el ledger del
    /// dispatcher con una clave de hecho ya "vista", tapando un reintento legítimo.
    /// </para>
    /// </summary>
    private Task EmitDecidedAsync(
        CaseDetail @case,
        CaseStatus to,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(@case.Citizen?.Email))
        {
            return Task.CompletedTask;
        }

        var notification = new NotificationEvent(
            Type: NotificationTypes.GovCaseDecided,
            SubjectId: @case.CaseId,
            ToEmail: @case.Citizen.Email,
            ToName: @case.Citizen.Name,
            Code: @case.Radicado,                      // el radicado es el comprobante que guarda
            OccurredAt: occurredAt,
            Data: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Trámite"] = @case.TramiteName,
                ["Estado"] = @case.CurrentStage,
            },
            ActionPath: $"/gobierno/tramites/{@case.Radicado}",
            DedupeKey: $"{NotificationTypes.GovCaseDecided}:{@case.Radicado}:{GovStatusSlugs.ToSlug(to)}");

        return NotificationEmission.SafeDispatchAsync(_notifier, notification, cancellationToken);
    }

    private static string DefaultNote(CaseStatus to) => to switch
    {
        CaseStatus.Resuelto => "Solicitud aprobada.",
        CaseStatus.Rechazado => "Solicitud rechazada.",
        CaseStatus.Subsanacion => "Se solicita información adicional.",
        _ => $"Transición a {GovStatusSlugs.ToSlug(to)}.",
    };
}
