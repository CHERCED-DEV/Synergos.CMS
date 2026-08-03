using Synergos.Core;

namespace Synergos.Api.Payments.Domain;

/// <summary>Lo que el cobro rechaza <b>solo</b>.</summary>
public static class PaymentRules
{
    public const string CodePrefix = "payments";

    /// <summary>Si el monto a autorizar sirve.</summary>
    public static Rejection? CheckAmount(Money amount)
    {
        if (amount.IsNegative)
        {
            return Rejection.Invalid($"{CodePrefix}.negative_amount", "No se autoriza un monto negativo.");
        }
        return amount.IsZero
            ? Rejection.Invalid($"{CodePrefix}.zero_amount",
                "Un cobro de cero no es un cobro. Si es gratis, no hay pago que registrar.")
            : null;
    }

    /// <summary>Si se puede capturar.</summary>
    public static Rejection? CheckCapturable(Payment payment) => payment.Status switch
    {
        PaymentStatus.Authorized => null,
        // Conflict y no Invalid: la petición está bien, el estado es el que no da. Y la
        // idempotencia se resuelve ANTES que esto, así que un reintento no llega hasta acá.
        PaymentStatus.Captured => Rejection.Conflict($"{CodePrefix}.already_captured", "Ese cobro ya se capturó."),
        PaymentStatus.Voided => Rejection.Conflict($"{CodePrefix}.voided", "Esa autorización se liberó; hay que autorizar de nuevo."),
        _ => Rejection.Conflict($"{CodePrefix}.payment_failed", "Ese cobro no llegó a autorizarse."),
    };

    /// <summary>Si se puede liberar la autorización.</summary>
    public static Rejection? CheckVoidable(Payment payment) => payment.Status switch
    {
        PaymentStatus.Authorized => null,
        PaymentStatus.Voided => null,   // idempotente: liberar lo liberado es el resultado que se quería
        PaymentStatus.Captured => Rejection.Conflict($"{CodePrefix}.already_captured",
            "No se libera un cobro ya capturado. Eso es una devolución."),
        _ => Rejection.Conflict($"{CodePrefix}.payment_failed", "Ese cobro no llegó a autorizarse."),
    };

    /// <summary>
    /// Si se puede devolver ese monto.
    /// </summary>
    /// <remarks>
    /// <b>La suma de devoluciones no puede pasar de lo capturado.</b> Es la regla que impide el
    /// modo de fallo más caro de esta capacidad: dos devoluciones parciales lanzadas a la vez
    /// que, sumadas, devuelven más de lo que entró. El <c>lock</c> del servicio hace que la
    /// comprobación y la escritura sean una sola operación; sin él, la regla sería correcta y
    /// aun así se colaría plata.
    /// </remarks>
    public static Rejection? CheckRefundable(Payment payment, Money amount)
    {
        if (payment.Status != PaymentStatus.Captured)
        {
            return Rejection.Conflict($"{CodePrefix}.not_captured",
                $"Solo se devuelve lo capturado, y este cobro está {payment.Status}.");
        }
        if (amount.IsNegative || amount.IsZero)
        {
            return Rejection.Invalid($"{CodePrefix}.bad_refund_amount", "La devolución tiene que ser mayor que cero.");
        }
        if (!string.Equals(amount.Currency, payment.Amount.Currency, StringComparison.Ordinal))
        {
            return Rejection.Invalid($"{CodePrefix}.refund_currency_mismatch",
                $"El cobro fue en {payment.Amount.Currency} y la devolución viene en {amount.Currency}.");
        }
        return amount > payment.Refundable
            ? Rejection.Conflict($"{CodePrefix}.refund_over_captured",
                $"Se pidió devolver {amount} y solo quedan {payment.Refundable} devolubles de {payment.Amount}.")
            : null;
    }
}
