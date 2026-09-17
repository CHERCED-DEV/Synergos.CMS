using Synergos.Core;

namespace Synergos.Api.Payments.Domain;

/// <summary>Lo que el cobro rechaza <b>solo</b>.</summary>
public static class PaymentRules
{
    public const string CodePrefix = "payments";

    /// <summary>
    /// Traduce lo que dijo el medio de pago al rechazo que sale por HTTP.
    /// </summary>
    /// <remarks>
    /// <para><b>Es el único sitio donde se decide qué se reintenta</b> (HU #27), y antes no
    /// existía: con un <c>bool</c>, «el banco dijo que no» y «la pasarela no contestó» salían los
    /// dos como <c>Unavailable</c> —transitorio, reintentable—. O sea que un rechazo firme se
    /// reintentaba ocho veces contra una tarjeta que ya había dicho que no.</para>
    ///
    /// <list type="bullet">
    ///   <item><b>Rechazado → <c>Conflict</c></b>, que NO es transitorio: reintentar no cambia la
    ///   respuesta. Y va con el motivo del proveedor, porque «fondos insuficientes» lleva a una
    ///   acción y «el pago falló» no.</item>
    ///   <item><b>No disponible → <c>Unavailable</c></b>, que sí lo es: no se sabe qué pasó, y no
    ///   saberlo es razón para volver a preguntar.</item>
    ///   <item><b>Sin configurar → <c>Unavailable</c> también</b>, y esto es a propósito: la
    ///   operación NO ocurrió y quien llama debe poder reintentar cuando el operador ponga la
    ///   credencial. El grito va al log, que es donde lo va a ver quien puede arreglarlo.</item>
    /// </list>
    /// </remarks>
    public static Rejection? FromAttempt(PaymentAttempt intento, string operacion) => intento.Outcome switch
    {
        PaymentOutcome.Ok => null,

        PaymentOutcome.Declined => Rejection.Conflict(
            $"{CodePrefix}.payment_declined",
            string.IsNullOrWhiteSpace(intento.Reason)
                ? $"El medio de pago rechazó {operacion}."
                : intento.Reason!),

        PaymentOutcome.Unavailable => Rejection.Unavailable(
            $"{CodePrefix}.payment_provider_unavailable",
            string.IsNullOrWhiteSpace(intento.Reason)
                ? $"El medio de pago no respondió al intentar {operacion}."
                : intento.Reason!),

        _ => Rejection.Unavailable(
            $"{CodePrefix}.transport_not_configured",
            string.IsNullOrWhiteSpace(intento.Reason)
                ? "El medio de pago no está configurado."
                : intento.Reason!),
    };

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

    /// <summary>
    /// Si lo que el proveedor cuenta de un cobro se puede anotar.
    /// </summary>
    /// <remarks>
    /// <para><b>La idempotencia va ANTES que el estado, y acá no hay llave que resolver: la
    /// resuelve el propio estado.</b> Un webhook se reentrega hasta ver un 2xx, así que el mismo
    /// <c>APPROVED</c> llega varias veces; y llega además <b>fuera de orden</b>, porque nada
    /// garantiza que el <c>PENDING</c> se entregue antes que el desenlace. Por eso solo se avanza
    /// desde <see cref="PaymentStatus.Authorized"/>: desde cualquier otro sitio el evento se acusa
    /// y no se aplica. Al revés —aplicar siempre lo último que llegue— un reenvío tardío
    /// retrocedería un cobro ya capturado, y eso no falla: se guarda.</para>
    ///
    /// <para><c>null</c> significa «anótalo»; un rechazo, «no lo anotes y di por qué».</para>
    /// </remarks>
    public static Rejection? CheckProviderEvent(Payment payment, PaymentStatus destino, long centavosDelProveedor)
    {
        if (payment.Status != PaymentStatus.Authorized) return null;   // ya resuelto: se acusa y no se toca

        if (destino != PaymentStatus.Captured) return null;

        var esperados = (long)Math.Round(payment.Amount.Amount * 100m, 0, MidpointRounding.AwayFromZero);
        if (centavosDelProveedor == esperados) return null;

        // La firma de integridad cubre el monto TAL COMO SE ENVIÓ, así que un error de centavos
        // produce una transacción impecable por la cifra equivocada. Dejarla pasar acá sería
        // despachar un pedido cobrando cien veces menos, y se descubriría cuadrando la caja.
        return Rejection.Conflict($"{CodePrefix}.provider_amount_mismatch",
            $"El proveedor dice {centavosDelProveedor} centavos y el cobro era de {esperados}.");
    }
}
