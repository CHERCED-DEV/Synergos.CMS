namespace Synergos.CMS.Interfaces;

/// <summary>
/// Resultado de una transición del expediente: el estado resultante. Si la transición
/// es ilegal (no permitida por la máquina de estados) el servicio lanza
/// <see cref="InvalidOperationException"/> en vez de devolver un estado inválido.
/// </summary>
public sealed record CaseTransitionResult(CaseStatus Status);

/// <summary>
/// Máquina de estados del expediente del vertical Gobierno (doc gobierno-app-spec §4 —
/// cara de funcionario). Es la pieza del MOTOR que hace AVANZAR el expediente:
/// <c>radicado → en-revisión → subsanación → resuelto / rechazado</c>, validando que
/// cada transición sea legal (State pattern). Cada transición se audita.
/// </summary>
/// <remarks>
/// Lógica pura en <c>Synergos.CMS.Application</c> — cero dependencia de
/// Umbraco/AspNetCore (ADR 0002). REUSA <see cref="IAuditTrailWriter"/> (ADR 0037):
/// cada transición legal es un evento append-only <c>gov.case-transition</c> — el
/// rastro forense es el diferenciador del dominio. <see cref="TransitionAsync"/> es
/// IDEMPOTENTE: transicionar a un estado en el que el expediente YA está devuelve ese
/// estado sin re-auditar ni re-ejecutar (núcleo anti-doble-efecto, caso central del
/// dominio). Las transiciones ilegales (p. ej. resuelto → radicado, o saltar de
/// radicado a resuelto) lanzan <see cref="InvalidOperationException"/>. El estado del
/// expediente se comparte con <see cref="IApplicationService"/> /
/// <see cref="ICaseTrackingProvider"/> vía composición (DIP). ADR 0075.
/// </remarks>
public interface ICaseWorkflowService
{
    /// <summary>
    /// Transiciona el expediente <paramref name="caseId"/> al estado <paramref name="to"/>
    /// con una <paramref name="note"/>, si la transición es legal. Idempotente:
    /// transicionar al estado actual devuelve ese estado sin efecto. Lanza
    /// <see cref="ArgumentException"/> si el expediente no existe y
    /// <see cref="InvalidOperationException"/> si la transición es ilegal.
    /// </summary>
    Task<CaseTransitionResult> TransitionAsync(
        string caseId,
        CaseStatus to,
        string note,
        CancellationToken cancellationToken = default);
}
