using System.Globalization;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Default <see cref="IPriceFormatter"/>. Lógica pura es-CO: miles con
/// punto, sin decimales, seguido del código de moneda. Replica el
/// patrón visual que antes vivía inline en los renderers de Shop
/// (<c>amount.ToString("N0", es-CO) + " " + currency</c>).
/// </summary>
/// <remarks>
/// Vive en <c>Synergos.CMS.Application</c> — cero dependencia de
/// Umbraco/AspNetCore (ADR 0002). La moneda default llega como POCO
/// <see cref="CartSettings"/>; el composer en Web extrae
/// <c>IOptions&lt;CartSettings&gt;.Value</c> y la inyecta, honrando la
/// decisión de no referenciar <c>Microsoft.Extensions.Options</c> desde
/// Application (mismo patrón que <c>AppsettingsFeatureGate</c>).
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
    {
        var code = string.IsNullOrWhiteSpace(currency)
            ? _defaultCurrency
            : currency.Trim();

        return $"{amount.ToString("N0", EsCo)} {code}";
    }
}
