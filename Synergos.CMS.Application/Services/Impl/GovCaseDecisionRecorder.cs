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
    /// <summary>El actor de demo con que se firmaba toda decisión antes de 009 (D-5).</summary>
    /// <remarks>
    /// <b>Ya no firma lo que hace una persona</b>: cada acción llega con la de la sesión
    /// (<see cref="CaseActor"/>) y es ésa la que queda en la historia, la decisión y la
    /// auditoría. Se conserva declarado (superset) porque es el correo con que los expedientes
    /// sembrados y los anteriores cuentan quién decidió, y como respaldo de quien llama sin
    /// identidad ninguna (<see cref="Firma"/>).
    /// </remarks>
    public const string OfficerActor = "funcionario@entidad.gov.co";

    /// <summary>Las acciones que son una decisión del funcionario (el resto, tomar y subsanar, son pasos del trámite).</summary>
    private static readonly HashSet<string> Decisiones = new(StringComparer.OrdinalIgnoreCase)
    {
        "approve", "reject", "request-info",
    };

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
    /// Aplica la acción al expediente, la asienta y avisa. Devuelve el expediente actualizado.
    /// </summary>
    /// <param name="current">El expediente como estaba ANTES, para poder contar de dónde salió.</param>
    /// <param name="accion">Cómo se llama la acción en el proceso: <c>take | approve | reject | request-info</c>
    /// (y <c>resubmit</c>, la del ciudadano).</param>
    /// <param name="to">A qué estado va.</param>
    /// <param name="note">La nota de quien actúa, o vacío para la de por defecto.</param>
    /// <param name="occurredAt">Cuándo.</param>
    /// <param name="actor">Quien actúa: la persona de la sesión (D-5 de 009). Es la que firma la historia, la
    /// decisión y el asiento de auditoría.</param>
    /// <param name="cancellationToken">Corta el asiento y el aviso, nunca la acción ya aplicada.</param>
    public async Task<CaseDetail> RecordAsync(
        CaseDetail current,
        string accion,
        CaseStatus to,
        string note,
        DateTimeOffset occurredAt,
        CaseActor actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(actor);

        var limpio = accion.Trim().ToLowerInvariant();
        var cleanNote = string.IsNullOrWhiteSpace(note) ? DefaultNote(limpio, to) : note.Trim();
        var firma = Firma(actor);

        // La decisión terminal (approve/reject) deja registro de resolución; el resto no.
        var decision = to is CaseStatus.Resuelto or CaseStatus.Rechazado
            ? new CaseDecision(limpio, cleanNote, occurredAt, firma)
            : null;

        var updated = _cases.ApplyDecision(current.CaseId, to, firma, cleanNote, occurredAt, decision);

        // CADA acción legal = evento append-only (ADR 0037). El id lleva el instante, no sólo el
        // destino: con la vuelta de la subsanación un expediente pasa dos veces por «en revisión»,
        // y un id por (expediente, destino) haría que el escritor descartara la segunda como
        // repetida. Repetir la MISMA acción no llega hasta acá (es idempotente antes).
        if (_audit is not null)
        {
            await BestEffort.RunAsync(() => _audit.WriteAsync(
                    new AuditEvent(
                        Id: $"{current.CaseId}:{to}:{occurredAt.UtcTicks}",
                        OccurredAtUtc: occurredAt.UtcDateTime,
                        ActorEmail: string.IsNullOrWhiteSpace(actor.Email) ? firma : actor.Email.Trim(),
                        ActorName: string.IsNullOrWhiteSpace(actor.DisplayName) ? firma : actor.DisplayName,
                        Action: Decisiones.Contains(limpio) ? "gov.case-decision" : "gov.case-transition",
                        Resource: current.Radicado,
                        Outcome: "success",
                        Detail: $"{GovStatusSlugs.ToSlug(current.Status)} → {GovStatusSlugs.ToSlug(to)} ({limpio}): {cleanNote}"),
                    cancellationToken), cancellationToken);
        }

        await NotifyAsync(updated, to, occurredAt, cancellationToken);
        return updated;
    }

    /// <summary>
    /// Cómo firma una persona en la historia del expediente: su correo, o su nombre, o su identidad.
    /// </summary>
    /// <remarks>
    /// Sólo quien llama sin identidad ninguna —una herramienta, nunca una sesión: las rutas exigen
    /// una— cae al <see cref="OfficerActor"/>, que es el respaldo declarado.
    /// </remarks>
    public static string Firma(CaseActor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (!string.IsNullOrWhiteSpace(actor.Email)) return actor.Email.Trim();
        if (!string.IsNullOrWhiteSpace(actor.DisplayName)) return actor.DisplayName.Trim();
        return string.IsNullOrWhiteSpace(actor.Id) ? OfficerActor : $"{actor.Kind}:{actor.Id.Trim()}";
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
    /// <para><b>Y con la vuelta, cuando no es la primera</b> (<c>…:{statusSlug}:{n}</c>, 009): desde que la
    /// subsanación vuelve a revisión, un expediente puede pedir información dos veces, y la segunda
    /// petición tenía la misma clave que la primera — el ledger la daba por vista y el ciudadano no se
    /// enteraba. La primera entrada a un estado conserva la clave de siempre, así que lo ya avisado no
    /// se vuelve a avisar; repetir la misma acción no cambia la cuenta, así que sigue sin duplicar.</para>
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
            DedupeKey: ClaveDelAviso(@case.Radicado, to, _cases.VecesEn(@case.CaseId, to)),
            // Entrar a revisión (tomarlo, o la subsanación que vuelve) no es una decisión: su propio texto.
            Variant: to == CaseStatus.EnRevision ? NotificationVariants.GovCaseInReview : null);

        return NotificationEmission.SafeDispatchAsync(_notifier, notification, cancellationToken);
    }

    /// <summary>
    /// La clave del aviso de la <paramref name="vez"/>-ésima entrada del expediente en <paramref name="to"/>:
    /// la de siempre la primera vez, con la vuelta a partir de la segunda.
    /// </summary>
    public static string ClaveDelAviso(string radicado, CaseStatus to, int vez)
    {
        var clave = $"{NotificationTypes.GovCaseDecided}:{radicado}:{GovStatusSlugs.ToSlug(to)}";
        return vez > 1 ? $"{clave}:{vez}" : clave;
    }

    /// <summary>La nota de por defecto de una acción cuando quien actúa no escribió ninguna.</summary>
    public static string DefaultNote(string accion, CaseStatus to) => accion?.Trim().ToLowerInvariant() switch
    {
        "take" => "Expediente tomado para revisión.",
        "resubmit" => "El ciudadano envió la subsanación.",
        _ => DefaultNote(to),
    };

    /// <summary>La nota de por defecto cuando el funcionario no escribió ninguna.</summary>
    public static string DefaultNote(CaseStatus to) => to switch
    {
        CaseStatus.Resuelto => "Solicitud aprobada.",
        CaseStatus.Rechazado => "Solicitud rechazada.",
        CaseStatus.Subsanacion => "Se solicita información adicional.",
        _ => $"Transición a {GovStatusSlugs.ToSlug(to)}.",
    };
}
