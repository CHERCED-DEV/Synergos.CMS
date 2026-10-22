namespace Synergos.CMS.Interfaces;

/// <summary>
/// Máquina de estados del expediente del vertical Gobierno (doc gobierno.md §4 — cara de
/// funcionario). Es la pieza del MOTOR que hace AVANZAR el expediente: el funcionario lo toma
/// (<c>take</c>) y decide (<c>approve | reject | request-info</c>), validando que cada acción sea
/// legal (State pattern). Cada acción se audita y muta el estado + la timeline del expediente.
/// </summary>
/// <remarks>
/// Lógica pura en <c>Synergos.CMS.Application</c> — cero dependencia de
/// Umbraco/AspNetCore (ADR 0002). REUSA <see cref="IAuditTrailWriter"/> (ADR 0037):
/// cada acción legal es un evento append-only — el rastro forense es el diferenciador del
/// dominio. Las acciones son IDEMPOTENTES sobre el estado destino: aplicar una que deja el
/// expediente en el estado en el que YA está devuelve el expediente sin re-auditar ni
/// re-ejecutar (núcleo anti-doble-efecto, caso central del dominio). Las ilegales (p. ej.
/// aprobar un expediente ya rechazado) lanzan <see cref="InvalidOperationException"/>. El
/// estado del expediente se comparte con <see cref="IApplicationService"/> /
/// <see cref="ICaseTrackingProvider"/> vía composición (DIP). ADR 0075.
/// <para><b>El proceso es un dato</b> (009-gobierno-tramite): las transiciones legales son las
/// de <c>gov.tramite.v2</c> en <c>tools/provisionar.procesos.json</c>, y un gate cruza con ella
/// los dos motores.</para>
/// </remarks>
public interface ICaseWorkflowService
{
    /// <summary>
    /// Resuelve el <paramref name="outcome"/> del funcionario (<c>approve | reject |
    /// request-info</c>) sobre el expediente <paramref name="caseId"/> con una
    /// <paramref name="note"/>, aplicando la transición si es legal, y devuelve el
    /// expediente actualizado (con su <see cref="CaseDetail.Decision"/> cuando el
    /// desenlace es terminal). Idempotente sobre el estado destino. Lanza
    /// <see cref="ArgumentException"/> si el expediente no existe o el outcome es
    /// desconocido, y <see cref="InvalidOperationException"/> si la transición es ilegal.
    /// </summary>
    /// <param name="actor">Quien decide: la persona de la sesión, que queda en la historia, la
    /// decisión y la auditoría (D-5 de 009).</param>
    Task<CaseDetail> DecideAsync(
        string caseId,
        string outcome,
        string note,
        CaseActor actor,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// El funcionario toma un expediente radicado: pasa a «en revisión» a su nombre (D-1 de 009).
    /// Idempotente sobre el estado destino; ilegal desde cualquier otro estado.
    /// </summary>
    Task<CaseDetail> TakeAsync(
        string caseId,
        CaseActor actor,
        string? note,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lo que el proceso del expediente permite desde su estado a la cara <paramref name="role"/>
    /// (<see cref="CaseRoles.Funcionario"/> o <see cref="CaseRoles.Ciudadano"/>), con los nombres de
    /// cable del proceso (<c>take | approve | reject | request-info | resubmit</c>). Cerrado = ninguna.
    /// </summary>
    /// <remarks>
    /// Es lo que la consola y el portal pintan, en vez de una lista fija (D-3 de 009): lo que se ofrece
    /// es exactamente lo que este mismo motor aceptaría. Lanza <see cref="ArgumentException"/> si el
    /// expediente no existe, e <see cref="InvalidOperationException"/> si no se puede consultar el
    /// proceso (la capacidad caída): ahí no se ofrece nada y no se cae a una tabla local.
    /// </remarks>
    Task<IReadOnlyList<string>> ActionsAsync(
        string caseId,
        string role,
        CancellationToken cancellationToken = default);
}

/// <summary>Quien actúa sobre un expediente: la persona de la sesión, nunca un actor de demo (D-5 de 009).</summary>
/// <param name="Kind">La cara con que actúa: <see cref="CaseRoles.KindFuncionario"/> o <see cref="CaseRoles.KindCiudadano"/>.</param>
/// <param name="Id">Su identidad estable (la clave del miembro).</param>
/// <param name="DisplayName">Cómo se la nombra en la historia y la auditoría.</param>
/// <param name="Email">Su correo, o vacío si no consta.</param>
public sealed record CaseActor(string Kind, string Id, string DisplayName, string Email);

/// <summary>Las dos caras del trámite, con los nombres que usa el proceso declarado.</summary>
public static class CaseRoles
{
    /// <summary>El rol que exigen las transiciones del funcionario.</summary>
    public const string Funcionario = "funcionario";

    /// <summary>El rol que exigen las transiciones del ciudadano.</summary>
    public const string Ciudadano = "ciudadano";

    /// <summary>Cómo se nombra al funcionario que actúa, ante la capacidad de procesos.</summary>
    public const string KindFuncionario = "gov.funcionario";

    /// <summary>Cómo se nombra al ciudadano que actúa, ante la capacidad de procesos.</summary>
    public const string KindCiudadano = "gov.ciudadano";
}
