using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Default <see cref="IMortgageCalculator"/> — calculadora de hipoteca del portal
/// inmobiliario (doc propiedades-app-spec §2). Amortización francesa (cuota fija)
/// sobre el monto financiado (<c>precio − cuota inicial</c>), a la tasa nominal
/// anual / 12, por el plazo en meses.
/// </summary>
/// <remarks>
/// PURA y DETERMINISTA — sin estado, sin IO, sin Umbraco/AspNetCore (ADR 0002).
/// Fórmula estándar de cuota: <c>P·i / (1 − (1 + i)^−n)</c> donde <c>i</c> es la
/// tasa mensual y <c>n</c> el plazo; con tasa cero degenera a <c>P / n</c>. Los
/// importes se redondean a centavos (banker's rounding) y la última cuota absorbe
/// el redondeo para que el saldo cierre en exactamente cero. Un seam server solo
/// se justificaría para tasas/productos bancarios reales (spec §8). ADR 0075.
/// </remarks>
public sealed class StubMortgageCalculator : IMortgageCalculator
{
    public MortgageResult Calculate(decimal price, decimal downPayment, int termMonths, decimal annualRate)
    {
        if (price <= 0m)
        {
            throw new ArgumentException("El precio debe ser mayor a cero.", nameof(price));
        }
        if (downPayment < 0m || downPayment >= price)
        {
            throw new ArgumentException("La cuota inicial debe ser ≥ 0 y menor al precio.", nameof(downPayment));
        }
        if (termMonths <= 0)
        {
            throw new ArgumentException("El plazo en meses debe ser mayor a cero.", nameof(termMonths));
        }
        if (annualRate < 0m)
        {
            throw new ArgumentException("La tasa anual no puede ser negativa.", nameof(annualRate));
        }

        var principal = price - downPayment;
        var monthlyRate = annualRate / 12m;

        decimal monthly;
        if (monthlyRate == 0m)
        {
            // Sin interés: cuota = capital / plazo.
            monthly = Round(principal / termMonths);
        }
        else
        {
            // Cuota fija (sistema francés). Se usa double para la potencia y se
            // vuelve a decimal redondeando — suficiente para una estimación.
            var i = (double)monthlyRate;
            var factor = i / (1d - Math.Pow(1d + i, -termMonths));
            monthly = Round((decimal)((double)principal * factor));
        }

        // Construir el cuadro de amortización: cada cuota = interés del saldo +
        // capital; la última absorbe el redondeo para cerrar el saldo en cero.
        var schedule = new List<MortgageScheduleRow>(termMonths);
        var balance = principal;
        decimal totalInterest = 0m;
        decimal totalPaid = 0m;

        for (var period = 1; period <= termMonths; period++)
        {
            var interest = Round(balance * monthlyRate);
            var payment = monthly;
            var isLast = period == termMonths;

            decimal principalPaid;
            if (isLast)
            {
                // Última cuota: paga exactamente el saldo + su interés (cierra en 0).
                principalPaid = balance;
                payment = Round(principalPaid + interest);
            }
            else
            {
                principalPaid = payment - interest;
            }

            balance = Round(balance - principalPaid);
            if (balance < 0m)
            {
                balance = 0m;
            }

            totalInterest += interest;
            totalPaid += payment;
            schedule.Add(new MortgageScheduleRow(period, payment, interest, principalPaid, balance));
        }

        return new MortgageResult(
            Monthly: monthly,
            TotalInterest: Round(totalInterest),
            TotalPaid: Round(totalPaid),
            Schedule: schedule);
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.ToEven);
}
