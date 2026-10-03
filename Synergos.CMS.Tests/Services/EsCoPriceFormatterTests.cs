using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Cubre <see cref="EsCoPriceFormatter"/> (Bucket B / IPriceFormatter):
/// formato es-CO (miles con punto, sin decimales) + código de moneda,
/// con fallback a la moneda default de CartSettings.
/// </summary>
public class EsCoPriceFormatterTests
{
    private static IPriceFormatter Make(string currency = "COP")
        => new EsCoPriceFormatter(new CartSettings { Currency = currency });

    [Fact]
    public void Format_HappyPath_EsCoThousandsPlusCurrency()
    {
        Assert.Equal("$ 1.500.000", Make().Format(1_500_000m));
    }

    [Fact]
    public void Format_Zero_RendersZeroWithCurrency()
    {
        Assert.Equal("$ 0", Make().Format(0m));
    }

    [Fact]
    public void Format_RoundsToInteger_NoDecimals()
    {
        Assert.Equal("$ 89.000", Make().Format(89_000.49m));
    }

    /// <summary>
    /// Un importe en otra moneda lleva su código ISO delante y NO el símbolo del peso.
    /// </summary>
    /// <remarks>
    /// <para>La moneda se IGNORABA (<c>_ = …</c>, «la demo es mono-moneda COP»), así que 99.000 USD
    /// salían «$ 99.000»: en un sitio colombiano eso se lee como pesos, una cifra 4.000 veces menor.
    /// Este test fijaba ese comportamiento y su comentario anunciaba el día en que se pondría rojo
    /// para decidirlo a conciencia: la regla del repo es que la moneda viaja con el importe.</para>
    ///
    /// <para>Es la MISMA regla que ya seguían los listados del servidor
    /// (<c>FormatoDelListado.Precio</c>: «USD 1.200»), y ahora es una sola pieza para los dos.</para>
    /// </remarks>
    [Fact]
    public void Format_OtraMoneda_LlevaSuCodigo_YNoElSimboloDelPeso()
    {
        Assert.Equal("USD 99.000", Make().Format(99_000m, "USD"));
        Assert.Equal("EUR 1.250", Make().Format(1_250m, " eur "));
    }

    [Fact] // la moneda del producto, escrita como venga, sigue saliendo con su símbolo.
    public void Format_COP_EnMinusculas_SaleConSimbolo()
    {
        Assert.Equal("$ 99.000", Make().Format(99_000m, "cop"));
    }

    [Fact] // sin moneda, la del carrito; y si la del carrito es otra, también lleva su código.
    public void Format_SinMoneda_UsaLaDelCarrito_ConSuRegla()
    {
        Assert.Equal("USD 50", Make("USD").Format(50m));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Format_NullOrBlankCurrency_FallsBackToDefault(string? currency)
    {
        Assert.Equal("$ 50.000", Make("COP").Format(50_000m, currency));
    }

    [Fact]
    public void Format_BlankDefaultCurrency_FallsBackToCop()
    {
        var formatter = new EsCoPriceFormatter(new CartSettings { Currency = "" });
        Assert.Equal("$ 10.000", formatter.Format(10_000m));
    }
}
