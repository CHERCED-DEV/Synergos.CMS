using System.Globalization;

namespace Synergos.CMS.Application.Dinero;

/// <summary>
/// Cómo se escribe un importe para una persona: con SU moneda, que viaja con él.
/// </summary>
/// <remarks>
/// <para>En la moneda de la cultura, con su símbolo y sin decimales —<c>$ 480.000</c> en es-CO—; en
/// otra, con su código ISO delante y los miles de la cultura —<c>USD 1.200</c>—, para que no se lea
/// como la local.</para>
///
/// <para><b>Una sola pieza para las dos maneras en que el CMS escribe importes</b>: las APIs
/// (<c>EsCoPriceFormatter</c>, con la cultura del producto) y los listados que arma el servidor
/// (<c>FormatoDelListado.Precio</c>, con la de la página). La regla ya la seguían los listados; el
/// formateador de las APIs IGNORABA la moneda y escribía «$ 99.000» para 99.000 USD, que en un
/// sitio colombiano se lee como pesos. Dos copias de la misma regla son exactamente cómo una se
/// queda atrás.</para>
/// </remarks>
public static class TextoDelImporte
{
    /// <summary>El importe <paramref name="monto"/> en <paramref name="moneda"/>, escrito en <paramref name="cultura"/>.</summary>
    /// <param name="moneda">Código ISO-4217, sin importar mayúsculas ni espacios. Vacío: la de la cultura.</param>
    public static string En(decimal monto, string? moneda, CultureInfo cultura)
    {
        ArgumentNullException.ThrowIfNull(cultura);
        var codigo = moneda?.Trim().ToUpperInvariant() ?? string.Empty;
        return codigo.Length == 0 || string.Equals(codigo, MonedaDe(cultura), StringComparison.Ordinal)
            ? monto.ToString("C0", cultura)
            : $"{codigo} {monto.ToString("N0", cultura)}";
    }

    private static string MonedaDe(CultureInfo cultura)
    {
        try
        {
            return new RegionInfo(cultura.Name).ISOCurrencySymbol;
        }
        catch (ArgumentException)
        {
            return string.Empty;
        }
    }
}
