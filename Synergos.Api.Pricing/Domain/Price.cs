using Synergos.Core;

namespace Synergos.Api.Pricing.Domain;

/// <summary>El precio publicado de algo.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Subject">De qué es el precio — opaco.</param>
/// <param name="Amount">Cuánto, con su moneda.</param>
/// <param name="TaxRateBasisPoints">Impuesto en puntos básicos: 1900 = 19 %.</param>
/// <param name="UpdatedAtUtc">Cuándo se fijó.</param>
/// <remarks>
/// <b>El impuesto en puntos básicos y no en decimal</b> porque un <c>0.19</c> guardado como
/// coma flotante y multiplicado por miles de líneas acumula centavos que no cuadran con lo
/// declarado. Un entero no se desvía.
/// </remarks>
public sealed record Price(
    string Id, Ref Subject, Money Amount, int TaxRateBasisPoints, DateTimeOffset UpdatedAtUtc);

/// <summary>Cómo descuenta una promoción.</summary>
public enum DiscountKind
{
    /// <summary>Un porcentaje en puntos básicos.</summary>
    Percentage,

    /// <summary>Un monto fijo, en la moneda de la promoción.</summary>
    Fixed,
}

/// <summary>Una promoción con código y vigencia.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Code">Lo que teclea quien compra. Se compara sin distinguir mayúsculas.</param>
/// <param name="Kind">Porcentaje o monto fijo.</param>
/// <param name="Value">Puntos básicos si es porcentaje; unidades de moneda si es fijo.</param>
/// <param name="Currency">Moneda del descuento fijo. Irrelevante para porcentaje.</param>
/// <param name="Validity">Desde cuándo y hasta cuándo vale.</param>
public sealed record Promotion(
    string Id, string Code, DiscountKind Kind, decimal Value, string Currency, TimeWindow Validity);

/// <summary>Una línea cotizada.</summary>
public sealed record QuoteLine(Ref Subject, int Quantity, Money UnitPrice, Money Subtotal, Money Tax);

/// <summary>El resultado de cotizar.</summary>
/// <param name="Lines">Las líneas, en el orden en que llegaron.</param>
/// <param name="Subtotal">Suma de subtotales, antes de impuesto y descuento.</param>
/// <param name="Discount">Lo que quitó la promoción. Cero si no hubo.</param>
/// <param name="Tax">Impuesto sobre la base ya descontada.</param>
/// <param name="Total">Lo que se cobra.</param>
public sealed record Quote(
    IReadOnlyList<QuoteLine> Lines, Money Subtotal, Money Discount, Money Tax, Money Total);
