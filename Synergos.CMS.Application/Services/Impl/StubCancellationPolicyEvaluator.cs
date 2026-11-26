using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Default <see cref="ICancellationPolicyEvaluator"/> — evaluador de política
/// de cancelación STUB para que el vertical Hoteles corra end-to-end en demo
/// sin un PMS real (mismo patrón stub-first que <c>StubPaymentProvider</c>).
/// Reglas simples y deterministas a partir de la convención de naming del
/// rate plan.
/// </summary>
/// <remarks>
/// Lógica pura y determinista en <c>Synergos.CMS.Application</c> — cero
/// dependencia de Umbraco/AspNetCore (ADR 0002). Reglas:
/// <list type="bullet">
///   <item>Non-refundable (rate plan termina en <c>-NREF</c>) → penalidad
///   total (no reembolso).</item>
///   <item>Refundable → 0 si se cancela con &gt;1 día de antelación al
///   check-in; si no, penalidad de 1 noche (se aproxima como una fracción
///   simple del total — el adapter real lee el desglose por noche).</item>
/// </list>
/// El adapter real lee las condiciones reales del rate plan (CMS/PMS) e
/// implementa la misma seam sin tocar el motor.
/// </remarks>
public sealed class StubCancellationPolicyEvaluator : ICancellationPolicyEvaluator
{
    // El stub no conoce el total ni el número de noches (la seam recibe solo
    // fechas + código): expresa la "penalidad de 1 noche" como un monto
    // simbólico determinista para que la demo muestre el comportamiento.
    // El adapter real recibe/consulta el desglose real del rate plan.
    private const decimal OneNightPenalty = 220_000m;

    private static readonly System.Globalization.CultureInfo EsCo = System.Globalization.CultureInfo.GetCultureInfo("es-CO");

    public CancellationOutcome Evaluate(string ratePlanCode, DateOnly checkIn, DateOnly asOf)
    {
        var refundable = !IsNonRefundable(ratePlanCode);

        if (!refundable)
        {
            return new CancellationOutcome(
                Refundable: false,
                PenaltyAmount: OneNightPenalty, // simbólico: el real es el prepago total
                Description: "Tarifa no reembolsable: la cancelación no genera devolución.");
        }

        // Refundable: gratis si se cancela con más de 1 día de antelación, es decir, hasta dos días antes del check-in.
        // La descripción dice esa fecha: «a tiempo» no le decía a nadie hasta cuándo (D-016-C23).
        var freeUntil = checkIn.AddDays(-1);
        if (asOf < freeUntil)
        {
            var ultimoDiaGratis = checkIn.AddDays(-2).ToString("d 'de' MMMM", EsCo);
            return new CancellationOutcome(
                Refundable: true,
                PenaltyAmount: 0m,
                Description: $"Cancelación gratuita hasta el {ultimoDiaGratis}; después se cobra una noche.");
        }

        return new CancellationOutcome(
            Refundable: true,
            PenaltyAmount: OneNightPenalty,
            Description: "Cancelación tardía: se cobra una penalidad equivalente a 1 noche.");
    }

    private static bool IsNonRefundable(string ratePlanCode)
        => !string.IsNullOrEmpty(ratePlanCode)
           && ratePlanCode.EndsWith("-NREF", StringComparison.OrdinalIgnoreCase);
}
