using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// El motor de carrito que corre EN PROCESO: aparta contra <see cref="IReservationService"/> y
/// cobra contra <see cref="IPaymentProvider"/>.
/// </summary>
/// <remarks>
/// <para><b>Es exactamente lo que hacía <see cref="TravelCartService"/></b>, movido sin cambiarle
/// una decisión — el expediente de la compra se quedó allá y acá sólo vive el motor. La prueba de
/// que no cambió nada son los tests de aquel servicio, que siguen en verde sin tocarse.</para>
///
/// <para><b>Su política ante un ítem caído: conservar lo que sí salió y devolver lo caído.</b>
/// Está argumentada desde antes y sigue valiendo — quien compró un vuelo, un hotel y un auto no
/// pierde el vuelo porque el auto se agotó, y cancelar todo sería peor servicio y más plata
/// moviéndose sin necesidad. La política del otro motor es suya y está escrita en el suyo.</para>
/// </remarks>
public sealed class InProcessTravelCartEngine : ITravelCartEngine
{
    private readonly IReservationService _reservations;
    private readonly IPaymentProvider _payments;

    public InProcessTravelCartEngine(IReservationService reservations, IPaymentProvider payments)
    {
        _reservations = reservations ?? throw new ArgumentNullException(nameof(reservations));
        _payments = payments ?? throw new ArgumentNullException(nameof(payments));
    }

    public async Task<TravelCartHold> HoldAllAsync(
        IReadOnlyList<TravelCartItem> items,
        TravelGuest guest,
        string orderRef,
        CancellationToken cancellationToken = default)
    {
        var currency = items[0].Currency;
        var held = new List<TravelCartHeldItem>(items.Count);
        var paymentLines = new List<PaymentLineItem>(items.Count);
        var total = 0m;

        foreach (var item in items)
        {
            var reservation = await _reservations.HoldItemAsync(
                new TravelItemReservationRequest(
                    ProductType: item.Product,
                    ProductRef: item.OfferId,
                    ProductLabel: item.Label,
                    GuestName: guest.Name.Trim(),
                    GuestEmail: guest.Email.Trim(),
                    TotalPrice: item.Price,
                    Currency: currency),
                cancellationToken);

            held.Add(new TravelCartHeldItem(item.OfferId, reservation.Id));
            paymentLines.Add(new PaymentLineItem(
                Sku: $"{item.Product}:{item.OfferId}",
                Description: item.Label,
                UnitPrice: item.Price,
                Quantity: 1));
            total += item.Price;
        }

        var session = await _payments.CreateSessionAsync(
            new PaymentSessionRequest(
                OrderReference: orderRef,
                Amount: total,
                Currency: currency,
                Items: paymentLines,
                CustomerEmail: guest.Email.Trim(),
                ReturnUrl: null,
                Metadata: null),
            cancellationToken);

        // Sin referencia propia: a este motor le basta la del CMS para reconocer el carrito.
        return new TravelCartHold(null, session.SessionId, total, currency, held);
    }

    public async Task<TravelCartSettlement> SettleAsync(
        string? engineRef,
        string paymentSessionId,
        IReadOnlyList<TravelCartSettledLine> lines,
        CancellationToken cancellationToken = default)
    {
        // 1) Capturar el pago del carrito completo (idempotente en el PSP).
        var capture = await _payments.CaptureAsync(paymentSessionId, cancellationToken: cancellationToken);
        if (capture.Status != PaymentStatus.Captured)
        {
            throw new InvalidOperationException(
                capture.FailureReason ?? $"No se pudo capturar el pago del carrito (estado {capture.Status}).");
        }

        // 2) Confirmar TODAS las reservas. ConfirmAsync es idempotente por reserva.
        var resultado = new List<TravelCartSettledItem>(lines.Count);
        var unfulfilled = 0m;

        foreach (var line in lines)
        {
            string estado;
            var reservationId = line.ReservationId;
            try
            {
                var confirmed = await _reservations.ConfirmAsync(
                    line.ReservationId, paymentSessionId, cancellationToken);
                estado = confirmed.Status.ToString();
                reservationId = confirmed.Id;
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                // Un ítem que no se pudo confirmar NO tumba los que sí: el cliente se queda con
                // el vuelo aunque el hotel se haya caído, y se le devuelve lo del hotel.
                estado = ReservationStatus.Cancelled.ToString();
            }

            if (!string.Equals(estado, ReservationStatus.Confirmed.ToString(), StringComparison.Ordinal))
            {
                unfulfilled += line.Price;
            }

            resultado.Add(new TravelCartSettledItem(line.OfferId, reservationId, estado));
        }

        // 3) Devolver lo que no se pudo entregar. Reembolso PARCIAL por el monto de las líneas
        //    caídas. Best-effort: lo que SÍ salió ya está confirmado, y un reembolso caído no
        //    puede deshacer un viaje confirmado. Pero se intenta siempre, porque no intentarlo es
        //    el defecto que esto cierra.
        if (unfulfilled > 0m && !string.IsNullOrWhiteSpace(paymentSessionId))
        {
            await BestEffort.RunAsync(
                () => _payments.RefundAsync(paymentSessionId, unfulfilled, cancellationToken),
                cancellationToken);
        }

        return new TravelCartSettlement(resultado, unfulfilled);
    }

    public async Task<TravelCartRelease> ReleaseAsync(
        string? engineRef,
        string paymentSessionId,
        IReadOnlyList<TravelCartSettledLine> lines,
        string reason,
        CancellationToken cancellationToken = default)
    {
        foreach (var line in lines)
        {
            await _reservations.CancelAsync(line.ReservationId, reason, cancellationToken);
        }

        // Reembolso SOLO si el pago estaba capturado: el PSP devuelve Refunded únicamente sobre
        // una sesión Captured (pre-confirm responde con otro outcome y no hay nada que devolver).
        var outcome = await _payments.RefundAsync(paymentSessionId, null, cancellationToken);

        return outcome.Status == PaymentStatus.Refunded
            ? new TravelCartRelease(true, outcome.AmountCaptured)
            : new TravelCartRelease(false, 0m);
    }
}
