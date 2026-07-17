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
        Assert.Equal("1.500.000 COP", Make().Format(1_500_000m));
    }

    [Fact]
    public void Format_Zero_RendersZeroWithCurrency()
    {
        Assert.Equal("0 COP", Make().Format(0m));
    }

    [Fact]
    public void Format_RoundsToInteger_NoDecimals()
    {
        Assert.Equal("89.000 COP", Make().Format(89_000.49m));
    }

    [Fact]
    public void Format_CurrencyOverride_UsesProvided()
    {
        Assert.Equal("99.000 USD", Make().Format(99_000m, "USD"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Format_NullOrBlankCurrency_FallsBackToDefault(string? currency)
    {
        Assert.Equal("50.000 COP", Make("COP").Format(50_000m, currency));
    }

    [Fact]
    public void Format_BlankDefaultCurrency_FallsBackToCop()
    {
        var formatter = new EsCoPriceFormatter(new CartSettings { Currency = "" });
        Assert.Equal("10.000 COP", formatter.Format(10_000m));
    }
}
