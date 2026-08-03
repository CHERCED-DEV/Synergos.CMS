using Synergos.Core;

namespace Synergos.Api.Pricing.Domain;

/// <summary>Lo que el precio rechaza <b>solo</b>, y cómo se compone una cotización.</summary>
public static class PricingRules
{
    public const string CodePrefix = "pricing";

    /// <summary>Puntos básicos de un 100 %.</summary>
    public const int FullBasisPoints = 10_000;

    /// <summary>Tope de líneas por cotización.</summary>
    public const int MaxLines = 200;

    /// <summary>Si el precio que se quiere publicar sirve.</summary>
    public static Rejection? CheckPrice(Money amount, int taxBasisPoints)
    {
        if (amount.IsNegative)
        {
            return Rejection.Invalid($"{CodePrefix}.negative_price", "Un precio no puede ser negativo.");
        }
        if (taxBasisPoints is < 0 or > FullBasisPoints)
        {
            return Rejection.Invalid($"{CodePrefix}.bad_tax_rate",
                $"El impuesto va entre 0 y {FullBasisPoints} puntos básicos.");
        }
        return null;
    }

    /// <summary>Si la promoción es usable ahora.</summary>
    public static Rejection? CheckPromotion(Promotion? promo, DateTimeOffset now)
    {
        if (promo is null)
        {
            return Rejection.NotFound($"{CodePrefix}.promotion_not_found", "Ese código de promoción no existe.");
        }
        if (!promo.Validity.Contains(now))
        {
            // Expired y no NotFound: al cliente le sirve saber que el código era real pero se
            // le pasó el plazo. Un NotFound lo manda a revisar si lo escribió mal.
            return Rejection.Expired($"{CodePrefix}.promotion_expired",
                $"La promoción valía entre {promo.Validity.Start:O} y {promo.Validity.End:O}.");
        }
        return null;
    }

    /// <summary>
    /// Calcula el descuento sobre una base.
    /// </summary>
    /// <remarks>
    /// <b>Nunca deja el total negativo.</b> Un descuento fijo de 50 000 sobre una compra de
    /// 30 000 se acota a 30 000: la alternativa es un cobro negativo, que aguas abajo se
    /// convierte en una devolución que nadie pidió.
    /// </remarks>
    public static Result<Money> Discount(Promotion promo, Money baseAmount)
    {
        if (promo.Kind == DiscountKind.Fixed &&
            !string.Equals(promo.Currency, baseAmount.Currency, StringComparison.Ordinal))
        {
            // No se convierte: hace falta una tasa, y adivinarla en silencio es peor que fallar.
            return Rejection.Invalid($"{CodePrefix}.promotion_currency_mismatch",
                $"La promoción descuenta {promo.Currency} y la compra va en {baseAmount.Currency}.");
        }

        var crudo = promo.Kind switch
        {
            DiscountKind.Percentage => Money.Of(baseAmount.Amount * promo.Value / FullBasisPoints, baseAmount.Currency),
            _ => Money.Of(promo.Value, baseAmount.Currency),
        };

        return Result.Ok(crudo > baseAmount ? baseAmount : crudo);
    }

    /// <summary>Impuesto sobre una base, en puntos básicos.</summary>
    public static Money Tax(Money baseAmount, int basisPoints)
        => Money.Of(baseAmount.Amount * basisPoints / FullBasisPoints, baseAmount.Currency);

    /// <summary>Si la cantidad pedida tiene sentido.</summary>
    public static Rejection? CheckQuantity(int quantity)
        => quantity > 0
            ? null
            : Rejection.Invalid($"{CodePrefix}.bad_quantity", "La cantidad tiene que ser mayor que cero.");
}
