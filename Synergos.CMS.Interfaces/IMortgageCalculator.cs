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
/// bancarios reales (CO: UVR vs pesos, E.A.) — decisión abierta en el spec §8.
/// ADR 0002 (Application sin Umbraco) + ADR 0075 (tests canónicos: degenerado
/// tasa-cero / happy / desglose / determinismo).
/// <para><b>«El cálculo base es el mismo en cliente y servidor» estaba escrito acá y era
/// FALSO durante toda la vida del endpoint</b> (#167). Las dos implementaciones eran la misma
/// fórmula con la tasa a 100× de distancia —acá fracción, en <c>mortgage.calc.ts</c>
/// porcentaje— y nada las cruzaba, así que el borde contestaba 240.000.000 al mes donde la
/// app pinta 2.642.606,72. Hoy la frase la comprueban los vectores de oro de
/// <c>docs/contracts/mortgage-vectors.json</c>, que ejecutan las DOS: acá
/// <c>HipotecaVectoresTests</c> por el borde (con su conversión) y en el otro árbol el spec de
/// <c>mortgage.calc</c>. Una afirmación sobre dos árboles que sólo vive en un
/// <c>&lt;remarks&gt;</c> no es un contrato: es una oración.</para>
/// <para><b>La unidad de esta firma es la FRACCIÓN y no se movió</b>: la conversión desde el
/// porcentaje que viaja por el cable (<c>annualRatePercent</c>) vive en el borde y en un solo
/// sitio. Lo que estaba mal no era esta unidad, era que el cable no nombrara la suya.</para>
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
