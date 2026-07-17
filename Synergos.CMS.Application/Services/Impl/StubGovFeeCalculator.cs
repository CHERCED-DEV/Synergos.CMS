using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Default <see cref="IGovFeeCalculator"/> — calculadora STUB de tasas/derechos del
/// vertical Gobierno (doc gobierno-app-spec §4). PURA/DETERMINISTA (sin estado, igual
/// que <c>StubMortgageCalculator</c> de Propiedades): la tarifa listada del catálogo
/// decide — tasa ≤ 0 o trámite desconocido ⇒ exento (Amount 0, el motor apaga el paso
/// de pago); tasa &gt; 0 ⇒ se cobra tal cual.
/// </summary>
/// <remarks>
/// Lógica pura en <c>Synergos.CMS.Application</c> — cero dependencia de
/// Umbraco/AspNetCore (ADR 0002). El adapter real (tarifario oficial de la entidad +
/// reglas de exoneración por condición del solicitante) implementa la misma seam sin
/// tocar el motor. ADR 0075.
/// </remarks>
public sealed class StubGovFeeCalculator : IGovFeeCalculator
{
    private const string DefaultCurrency = "COP";

    public GovFeeQuote Calculate(string tramiteId, decimal listedFee, string currency)
    {
        var effectiveCurrency = string.IsNullOrWhiteSpace(currency) ? DefaultCurrency : currency.Trim();

        if (string.IsNullOrWhiteSpace(tramiteId) || listedFee <= 0m)
        {
            return new GovFeeQuote(0m, effectiveCurrency, Exempt: true);
        }

        return new GovFeeQuote(listedFee, effectiveCurrency, Exempt: false);
    }
}
