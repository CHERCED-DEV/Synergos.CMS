using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Xunit;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Cubre <see cref="TravelCartService"/> (seam <see cref="ITravelCartService"/>,
/// carrito de viaje multi-producto — motor transaccional del dominio Booking):
/// los casos canónicos (ADR 0075) + multi-ítem + expired —
/// empty / happy / multi-item / idempotent / expired.
/// Compone los seams reales (<see cref="StubReservationService"/> +
/// <see cref="StubPaymentProvider"/>) para ejercer el flujo end-to-end.
/// </summary>
public class TravelCartServiceTests
{
    private static TravelGuest Guest() => new("Ada Lovelace", "ada@synergos.test");

    private static ITravelCartService Make(IReservationService? reservations = null, IPaymentProvider? payments = null)
        => new TravelCartService(reservations ?? new StubReservationService(), payments ?? new StubPaymentProvider());

    // El periodo de cada ítem es obligatorio (HU #40) y cada producto lo lleva a su
    // escala: el hotel de medianoche a medianoche UTC —la regla de la vía hotel, para
    // no inventar una segunda— y el vuelo con hora real, que es lo que una ventana de
    // `Api.Booking` necesita para no solaparse con otra.
    private static readonly DateTimeOffset Dia1 = new(2026, 9, 10, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Dia4 = new(2026, 9, 13, 0, 0, 0, TimeSpan.Zero);

    private static TravelCartItem Hotel(decimal price = 600_000m)
        => new(TravelProductType.Hotel, "DLX/BB", "Habitación Deluxe (3 noches)", price, "COP", Dia1, Dia4);

    private static TravelCartItem Flight(decimal price = 450_000m)
        => new(TravelProductType.Flight, "SYN1010-ECOBAS", "Vuelo BOG→MDE Economy", price, "COP",
            new DateTimeOffset(2026, 9, 10, 14, 30, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 10, 15, 45, 0, TimeSpan.Zero));

    private static TravelCartItem Car(decimal price = 285_000m)
        => new(TravelProductType.Car, "ECON", "Auto Económico (3 días)", price, "COP", Dia1, Dia4);

    [Fact] // empty: carrito vacío lanza
    public async Task Checkout_EmptyCart_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => Make().CheckoutAsync(Array.Empty<TravelCartItem>(), Guest()));
    }

    [Fact] // empty/inválido: ítem con precio ≤ 0 lanza
    public async Task Checkout_NonPositivePrice_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => Make().CheckoutAsync(new[] { Hotel(price: 0m) }, Guest()));
    }

    [Fact] // inválido: monedas mezcladas lanza (un solo total = una sola moneda)
    public async Task Checkout_MixedCurrencies_Throws()
    {
        var items = new[]
        {
            Hotel(),
            new TravelCartItem(TravelProductType.Flight, "F1", "Vuelo internacional", 1_200_000m, "USD", new DateTimeOffset(2026, 9, 10, 14, 30, 0, TimeSpan.Zero), new DateTimeOffset(2026, 9, 10, 15, 45, 0, TimeSpan.Zero)),
        };
        await Assert.ThrowsAsync<ArgumentException>(() => Make().CheckoutAsync(items, Guest()));
    }

    [Fact] // inválido: el periodo tiene que AVANZAR — se rechaza en el borde (HU #40)
    public async Task Checkout_VentanaQueNoAvanza_Throws()
    {
        var mismoInstante = new DateTimeOffset(2026, 9, 10, 0, 0, 0, TimeSpan.Zero);
        var item = Hotel() with { Start = mismoInstante, End = mismoInstante };

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => Make().CheckoutAsync(new[] { item }, Guest()));
        Assert.Contains("periodo", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact] // y al revés tampoco: fin antes que inicio
    public async Task Checkout_VentanaInvertida_Throws()
    {
        var item = Hotel() with
        {
            Start = new DateTimeOffset(2026, 9, 13, 0, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 9, 10, 0, 0, 0, TimeSpan.Zero),
        };

        await Assert.ThrowsAsync<ArgumentException>(
            () => Make().CheckoutAsync(new[] { item }, Guest()));
    }

    [Fact] // happy: un ítem → orderRef + sesión de pago + monto del ítem
    public async Task Checkout_SingleItem_OpensSessionForTotal()
    {
        var cart = Make();
        var result = await cart.CheckoutAsync(new[] { Hotel(price: 600_000m) }, Guest());

        Assert.False(string.IsNullOrWhiteSpace(result.OrderRef));
        Assert.False(string.IsNullOrWhiteSpace(result.PaymentSessionId));
        Assert.Equal(600_000m, result.Amount);
        Assert.Equal("COP", result.Currency);
    }

    [Fact] // multi-item: el total es la suma; confirm devuelve una reserva por ítem
    public async Task Checkout_MultiItem_SumsTotal_AndConfirmsAllReservations()
    {
        var reservations = new StubReservationService();
        var cart = Make(reservations);
        var items = new[] { Hotel(600_000m), Flight(450_000m), Car(285_000m) };

        var checkout = await cart.CheckoutAsync(items, Guest());
        Assert.Equal(1_335_000m, checkout.Amount); // 600k + 450k + 285k

        var confirm = await cart.ConfirmAsync(checkout.OrderRef);

        Assert.Equal(ReservationStatus.Confirmed.ToString(), confirm.Status);
        Assert.Equal(3, confirm.Items.Count);
        Assert.All(confirm.Items, i => Assert.Equal(ReservationStatus.Confirmed.ToString(), i.Status));
        // Una reserva real por ítem, ligada al motor de reservas.
        Assert.Equal(3, confirm.Items.Select(i => i.ReservationId).Distinct().Count());
        foreach (var item in confirm.Items)
        {
            var stored = await reservations.GetAsync(item.ReservationId);
            Assert.NotNull(stored);
            Assert.Equal(ReservationStatus.Confirmed, stored!.Status);
        }
        // Cubre los 3 productos heterogéneos en una sola transacción.
        Assert.Equal(
            new[] { "Car", "Flight", "Hotel" },
            confirm.Items.Select(i => i.Product.ToString()).OrderBy(p => p, StringComparer.Ordinal));
        Assert.False(string.IsNullOrWhiteSpace(confirm.ConfirmationCode));
    }

    [Fact] // idempotent: re-confirmar el mismo orderRef da el mismo resultado (sin doble efecto)
    public async Task Confirm_Twice_IsIdempotent()
    {
        var cart = Make();
        var checkout = await cart.CheckoutAsync(new[] { Hotel(), Flight() }, Guest());

        var first = await cart.ConfirmAsync(checkout.OrderRef);
        var second = await cart.ConfirmAsync(checkout.OrderRef);

        Assert.Equal(first.Status, second.Status);
        Assert.Equal(first.ConfirmationCode, second.ConfirmationCode);
        Assert.Equal(
            first.Items.Select(i => i.ReservationId).OrderBy(x => x),
            second.Items.Select(i => i.ReservationId).OrderBy(x => x));
    }

    [Fact] // inválido: confirmar un orderRef inexistente lanza
    public async Task Confirm_UnknownOrder_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Make().ConfirmAsync("trip_doesnotexist"));
    }

    [Fact] // expired: si el hold de un ítem vence antes de confirmar, Confirm falla
    public async Task Confirm_AfterHoldExpired_Throws()
    {
        // Reloj controlable + ventana de hold corta: el hold vence entre checkout y confirm.
        var now = new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);
        var reservations = new StubReservationService(TimeSpan.FromMinutes(10), () => now);
        var cart = Make(reservations);

        var checkout = await cart.CheckoutAsync(new[] { Hotel(), Flight() }, Guest());

        // Avanzar el reloj más allá de la ventana de hold.
        now = now.AddMinutes(20);

        await Assert.ThrowsAsync<InvalidOperationException>(() => cart.ConfirmAsync(checkout.OrderRef));
    }

    // ── Fan-out T1 (doc 25) — durabilidad e2e del carrito de viaje ──

    [Fact] // durabilidad: checkout con un servicio, confirm con OTRO sobre los MISMOS
           // stores (orden de viaje + reserva + pago) — proxy de reinicio del CMS.
    public async Task ConfirmAfterServiceReplacement_ViaSharedStores_Succeeds()
    {
        // Los TRES estados que ConfirmAsync toca deben ser durables: la orden de viaje,
        // las reservas y la sesión de pago. Con el store GENÉRICO basta UNA instancia
        // para las tres familias — el resourceType las aísla entre sí.
        var store = new InMemoryJsonEntityStore();

        var beforeRestart = new TravelCartService(
            new StubReservationService(StubReservationService.DefaultHoldWindow, null, store),
            new StubPaymentProvider(store),
            null, null, store);
        var checkout = await beforeRestart.CheckoutAsync(new[] { Hotel(), Flight() }, Guest());

        // "Reinicio": instancias NUEVAS del servicio + motores sobre los MISMOS stores.
        var afterRestart = new TravelCartService(
            new StubReservationService(StubReservationService.DefaultHoldWindow, null, store),
            new StubPaymentProvider(store),
            null, null, store);
        var confirmation = await afterRestart.ConfirmAsync(checkout.OrderRef);

        Assert.Equal(ReservationStatus.Confirmed.ToString(), confirmation.Status);
        Assert.False(string.IsNullOrWhiteSpace(confirmation.ConfirmationCode));
        Assert.Equal(2, confirmation.Items.Count);
        // "Mis viajes" también resuelve la orden persistida tras el reinicio.
        var trips = await afterRestart.GetTripsAsync("ada@synergos.test");
        Assert.Single(trips);
    }
}
