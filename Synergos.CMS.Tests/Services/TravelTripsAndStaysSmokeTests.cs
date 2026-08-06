using System;
using System.Linq;
using System.Threading.Tasks;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Xunit;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// SMOKE de la cara viajera OLA 2 Booking (doc 21 §2.2) — mínimo viable, no
/// suite exhaustiva: Trips (GetTripsAsync/GetOrderAsync por email del guest),
/// MMB v1 (CancelOrderAsync: cancela reservas + refund si capturado,
/// idempotente), tracking de viaje al confirmar (pipeline travel) y ficha de
/// estadía rica con geo (StubStayContentProvider).
/// </summary>
public class TravelTripsAndStaysSmokeTests
{
    private const string TravelerEmail = "viajera@synergos.test";

    private static (TravelCartService Cart, StubReservationService Reservations) Make()
    {
        var reservations = new StubReservationService();
        var cart = new TravelCartService(
            reservations,
            new StubPaymentProvider(),
            new StubOrderTrackingService(TravelCartService.TravelPipeline, null),
            null);
        return (cart, reservations);
    }

    private static TravelCartItem[] Items() => new[]
    {
        new TravelCartItem(TravelProductType.Hotel, "DLX/DLX-FLEX", "Habitación Deluxe (3 noches)", 600_000m, "COP"),
        new TravelCartItem(TravelProductType.Flight, "SYN1010-ECOBAS", "Vuelo BOG→MDE Economy", 450_000m, "COP"),
    };

    private static TravelGuest Guest() => new("Viajera Prueba", TravelerEmail);

    [Fact] // empty: viajero sin órdenes → lista vacía
    public async Task Trips_UnknownTraveler_ReturnsEmpty()
    {
        var (cart, _) = Make();
        Assert.Empty(await cart.GetTripsAsync("nadie@synergos.test"));
        Assert.Empty(await cart.GetTripsAsync("  "));
    }

    [Fact] // happy: checkout+confirm → el trip aparece con estado, código y etapa del timeline
    public async Task Trips_AfterCheckoutAndConfirm_ListsConfirmedTrip()
    {
        var (cart, _) = Make();
        var checkout = await cart.CheckoutAsync(Items(), Guest());
        await cart.ConfirmAsync(checkout.OrderRef);

        var trips = await cart.GetTripsAsync(TravelerEmail);

        var trip = Assert.Single(trips);
        Assert.Equal(checkout.OrderRef, trip.OrderRef);
        Assert.Equal("Confirmed", trip.Status);
        Assert.False(string.IsNullOrWhiteSpace(trip.ConfirmationCode));
        Assert.Equal(1_050_000m, trip.Total);
        Assert.Equal(2, trip.Items.Count);
        Assert.All(trip.Items, i => Assert.Equal("Confirmed", i.Status));
        // Tracking de viaje alimentado al confirmar (pipeline paid→confirmed→…).
        Assert.Equal(TravelCartService.StageConfirmed, trip.CurrentStage);
        // GetOrderAsync devuelve la misma orden; ref desconocido → null.
        Assert.NotNull(await cart.GetOrderAsync(checkout.OrderRef));
        Assert.Null(await cart.GetOrderAsync("trip_desconocido"));
    }

    [Fact] // MMB happy + idempotent: cancelar tras confirmar reembolsa una sola vez
    public async Task CancelOrder_AfterConfirm_RefundsAndIsIdempotent()
    {
        var (cart, reservations) = Make();
        var checkout = await cart.CheckoutAsync(Items(), Guest());
        await cart.ConfirmAsync(checkout.OrderRef);

        var first = await cart.CancelOrderAsync(checkout.OrderRef);

        Assert.Equal("Cancelled", first.Status);
        Assert.True(first.Refunded);
        Assert.Equal(1_050_000m, first.RefundAmount);

        // Cada reserva del carrito quedó Cancelled en el motor.
        var trip = await cart.GetOrderAsync(checkout.OrderRef);
        Assert.NotNull(trip);
        Assert.Equal("Cancelled", trip!.Status);
        foreach (var item in trip.Items)
        {
            var reservation = await reservations.GetAsync(item.ReservationId);
            Assert.Equal(ReservationStatus.Cancelled, reservation!.Status);
        }

        // Idempotente: re-cancelar devuelve lo mismo, sin doble reembolso.
        var second = await cart.CancelOrderAsync(checkout.OrderRef);
        Assert.Equal(first, second);

        // Ref desconocido → ArgumentException (el controller lo mapea a 404).
        await Assert.ThrowsAsync<ArgumentException>(() => cart.CancelOrderAsync("trip_desconocido"));
    }

    [Fact] // MMB pre-confirm: sin pago capturado no hay reembolso, solo liberación
    public async Task CancelOrder_BeforeConfirm_CancelsWithoutRefund()
    {
        var (cart, _) = Make();
        var checkout = await cart.CheckoutAsync(Items(), Guest());

        var result = await cart.CancelOrderAsync(checkout.OrderRef);

        Assert.Equal("Cancelled", result.Status);
        Assert.False(result.Refunded);
        Assert.Equal(0m, result.RefundAmount);
        // Un carrito cancelado no se puede confirmar después.
        await Assert.ThrowsAsync<InvalidOperationException>(() => cart.ConfirmAsync(checkout.OrderRef));
    }

    [Fact] // ficha rica: por stayId, por room type y por offerId del search; geo real
    public async Task Stay_ResolvesByIdRoomTypeAndOfferId_WithRichContent()
    {
        var stays = new StubStayContentProvider();

        var byRoomType = await stays.GetStayAsync("DLX");
        var byOfferId = await stays.GetStayAsync("DLX/DLX-FLEX");
        Assert.NotNull(byRoomType);
        Assert.Equal(byRoomType!.StayId, byOfferId!.StayId);

        var byId = await stays.GetStayAsync("ctg-getsemani");
        Assert.NotNull(byId);
        Assert.Equal("Cartagena", byId!.City);
        Assert.NotEqual(0d, byId.Geo.Lat);
        Assert.NotEqual(0d, byId.Geo.Lng);
        // La galería es UNA portada por estadía, no una tira de fotos: `GalleryFor`
        // emite `/media/booking/{stayId}.jpg` y en `wwwroot/media/booking/` hay
        // exactamente 6 ficheros, uno por estadía. Nació así (88cf6b7); el `>= 3`
        // era un umbral inventado que el stub NUNCA cumplió, sin gemelo en el
        // resto de la suite —el test hermano de propiedades afirma `NotEmpty`— y
        // sin nadie que lo lea: ninguna vista de travel-shell consume `gallery`.
        // Se afirma lo que el contrato garantiza de verdad (hay portada) con el
        // idioma del repo. Si algún día la ficha pinta miniaturas, lo que falta
        // son IMÁGENES en disco, no un número más alto aquí.
        Assert.NotEmpty(byId.Gallery);
        Assert.NotEmpty(byId.Amenities);
        Assert.NotEmpty(byId.Specs);
        Assert.False(string.IsNullOrWhiteSpace(byId.Description));
        Assert.InRange(byId.Reviews.Average, 1d, 10d);
        Assert.NotEmpty(byId.Reviews.Categories);

        Assert.Null(await stays.GetStayAsync("no-existe"));
    }

    [Fact] // geo en search: los 4 room types del stub de disponibilidad tienen estadía con geo
    public async Task Stay_EveryRoomTypeOfAvailabilityStub_HasGeo()
    {
        var stays = new StubStayContentProvider();
        foreach (var code in new[] { "STD", "DLX", "STE", "FAM" })
        {
            var stay = await stays.GetStayAsync(code);
            Assert.NotNull(stay);
            Assert.NotEqual(0d, stay!.Geo.Lat);
            Assert.NotEqual(0d, stay.Geo.Lng);
        }
    }
}
