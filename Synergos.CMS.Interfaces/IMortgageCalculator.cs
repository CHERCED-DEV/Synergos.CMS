namespace Synergos.CMS.Interfaces;

/// <summary>
/// Una fila del cuadro de amortización (francés): por cuota, qué parte va a
/// interés vs a capital y cuánto saldo queda. La tabla completa la pinta la
/// calculadora de la PDP (doc propiedades-app-spec §2).
/// </summary>
public sealed record MortgageScheduleRow(
    int Period,
    decimal Payment,
    decimal Interest,
    decimal Principal,
    decimal Balance);

/// <summary>
/// Resultado de la calculadora de hipoteca: la cuota mensual fija, el total de
/// intereses y el total pagado sobre el plazo, más el cuadro de amortización
/// completo (sistema francés). Determinista — mismas entradas, mismo resultado.
/// </summary>
public sealed record MortgageResult(
    decimal Monthly,
    decimal TotalInterest,
    decimal TotalPaid,
    IReadOnlyList<MortgageScheduleRow> Schedule);

/// <summary>
/// Calculadora de hipoteca del vertical Propiedades — afina el presupuesto
/// buscable del comprador (cuota mensual estimada + desglose capital/interés).
/// Es <strong>PURA y DETERMINISTA</strong>: amortización francesa (cuota fija)
/// sobre <c>precio − cuota inicial</c>, a la tasa nominal anual / 12, por el
/// plazo en meses. Sin estado, sin IO, sin <see cref="System.Threading.Tasks.Task"/>.
/// </summary>
/// <remarks>
/// El default <c>StubMortgageCalculator</c> (Application, lógica pura) implementa
/// la fórmula estándar; un seam server solo se justificaría para tasas/productos
/// bancarios reales (CO: UVR vs pesos, E.A.) — decisión abierta en el spec §8. El
/// cálculo base es el mismo en cliente y servidor. ADR 0002 (Application sin
/// Umbraco) + ADR 0075 (tests canónicos: degenerado tasa-cero / happy / desglose /
/// determinismo).
/// </remarks>
public interface IMortgageCalculator
{
    /// <summary>
    /// Calcula la cuota mensual + totales + cuadro de amortización francés.
    /// </summary>
    /// <param name="price">Precio del inmueble (mayor a cero).</param>
    /// <param name="downPayment">Cuota inicial (≥ 0 y &lt; precio).</param>
    /// <param name="termMonths">Plazo en meses (mayor a cero).</param>
    /// <param name="annualRate">Tasa nominal anual en fracción (0.12 = 12%). Cero
    ///   = sin interés (cuota = capital / plazo).</param>
    MortgageResult Calculate(decimal price, decimal downPayment, int termMonths, decimal annualRate);
}
