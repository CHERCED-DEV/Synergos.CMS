using System;
using System.Linq;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Xunit;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Cubre <see cref="StubMortgageCalculator"/> (seam <see cref="IMortgageCalculator"/>,
/// calculadora de hipoteca del portal inmobiliario): los 4 casos canónicos (ADR 0075),
/// adaptados a un cálculo puro — degenerado (entrada inválida) / happy (cuota francesa
/// conocida) / filter (caso tasa-cero) / idempotent (determinismo) — más el cierre del
/// cuadro de amortización en saldo cero.
/// </summary>
public class StubMortgageCalculatorTests
{
    private static IMortgageCalculator Make() => new StubMortgageCalculator();

    [Fact] // degenerado: precio no positivo lanza
    public void Calculate_NonPositivePrice_Throws()
    {
        Assert.Throws<ArgumentException>(() => Make().Calculate(0m, 0m, 120, 0.12m));
    }

    [Fact] // degenerado: cuota inicial ≥ precio lanza
    public void Calculate_DownPaymentTooHigh_Throws()
    {
        Assert.Throws<ArgumentException>(() => Make().Calculate(100_000_000m, 100_000_000m, 120, 0.12m));
    }

    [Fact] // happy: cuota francesa conocida (100M @ 12% nominal anual, 12 meses)
    public void Calculate_FrenchAmortization_KnownPayment()
    {
        // i = 0.01 mensual, n = 12 → cuota = 8.884.878,87 sobre 100.000.000.
        var result = Make().Calculate(100_000_000m, 0m, 12, 0.12m);

        Assert.Equal(8_884_878.87m, result.Monthly);
        Assert.Equal(12, result.Schedule.Count);
        Assert.True(result.TotalPaid > 100_000_000m); // paga capital + interés
        Assert.True(result.TotalInterest > 0m);
    }

    [Fact] // happy/cierre: el cuadro de amortización cierra en saldo cero
    public void Calculate_Schedule_ClosesAtZeroBalance()
    {
        var result = Make().Calculate(250_000_000m, 50_000_000m, 60, 0.10m);
        Assert.Equal(0m, result.Schedule.Last().Balance);
    }

    [Fact] // filter (degenerado de tasa): tasa cero → cuota = capital / plazo, sin interés
    public void Calculate_ZeroRate_NoInterest()
    {
        var result = Make().Calculate(120_000_000m, 0m, 120, 0m);

        Assert.Equal(1_000_000m, result.Monthly); // 120M / 120
        Assert.Equal(0m, result.TotalInterest);
        Assert.Equal(120_000_000m, result.TotalPaid);
    }

    [Fact] // idempotent: mismas entradas → mismo resultado (puro/determinista)
    public void Calculate_IsDeterministic()
    {
        var a = Make().Calculate(300_000_000m, 60_000_000m, 180, 0.11m);
        var b = Make().Calculate(300_000_000m, 60_000_000m, 180, 0.11m);

        Assert.Equal(a.Monthly, b.Monthly);
        Assert.Equal(a.TotalInterest, b.TotalInterest);
        Assert.Equal(a.TotalPaid, b.TotalPaid);
    }
}
