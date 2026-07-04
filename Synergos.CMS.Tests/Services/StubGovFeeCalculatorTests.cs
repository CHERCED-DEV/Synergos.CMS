using Synergos.CMS.Application.Services.Impl;
using Xunit;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Cubre <see cref="StubGovFeeCalculator"/> (seam <c>IGovFeeCalculator</c>, tasa
/// pura/determinista del portal Gobierno): empty (trámite desconocido/vacío = exento) /
/// happy (tasa listada se cobra tal cual) / filter (tasa ≤ 0 = exento, moneda default) /
/// idempotent (determinista: misma entrada = misma salida). ADR 0075.
/// </summary>
public class StubGovFeeCalculatorTests
{
    private static StubGovFeeCalculator Make() => new();

    [Fact] // empty: trámite vacío ⇒ exento con monto 0 (paso de pago OFF)
    public void Calculate_EmptyTramite_IsExempt()
    {
        var quote = Make().Calculate(string.Empty, 100_000m, "COP");

        Assert.True(quote.Exempt);
        Assert.Equal(0m, quote.Amount);
        Assert.Equal("COP", quote.Currency);
    }

    [Fact] // happy: tasa listada > 0 se cobra tal cual (no exento)
    public void Calculate_ListedFee_IsCharged()
    {
        var quote = Make().Calculate("trm-pasaporte", 189_000m, "COP");

        Assert.False(quote.Exempt);
        Assert.Equal(189_000m, quote.Amount);
        Assert.Equal("COP", quote.Currency);
    }

    [Fact] // filter: tasa 0 o negativa ⇒ exento (trámite gratuito)
    public void Calculate_ZeroOrNegativeFee_IsExempt()
    {
        var svc = Make();

        Assert.True(svc.Calculate("trm-certificado-residencia", 0m, "COP").Exempt);
        Assert.True(svc.Calculate("trm-certificado-residencia", -5m, "COP").Exempt);
    }

    [Fact] // filter: moneda vacía cae a la default COP
    public void Calculate_EmptyCurrency_DefaultsToCop()
    {
        var quote = Make().Calculate("trm-pasaporte", 189_000m, " ");
        Assert.Equal("COP", quote.Currency);
    }

    [Fact] // idempotent: pura/determinista — misma entrada, misma salida
    public void Calculate_SameInput_SameOutput()
    {
        var svc = Make();
        var first = svc.Calculate("trm-pasaporte", 189_000m, "COP");
        var second = svc.Calculate("trm-pasaporte", 189_000m, "COP");

        Assert.Equal(first, second);
    }
}
