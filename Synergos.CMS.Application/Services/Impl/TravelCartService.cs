using System.Collections.Concurrent;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Default <see cref="ITravelCartService"/> — carrito de viaje multi-producto
/// server-side liviano. Es el MOTOR transaccional del dominio Booking: compone
/// los seams existentes (<see cref="IReservationService"/> +
/// <see cref="IPaymentProvider"/>) para llevar N ítems heterogéneos
/// (hotel|vuelo|auto) por el flujo unificado seleccionar → pagar (una sola vez)
/// → confirmar.
/// </summary>
/// <remarks>
/// Lógica pura en <c>Synergos.CMS.Application</c> — cero dependencia de
/// Umbraco/AspNetCore (ADR 0002). NO toca el flujo hotel del BookingController
/// (aditivo): usa la vía polimórfica <see cref="IReservationService.HoldItemAsync"/>.
/// Estado del carrito (orderRef → reservas + sesión de pago) en memoria
/// (proceso), suficiente para demo; un adapter real delega a DB/BookingSession.
/// <see cref="ConfirmAsync"/> es idempotente: re-confirmar el mismo orderRef
/// devuelve el mismo resultado sin doble captura ni doble efecto (la captura del
/// PSP y el Confirm de cada reserva ya lo son). ADR 0075 (seam con tests).
/// </remarks>
public sealed class TravelCartService : ITravelCartService
{
    private readonly IReservationService _reservations;
    private readonly IPaymentProvider _payments;
    private readonly ConcurrentDictionary<string, CartOrder> _orders = new(StringComparer.Ordinal);

    public TravelCartService(IReservationService reservations, IPaymentProvider payments)
    {
        _reservations = reservations ?? throw new ArgumentNullException(nameof(reservations));
        _payments = payments ?? throw new ArgumentNullException(nameof(payments));
    }

    public async Task<TravelCheckoutResult> CheckoutAsync(
        IReadOnlyList<TravelCartItem> items,
        TravelGuest guest,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(guest);
        if (items is null || items.Count == 0)
        {
            throw new ArgumentException("El carrito de viaje requiere al menos un ítem.", nameof(items));
        }
        if (string.IsNullOrWhiteSpace(guest.Name) || string.IsNullOrWhiteSpace(guest.Email))
        {
            throw new ArgumentException("El nombre y el email del viajero son obligatorios.", nameof(guest));
        }

        // Una sola moneda por carrito (el PSP cobra un total). Si llegan monedas
        // mezcladas es un error de armado del carrito: el split por moneda sería
        // sesiones de pago separadas, fuera del alcance del caso "un total".
        var currency = items[0].Currency;
        if (string.IsNullOrWhiteSpace(currency))
        {
            throw new ArgumentException("Cada ítem del carrito debe declarar su moneda.", nameof(items));
        }
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.OfferId) || string.IsNullOrWhiteSpace(item.Label))
            {
                throw new ArgumentException("Cada ítem requiere offerId y etiqueta.", nameof(items));
            }
            if (item.Price <= 0m)
            {
                throw new ArgumentException("El precio de cada ítem debe ser mayor a cero.", nameof(items));
            }
            if (!string.Equals(item.Currency, currency, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Todos los ítems del carrito deben usar la misma moneda.", nameof(items));
            }
        }

        // 1) Reservar CADA ítem (un hold por ítem) vía la vía polimórfica.
        var lines = new List<CartLine>(items.Count);
        var paymentLines = new List<PaymentLineItem>(items.Count);
        decimal total = 0m;
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

            lines.Add(new CartLine(item.Product, item.OfferId, item.Label, reservation.Id, item.Price, currency));
            paymentLines.Add(new PaymentLineItem(
                Sku: $"{item.Product}:{item.OfferId}",
                Description: item.Label,
                UnitPrice: item.Price,
                Quantity: 1));
            total += item.Price;
        }

        // 2) UNA sola sesión de pago por el total agregado del carrito.
        var orderRef = $"trip_{Guid.NewGuid():N}";
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

        _orders[orderRef] = new CartOrder(orderRef, session.SessionId, total, currency, lines);

        return new TravelCheckoutResult(orderRef, session.SessionId, total, currency);
    }

    public async Task<TravelConfirmationResult> ConfirmAsync(string orderRef, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(orderRef) || !_orders.TryGetValue(orderRef, out var order))
        {
            throw new ArgumentException("Carrito de viaje no encontrado.", nameof(orderRef));
        }

        // 1) Capturar el pago del carrito completo (idempotente en el PSP).
        var capture = await _payments.CaptureAsync(order.PaymentSessionId, cancellationToken);
        if (capture.Status != PaymentStatus.Captured)
        {
            throw new InvalidOperationException(
                capture.FailureReason ?? $"No se pudo capturar el pago del carrito (estado {capture.Status}).");
        }

        // 2) Confirmar TODAS las reservas del carrito (ConfirmAsync es idempotente
        //    por reserva: re-confirmar deja Confirmed sin doble efecto). Si el hold
        //    de un ítem venció, ConfirmAsync lanza InvalidOperationException →
        //    burbujea como política de compensación a futuro (rollback/reembolso).
        var confirmedItems = new List<TravelOrderItem>(order.Lines.Count);
        foreach (var line in order.Lines)
        {
            var confirmed = await _reservations.ConfirmAsync(line.ReservationId, order.PaymentSessionId, cancellationToken);
            confirmedItems.Add(new TravelOrderItem(
                Product: line.Product,
                OfferId: line.OfferId,
                Label: line.Label,
                ReservationId: confirmed.Id,
                Status: confirmed.Status.ToString(),
                Price: line.Price,
                Currency: line.Currency));
        }

        var allConfirmed = confirmedItems.All(i =>
            string.Equals(i.Status, ReservationStatus.Confirmed.ToString(), StringComparison.Ordinal));

        return new TravelConfirmationResult(
            OrderRef: order.OrderRef,
            Status: allConfirmed ? ReservationStatus.Confirmed.ToString() : "Partial",
            ConfirmationCode: BuildConfirmationCode(order.OrderRef),
            Items: confirmedItems);
    }

    // Código de confirmación human-facing derivado determinísticamente del
    // orderRef (idempotente: re-confirmar el mismo orderRef da el mismo código).
    private static string BuildConfirmationCode(string orderRef)
        => "SYN-" + orderRef.Replace("trip_", string.Empty, StringComparison.Ordinal)[..8].ToUpperInvariant();

    private readonly record struct CartLine(
        TravelProductType Product,
        string OfferId,
        string Label,
        string ReservationId,
        decimal Price,
        string Currency);

    private sealed record CartOrder(
        string OrderRef,
        string PaymentSessionId,
        decimal Total,
        string Currency,
        IReadOnlyList<CartLine> Lines);
}
