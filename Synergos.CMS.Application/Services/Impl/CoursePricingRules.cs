using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Cómo se paga un curso: de contado, o en cuotas con recargo.
/// </summary>
/// <remarks>
/// <b>Vivía dentro de <see cref="StubCourseCatalogProvider"/> y subió al SEGUNDO consumidor</b>
/// (CLAUDE.md §0.B.17), que es el catálogo servido desde el contenido del CMS. Copiarla habría
/// dejado el mismo curso ofreciendo cuotas distintas según
/// <c>Synergos:Catalog:Sources:Academy</c> — y la que nadie mira es la que se desvía.
///
/// <para>Es una regla de NEGOCIO, no de presentación: el recargo por financiar es lo que la
/// escuela cobra por esperar su plata. Por eso está aquí y no en el controller.</para>
/// </remarks>
public static class CoursePricingRules
{
    /// <summary>
    /// Recargo por pagar en cuotas. Un 8 % sobre el total, no un interés compuesto: es lo que
    /// se le anuncia al alumno y lo que el motor cobra.
    /// </summary>
    private const decimal InstallmentSurcharge = 1.08m;

    /// <summary>Cuántas cuotas ofrece el plan financiado.</summary>
    private const int Installments = 3;

    /// <summary>
    /// Los planes de un curso: sólo «inscripción gratuita» si no cuesta, o contado + cuotas.
    /// </summary>
    /// <remarks>
    /// El recargo se redondea a entero porque el patrón de COP no lleva decimales; con
    /// <see cref="MidpointRounding.AwayFromZero"/> y no al par, que es lo que un alumno espera
    /// al comprobar la cuenta a mano.
    /// </remarks>
    public static IReadOnlyList<CoursePricingPlan> Build(decimal price, string currency)
    {
        if (price <= 0m)
        {
            return new[] { new CoursePricingPlan("free", "Inscripción gratuita", 0m, currency, 1) };
        }

        var installmentTotal = decimal.Round(price * InstallmentSurcharge, 0, MidpointRounding.AwayFromZero);
        return new[]
        {
            new CoursePricingPlan("full", "Pago de contado", price, currency, 1),
            new CoursePricingPlan("emi-3", "3 cuotas", installmentTotal, currency, Installments),
        };
    }
}
