using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// El sink de Tienda: confirma la orden cuando el PSP avisa que se pagó.
/// </summary>
/// <remarks>
/// Es la extracción de lo que antes estaba cableado dentro de
/// <c>PaymentWebhookController</c> (ADR 0116 fase 4). El comportamiento no
/// cambia; lo que cambia es que ahora es UNO de N y no EL destino.
/// </remarks>
public sealed class ShopPaymentEventSink : IPaymentEventSink
{
    private readonly IShopOrderService _orders;
    private readonly ILogger<ShopPaymentEventSink> _logger;

    public ShopPaymentEventSink(IShopOrderService orders, ILogger<ShopPaymentEventSink> logger)
    {
        _orders = orders;
        _logger = logger;
    }

    public string Vertical => "shop";

    public async Task<bool> OwnsAsync(string orderReference, CancellationToken cancellationToken = default)
        => await _orders.GetOrderAsync(orderReference, cancellationToken).ConfigureAwait(false) is not null;

    public async Task<PaymentEventOutcome> HandleAsync(
        PaymentEvent paymentEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paymentEvent);

        var order = await _orders
            .GetOrderAsync(paymentEvent.OrderReference, cancellationToken)
            .ConfigureAwait(false);

        // Se relee en vez de confiar en OwnsAsync: entre una llamada y otra la
        // orden pudo desaparecer, y de todos modos hace falta el registro
        // entero para comparar la sesión.
        if (order is null)
        {
            return PaymentEventOutcome.NotMine;
        }

        // La sesión del evento tiene que ser la de ESTA orden. Sin este chequeo,
        // el estado autorizado de una sesión ajena podría confirmar una orden
        // que nadie pagó.
        if (!string.Equals(paymentEvent.SessionId, order.PaymentSessionId, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "Pago {EventId}: la sesión {SessionId} no corresponde a la orden {OrderRef}.",
                paymentEvent.EventId, paymentEvent.SessionId, paymentEvent.OrderReference);
            return PaymentEventOutcome.Rejected("la sesión no corresponde a la orden");
        }

        if (paymentEvent.VerifiedStatus is not (PaymentStatus.Authorized or PaymentStatus.Captured))
        {
            // Ni confirmamos ni cerramos: un pago que hoy está pendiente puede
            // aprobarse después, y ese será otro evento.
            return PaymentEventOutcome.Ok($"estado {paymentEvent.VerifiedStatus}, nada que confirmar");
        }

        try
        {
            var result = await _orders
                .ConfirmAsync(paymentEvent.OrderReference, cancellationToken)
                .ConfigureAwait(false);
            return PaymentEventOutcome.Ok($"orden {result.OrderNumber} en estado {result.Status}");
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // Rechazo de negocio PERMANENTE (un hold vencido, por ejemplo): que
            // el PSP deje de reintentar. Una IOException transitoria NO cae acá
            // — propaga, no se marca, y el reintento vuelve a ejecutar
            // ConfirmAsync, que es idempotente.
            _logger.LogWarning(ex,
                "Pago {EventId}: la orden {OrderRef} no se pudo confirmar (rechazo terminal).",
                paymentEvent.EventId, paymentEvent.OrderReference);
            return PaymentEventOutcome.Rejected(ex.Message);
        }
    }
}
