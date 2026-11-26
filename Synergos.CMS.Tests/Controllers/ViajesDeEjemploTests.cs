using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Controllers;

namespace Synergos.CMS.Tests.Controllers;

/// <summary>
/// Viajes con el catálogo de muestra (016, D-016-C25): el destino filtra, el régimen va en palabras, el precio dice de
/// cuántas noches es, nadie inventa opiniones, y ningún proveedor ni hotel lleva el nombre de uno real.
/// </summary>
public sealed class ViajesDeEjemploTests
{
    private static readonly DateOnly Entrada = new(2026, 12, 10);
    private static readonly DateOnly Salida = new(2026, 12, 12);

    private static TravelController Sut()
    {
        var precios = Substitute.For<IPriceFormatter>();
        precios.Format(Arg.Any<decimal>(), Arg.Any<string?>()).Returns(ci => $"$ {ci.ArgAt<decimal>(0)}");
        return new TravelController(new StubRoomAvailabilityProvider(), Substitute.For<IFlightAvailabilityProvider>(),
            new StubCarRentalProvider(), Substitute.For<ITravelCartService>(), new StubStayContentProvider(), precios,
            Substitute.For<IMemberAccessGate>());
    }

    private static List<JsonElement> Ofertas(IActionResult resultado)
    {
        var ok = Assert.IsType<OkObjectResult>(resultado);
        return JsonSerializer.SerializeToElement(ok.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            .GetProperty("offers").EnumerateArray().ToList();
    }

    [Theory]
    [InlineData("Cartagena, Colombia", "Cartagena")]
    [InlineData("medellin", "Medellín")]
    public async Task El_destino_filtra_por_la_ciudad_de_la_estadia(string destino, string ciudad)
    {
        var ofertas = Ofertas(await Sut().SearchHotel(Entrada, Salida, destination: destino));

        Assert.NotEmpty(ofertas);
        Assert.All(ofertas, o => Assert.Equal(ciudad, o.GetProperty("city").GetString()));
    }

    [Fact]
    public async Task Sin_destino_salen_todas_y_un_destino_sin_estadias_no_inventa_ninguna()
    {
        var todas = Ofertas(await Sut().SearchHotel(Entrada, Salida));
        var ninguna = Ofertas(await Sut().SearchHotel(Entrada, Salida, destination: "Leticia"));

        Assert.True(todas.Select(o => o.GetProperty("city").GetString()).Distinct().Count() > 1);
        Assert.Empty(ninguna);
    }

    [Fact]
    public async Task La_oferta_dice_el_regimen_en_palabras_las_noches_y_no_inventa_valoracion()
    {
        var ofertas = Ofertas(await Sut().SearchHotel(Entrada, Salida));

        Assert.All(ofertas, o =>
        {
            Assert.DoesNotMatch(@"·\s*(BB|RO)\b", o.GetProperty("subtitle").GetString()!);
            Assert.Contains(o.GetProperty("board").GetString(), new[] { "Desayuno incluido", "Solo alojamiento" });
            Assert.Equal("por 2 noches", o.GetProperty("priceNote").GetString());
            Assert.Equal(JsonValueKind.Null, o.GetProperty("rating").ValueKind);
        });
    }

    [Fact]
    public async Task Ningun_proveedor_ni_hotel_lleva_el_nombre_de_uno_real_y_no_hay_direcciones_exactas()
    {
        var reales = new[] { "Localiza", "Hertz", "Avis", "Provenza Lofts", "Mirador del Cocora" };
        var autos = await new StubCarRentalProvider().SearchAsync(new CarRentalQuery("CTG", null, Entrada, Salida));
        var estadias = new StubStayContentProvider();
        var nombres = new List<string>(autos.Select(a => a.Provider));
        foreach (var id in new[] { "ctg-getsemani", "ctg-bocagrande", "med-provenza", "bog-zonat", "eje-salento", "eje-filandia" })
        {
            var estadia = (await estadias.GetStayAsync(id))!;
            nombres.Add(estadia.Name);
            Assert.DoesNotContain("#", estadia.Address, StringComparison.Ordinal);
            Assert.Equal(0, estadia.Reviews.Count);
        }

        Assert.NotEmpty(autos);
        foreach (var real in reales)
        {
            Assert.DoesNotContain(nombres, n => n.Contains(real, StringComparison.OrdinalIgnoreCase));
        }
    }
}
