using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Controllers;

namespace Synergos.CMS.Tests.Controllers;

/// <summary>
/// La forma de «Mis viajes» por la ruta directa (<c>GET /api/travel/trips</c>), con las claves que lee travel-shell
/// (lote 10, 006-H1; ADR 0083: la UI es la fuente de verdad).
/// </summary>
/// <remarks>
/// Medido en vivo en la copia piloto: el servidor contestaba el viaje y la pantalla lo descartaba, porque su normalizador
/// exige <c>ref</c> y <c>title</c> y aquí sólo había <c>orderRef</c> y las etiquetas de cada parte. Desde que «Mis viajes»
/// junta lo de la puerta con lo de antes, lo de antes no salía nunca. Lo que ya se emitía sigue igual.
/// </remarks>
public sealed class TravelControllerTripsTests
{
    private const string Yo = "viajera@correo.co";
    private static readonly DateTimeOffset Ahora = new(2026, 10, 25, 18, 0, 0, TimeSpan.FromHours(-5));

    private readonly ITravelCartService _cart = Substitute.For<ITravelCartService>();
    private readonly IMemberAccessGate _gate = Substitute.For<IMemberAccessGate>();

    private TravelController BuildSut() => new(
        Substitute.For<IRoomAvailabilityProvider>(), Substitute.For<IFlightAvailabilityProvider>(),
        Substitute.For<ICarRentalProvider>(), _cart, Substitute.For<IStayContentProvider>(),
        Substitute.For<IPriceFormatter>(), _gate);

    private static TravelOrder Orden(params TravelOrderItem[] partes) => new(
        "trip_1", "Confirmed", "SYN-0001", "Ana", Yo, partes, 1_000_000m, "COP", Ahora, Ahora, "confirmed");

    private static TravelOrderItem Parte(TravelProductType producto, string etiqueta, decimal precio) =>
        new(producto, "OFERTA", etiqueta, string.Empty, "Confirmed", precio, "COP");

    private async Task<JsonElement> ViajesDe(params TravelOrder[] ordenes)
    {
        _gate.IsAuthenticated.Returns(true);
        _gate.CurrentMemberEmail.Returns(Yo);
        _cart.GetTripsAsync(Yo, Arg.Any<CancellationToken>()).Returns(ordenes);

        var ok = Assert.IsType<OkObjectResult>(await BuildSut().Trips(default));
        // Como lo serializa ASP.NET: camelCase.
        return JsonSerializer.SerializeToElement(ok.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web)).GetProperty("trips");
    }

    [Fact] // happy: ref, title del viaje y title de cada parte; lo de antes sigue
    public async Task Cada_viaje_trae_ref_y_title_y_cada_parte_su_title()
    {
        var viajes = await ViajesDe(Orden(
            Parte(TravelProductType.Flight, "Bogotá → Cartagena · SYN2200", 340_000m),
            Parte(TravelProductType.Hotel, " Habitación Estándar ", 660_000m)));

        var viaje = Assert.Single(viajes.EnumerateArray());
        Assert.Equal("trip_1", viaje.GetProperty("ref").GetString());
        Assert.Equal("Bogotá → Cartagena · SYN2200 · Habitación Estándar", viaje.GetProperty("title").GetString());
        Assert.Equal("Bogotá → Cartagena · SYN2200", viaje.GetProperty("items")[0].GetProperty("title").GetString());
        Assert.Equal("trip_1", viaje.GetProperty("orderRef").GetString());
        Assert.Equal("SYN-0001", viaje.GetProperty("confirmationCode").GetString());
        Assert.Equal("Flight", viaje.GetProperty("items")[0].GetProperty("product").GetString());
        Assert.Equal("Bogotá → Cartagena · SYN2200", viaje.GetProperty("items")[0].GetProperty("label").GetString());
    }

    [Fact] // empty: sin partes con nombre, el título es su código
    public async Task Sin_partes_con_nombre_el_titulo_es_su_codigo()
    {
        var viajes = await ViajesDe(Orden(Parte(TravelProductType.Car, "  ", 95_000m)), Orden() with { ConfirmationCode = null });

        Assert.Equal("SYN-0001", viajes[0].GetProperty("title").GetString());
        Assert.Equal("trip_1", viajes[1].GetProperty("title").GetString());
    }
}
