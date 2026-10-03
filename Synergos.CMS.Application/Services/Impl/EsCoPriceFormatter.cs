using System.Globalization;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Application.Dinero;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Default <see cref="IPriceFormatter"/>: el importe en la cultura del producto, es-CO, con SU
/// moneda — <c>$ 1.500.000</c> en pesos, <c>USD 99.000</c> en dólares.
/// </summary>
/// <remarks>
/// Vive en <c>Synergos.CMS.Application</c> — cero dependencia de
/// Umbraco/AspNetCore (ADR 0002). La moneda default llega como POCO
/// <see cref="CartSettings"/>; el composer en Web extrae
/// <c>IOptions&lt;CartSettings&gt;.Value</c> y la inyecta, honrando la
/// decisión de no referenciar <c>Microsoft.Extensions.Options</c> desde
/// Application (mismo patrón que <c>AppsettingsFeatureGate</c>).
/// <para><b>La moneda se ignoraba</b> («la demo es mono-moneda COP»): 99.000 USD salían
/// «$ 99.000», que en un sitio colombiano se lee como pesos. La regla es
/// <see cref="TextoDelImporte"/>, la misma de los listados del servidor: la moneda viaja con el
/// importe.</para>
/// </remarks>
public sealed class EsCoPriceFormatter : IPriceFormatter
{
    private static readonly CultureInfo EsCo = CultureInfo.GetCultureInfo("es-CO");

    private readonly string _defaultCurrency;

    public EsCoPriceFormatter(CartSettings cartSettings)
    {
        _defaultCurrency = string.IsNullOrWhiteSpace(cartSettings.Currency)
            ? "COP"
            : cartSettings.Currency.Trim();
    }

    public string Format(decimal amount, string? currency = null)
        => TextoDelImporte.En(amount, string.IsNullOrWhiteSpace(currency) ? _defaultCurrency : currency, EsCo);
}
