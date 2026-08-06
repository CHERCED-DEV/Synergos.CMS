namespace Synergos.CMS.Interfaces;

/// <summary>
/// Resultado de evaluar la política de cancelación de un rate plan a una
/// fecha dada: si es reembolsable, la penalidad a cobrar y una descripción
/// editor-/huésped-facing.
/// </summary>
public sealed record CancellationOutcome(bool Refundable, decimal PenaltyAmount, string Description);

/// <summary>
/// Evalúa la política de cancelación de un rate plan: dado el <c>ratePlanCode</c>,
/// el <c>checkIn</c> y la fecha <c>asOf</c> de la cancelación, calcula la
/// penalidad. Es la pieza del MOTOR que materializa Refundable vs Non-refundable.
/// </summary>
/// <remarks>
/// Seam stub-first (igual que <see cref="IPaymentProvider"/>): el default
/// <c>StubCancellationPolicyEvaluator</c> (Application, lógica pura y
/// determinista) aplica reglas simples (non-refundable → penalidad total;
/// refundable → 0 si se cancela con &gt;1 día de antelación, si no penalidad
/// de 1 noche); el adapter real lee las condiciones del rate plan del CMS/PMS
/// sin tocar el motor. ADR 0002 (Application sin Umbraco).
/// </remarks>
public interface ICancellationPolicyEvaluator
{
    /// <summary>
    /// Evalúa la cancelación de <paramref name="ratePlanCode"/> con check-in
    /// <paramref name="checkIn"/> hecha el día <paramref name="asOf"/>.
    /// </summary>
    CancellationOutcome Evaluate(string ratePlanCode, DateOnly checkIn, DateOnly asOf);
}
