namespace Synergos.CMS.Interfaces;

/// <summary>
/// Máquina de estados del expediente del vertical Gobierno (doc gobierno.md §4 — cara de
/// funcionario). Es la pieza del MOTOR que hace AVANZAR el expediente resolviendo un
/// <c>outcome</c> del funcionario (<c>approve | reject | request-info</c>) a la
/// transición correspondiente, validando que sea legal (State pattern). Cada decisión se
/// audita y muta el estado + la timeline + la decisión del expediente.
/// </summary>
/// <remarks>
/// Lógica pura en <c>Synergos.CMS.Application</c> — cero dependencia de
/// Umbraco/AspNetCore (ADR 0002). REUSA <see cref="IAuditTrailWriter"/> (ADR 0037):
/// cada decisión legal es un evento append-only <c>gov.case-decision</c> — el rastro
/// forense es el diferenciador del dominio. <see cref="DecideAsync"/> es IDEMPOTENTE
/// sobre el estado destino: aplicar una decisión que deja el expediente en el estado en
/// el que YA está devuelve el expediente sin re-auditar ni re-ejecutar (núcleo
/// anti-doble-efecto, caso central del dominio). Los outcomes ilegales (p. ej. aprobar
/// un expediente ya rechazado) lanzan <see cref="InvalidOperationException"/>. El estado
/// del expediente se comparte con <see cref="IApplicationService"/> /
/// <see cref="ICaseTrackingProvider"/> vía composición (DIP). ADR 0075.
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
    Task<CaseDetail> DecideAsync(
        string caseId,
        string outcome,
        string note,
        CancellationToken cancellationToken = default);
}
