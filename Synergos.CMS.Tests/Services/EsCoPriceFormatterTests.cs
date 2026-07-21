using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Xunit;

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

    [Fact]
    public void Format_MonedaExplicita_SeIgnora_DemoMonoMoneda()
    {
        // El parámetro de moneda se IGNORA hoy: `EsCoPriceFormatter` lo descarta con
        // `_ = ...` porque la demo es mono-moneda COP y el símbolo de la cultura es
        // '$'. Este test se llamaba "UsesProvided" y afirmaba "99.000 USD" — mentía
        // en las dos mitades. Ahora fija lo que el código HACE, que es lo único que
        // una prueba puede defender: si algún día se soporta multi-moneda, este test
        // se pone rojo y obliga a decidirlo a conciencia.
        Assert.Equal("$ 99.000", Make().Format(99_000m, "USD"));
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
