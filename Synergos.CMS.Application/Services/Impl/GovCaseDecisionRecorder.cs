using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Anota una decisión YA declarada legal: mueve el expediente, la asienta y avisa.
/// </summary>
/// <remarks>
/// <para><b>Es la mitad que NO cambia según quién decida la legalidad</b> (HU #44). La tabla de
/// transiciones se va a <c>Api.Workflow</c>; lo de acá se queda, porque el expediente vive de
/// este lado —igual que la entrada de un evento se queda en el CMS aunque el aforo lo lleve
/// <c>Api.Inventory</c>—. Sin este sitio, el camino HTTP tendría que copiar el asiento de
/// auditoría y el aviso al ciudadano, y el día que uno de los dos cambiara sólo cambiaría en
/// uno.</para>
///
/// <para><b>Qué NO decide.</b> No sabe si una transición es legal ni si hace falta hacerla: eso
/// lo resuelve quien llama, que es justamente lo que se está mudando. Acá sólo se anota.</para>
///
/// <para><b>El asiento y el aviso son best-effort, y el orden importa.</b> Cuando llegan, la
/// decisión YA está aplicada y persistida: una auditoría caída o un correo rebotado no pueden
/// tumbar una decisión que el expediente ya refleja. Al revés —avisar antes de aplicar— dejaría
/// al ciudadano leyendo que su trámite se resolvió mientras el expediente dice otra cosa.</para>
/// </remarks>
public sealed class GovCaseDecisionRecorder
{
    /// <summary>Actor de demo de la cara de funcionario (la UI conmuta rol, no identidad).</summary>
    /// <remarks>
    /// <b>Sigue cableado, y hay que decirlo</b> (HU #44 §4). <c>Api.Workflow</c> sabe de roles
    /// —<c>RequiredRoles</c> es lo que separa radicar de aprobar— así que decidir con un actor de
    /// demo desperdicia la única guarda que la capacidad ofrece. Es el primer caso de producto
    /// que pide la HU #14 fuera de <c>Api.Messaging</c>.
    /// </remarks>
    public const string OfficerActor = "funcionario@entidad.gov.co";

    private const string OfficerName = "Funcionario de ventanilla";

    private readonly StubApplicationService _cases;
    private readonly IAuditTrailWriter? _audit;
    private readonly ITransactionalNotifier? _notifier;

    public GovCaseDecisionRecorder(
        StubApplicationService cases,
        IAuditTrailWriter? audit = null,
        ITransactionalNotifier? notifier = null)
    {
        _cases = cases ?? throw new ArgumentNullException(nameof(cases));
        _audit = audit;
        _notifier = notifier;
    }

    /// <summary>Busca el expediente por id o por radicado.</summary>
    public CaseDetail? Find(string caseIdOrRadicado) => _cases.FindCase(caseIdOrRadicado);

    /// <summary>
    /// Aplica la decisión al expediente, la asienta y avisa. Devuelve el expediente actualizado.
    /// </summary>
    /// <param name="current">El expediente como estaba ANTES, para poder contar de dónde salió.</param>
    /// <param name="outcome">Cómo lo llamó quien decidió: <c>approve | reject | request-info</c>.</param>
    /// <param name="to">A qué estado va.</param>
    /// <param name="note">La nota del funcionario, o vacío para la de por defecto.</param>
    /// <param name="occurredAt">Cuándo.</param>
    /// <param name="cancellationToken">Corta el asiento y el aviso, nunca la decisión ya aplicada.</param>
    public async Task<CaseDetail> RecordAsync(
        CaseDetail current,
        string outcome,
        CaseStatus to,
        string note,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(current);

        var cleanNote = string.IsNullOrWhiteSpace(note) ? DefaultNote(to) : note.Trim();
        var limpio = outcome.Trim().ToLowerInvariant();

        // La decisión terminal (approve/reject) deja registro de resolución; request-info no.
        var decision = to is CaseStatus.Resuelto or CaseStatus.Rechazado
            ? new CaseDecision(limpio, cleanNote, occurredAt, OfficerActor)
            : null;

        var updated = _cases.ApplyDecision(current.CaseId, to, OfficerActor, cleanNote, occurredAt, decision);

        // CADA decisión legal = evento append-only (ADR 0037). Id único por (case, destino)
        // mantiene el dedupe del writer sin colisionar entre pasos.
        if (_audit is not null)
        {
            await BestEffort.RunAsync(() => _audit.WriteAsync(
                    new AuditEvent(
                        Id: $"{current.CaseId}:{to}",
                        OccurredAtUtc: occurredAt.UtcDateTime,
                        ActorEmail: OfficerActor,
                        ActorName: OfficerName,
                        Action: "gov.case-decision",
                        Resource: current.Radicado,
                        Outcome: "success",
                        Detail: $"{GovStatusSlugs.ToSlug(current.Status)} → {GovStatusSlugs.ToSlug(to)} ({limpio}): {cleanNote}"),
                    cancellationToken), cancellationToken);
        }

        await NotifyAsync(updated, to, occurredAt, cancellationToken);
        return updated;
    }

    /// <summary>
    /// El aviso «expediente decidido», suelto para poder re-emitirlo sin volver a decidir.
    /// </summary>
    /// <remarks>
    /// <para><b>DedupeKey con la transición</b> (<c>gov.case.decided:{radicado}:{statusSlug}</c>):
    /// el SubjectId NO identifica el hecho — un mismo expediente pasa por varias transiciones
    /// (revisión → subsanación → aprobado) y CADA una merece su aviso. Con el default
    /// (<c>{Type}:{SubjectId}</c>) el ciudadano sólo se enteraría de la primera.</para>
    ///
    /// <para><b>Destinatario ausente:</b> el ciudadano sale de las respuestas del formulario y su
    /// correo puede venir vacío. En ese caso NO se emite, ni se inventa un placeholder: un evento
    /// sin destinatario real sólo ensuciaría el ledger del dispatcher con una clave de hecho ya
    /// «vista», tapando un reintento legítimo.</para>
    /// </remarks>
    public Task NotifyAsync(
        CaseDetail @case,
        CaseStatus to,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken = default)
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

    /// <summary>La nota de por defecto cuando el funcionario no escribió ninguna.</summary>
    public static string DefaultNote(CaseStatus to) => to switch
    {
        CaseStatus.Resuelto => "Solicitud aprobada.",
        CaseStatus.Rechazado => "Solicitud rechazada.",
        CaseStatus.Subsanacion => "Se solicita información adicional.",
        _ => $"Transición a {GovStatusSlugs.ToSlug(to)}.",
    };
}
