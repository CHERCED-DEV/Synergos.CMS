namespace Synergos.CMS.Interfaces;

/// <summary>
/// Tasa/derecho calculada de un trámite. <see cref="Exempt"/> = true cuando el trámite
/// es gratuito (<see cref="Amount"/> = 0) — el motor apaga el paso de pago (caso
/// <c>seleccionar → confirmar</c> sin pago, ya validado en Propiedades).
/// </summary>
public sealed record GovFeeQuote(
    decimal Amount,
    string Currency,
    bool Exempt);

/// <summary>
/// Calculadora de tasas/derechos del vertical Gobierno (doc gobierno-app-spec §4).
/// Decide si un trámite cobra y cuánto. Es lógica PURA/DETERMINISTA (sin estado): la
/// tarifa sale de una tabla sembrada por trámite; algunos trámites exoneran (Exempt).
/// </summary>
/// <remarks>
/// Lógica pura en <c>Synergos.CMS.Application</c> — cero dependencia de
/// Umbraco/AspNetCore (ADR 0002), igual que <c>StubMortgageCalculator</c> de
/// Propiedades. El adapter real (tarifario oficial de la entidad / reglas de
/// exoneración por condición del solicitante) implementa la misma seam sin tocar el
/// motor. ADR 0075.
/// </remarks>
public interface IGovFeeCalculator
{
    /// <summary>
    /// Calcula la tasa del trámite <paramref name="tramiteId"/>. Trámite gratuito o
    /// desconocido → <see cref="GovFeeQuote"/> con Amount 0 y Exempt true.
    /// </summary>
    GovFeeQuote Calculate(string tramiteId, decimal listedFee, string currency);
}
