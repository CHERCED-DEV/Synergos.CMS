namespace Synergos.CMS.Interfaces;

/// <summary>
/// Formatea importes monetarios al patrón visual canónico de la tienda
/// (es-CO): separador de miles con punto, sin decimales, y la moneda del
/// importe. En pesos, con su símbolo: <c>1500000 → "$ 1.500.000"</c>; en
/// otra moneda, con su código ISO delante: <c>99000 USD → "USD 99.000"</c>.
/// </summary>
/// <remarks>
/// Seam introducida para unificar el render del precio que antes se
/// duplicaba inline en 6+ renderers Razor de Shop (cada uno con su
/// <c>.ToString("N0", es-CO)</c> + concatenación de moneda). La
/// implementación por defecto vive en
/// <c>Synergos.CMS.Application.Services.Impl.EsCoPriceFormatter</c> —
/// lógica pura con <c>CultureInfo.GetCultureInfo("es-CO")</c>, sin
/// dependencia de Umbraco ni AspNetCore (ADR 0002). La moneda default
/// sale de <c>CartSettings.Currency</c> (COP), inyectada como POCO por
/// el composer.
/// <para>Esto decía «seguido del código de moneda» y el formateador escribía el símbolo
/// <c>$</c> para TODA moneda: 99.000 USD salían «$ 99.000». La moneda viaja con el importe.</para>
/// </remarks>
public interface IPriceFormatter
{
    /// <summary>
    /// Devuelve el importe formateado en es-CO (miles con punto, sin
    /// decimales) con su moneda: el símbolo si es la de la cultura, el
    /// código ISO delante si es otra. Si <paramref name="currency"/>
    /// es <c>null</c> o vacío, usa la moneda default
    /// (<c>CartSettings.Currency</c>).
    /// </summary>
    /// <param name="amount">Importe a formatear. Se redondea a entero
    ///   (sin decimales) según el patrón visual de la tienda.</param>
    /// <param name="currency">Código ISO 4217 (e.g. "COP", "USD"). Si
    ///   <c>null</c>/vacío, cae a la moneda default.</param>
    /// <returns>Cadena lista para render, e.g. <c>"$ 1.500.000"</c> o <c>"USD 99.000"</c>.</returns>
    string Format(decimal amount, string? currency = null);
}
