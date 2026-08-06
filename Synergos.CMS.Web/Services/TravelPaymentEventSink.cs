using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// El sink de Viajes: confirma el carrito cuando el PSP avisa que se pagó.
/// </summary>
/// <remarks>
/// <para>Segundo vertical que se entera de un cobro asíncrono (ADR 0116 fase 4-5).
/// Importa especialmente acá: un carrito de viaje se paga a menudo por PSE, y
/// con PSE el resultado NO vuelve en el retorno del banco — llega por evento,
/// minutos después. Sin este sink, un carrito pagado por PSE se quedaba
/// esperando para siempre.</para>
///
/// <para>`ConfirmAsync` del carrito ya es idempotente y, desde la fase 5,
/// reembolsa por su cuenta lo que no se pueda honrar. Este sink no repite esa
/// lógica: sólo lo dispara.</para>
/// </remarks>
public sealed class TravelPaymentEventSink : IPaymentEventSink
{
    private readonly ITravelCartService _travel;
    private readonly ILogger<TravelPaymentEventSink> _logger;

    public TravelPaymentEventSink(ITravelCartService travel, ILogger<TravelPaymentEventSink> logger)
    {
        _travel = travel;
        _logger = logger;
    }

    public string Vertical => "travel";

    public async Task<bool> OwnsAsync(string orderReference, CancellationToken cancellationToken = default)
        => await _travel.GetOrderAsync(orderReference, cancellationToken).ConfigureAwait(false) is not null;

    public async Task<PaymentEventOutcome> HandleAsync(
        PaymentEvent paymentEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paymentEvent);

        var order = await _travel
            .GetOrderAsync(paymentEvent.OrderReference, cancellationToken)
            .ConfigureAwait(false);

        if (order is null)
        {
            return PaymentEventOutcome.NotMine;
        }

        // La sesión del evento tiene que ser la de ESTE carrito: sin el chequeo,
        // el estado autorizado de una sesión ajena podría confirmar un viaje que
        // nadie pagó.
        if (!string.Equals(paymentEvent.SessionId, order.PaymentSessionId, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "Pago {EventId}: la sesión {SessionId} no corresponde al carrito {OrderRef}.",
                paymentEvent.EventId, paymentEvent.SessionId, paymentEvent.OrderReference);
            return PaymentEventOutcome.Rejected("la sesión no corresponde al carrito");
        }

        if (paymentEvent.VerifiedStatus is not (PaymentStatus.Authorized or PaymentStatus.Captured))
        {
            return PaymentEventOutcome.Ok($"estado {paymentEvent.VerifiedStatus}, nada que confirmar");
        }

        try
        {
            var result = await _travel
                .ConfirmAsync(paymentEvent.OrderReference, cancellationToken)
                .ConfigureAwait(false);
            return PaymentEventOutcome.Ok($"carrito {result.ConfirmationCode} en estado {result.Status}");
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // Rechazo permanente (carrito ya cancelado, por ejemplo): que el PSP
            // deje de reintentar. Un fallo transitorio NO cae acá y propaga.
            _logger.LogWarning(ex,
                "Pago {EventId}: el carrito {OrderRef} no se pudo confirmar (rechazo terminal).",
                paymentEvent.EventId, paymentEvent.OrderReference);
            return PaymentEventOutcome.Rejected(ex.Message);
        }
    }
}
