using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Controllers;

namespace Synergos.CMS.Tests.Controllers;

/// <summary>
/// La consola del agente CRUZADA con el servicio de leads de verdad (008-propiedades-visita, D-1): el agente con sesión
/// ve los leads de los inmuebles que declaran su correo, y sólo esos; el administrador ve además los «sin agente».
/// </summary>
/// <remarks>
/// <para><b>Por qué cruzada.</b> El defecto no lo veía ninguna prueba porque cada lado se probaba solo: la del controller
/// simulaba el servicio con el correo del agente y la del servicio pedía el slug del nombre. Los dos estaban bien por
/// separado y juntos no casaban nunca: la consola pedía por <c>ana@inmo.co</c> y el lead estaba guardado como
/// <c>ana-gomez</c>.</para>
/// </remarks>
public sealed class RealtyAgentLeadsCruzadoTests
{
    private const string Ana = "ana@inmo.co";
    private const string Beto = "beto@inmo.co";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private readonly IPropertyCatalogProvider _catalog = Substitute.For<IPropertyCatalogProvider>();
    private readonly IMemberAccessGate _gate = Substitute.For<IMemberAccessGate>();

    public RealtyAgentLeadsCruzadoTests()
    {
        _catalog.GetListingAsync("L-ANA", Arg.Any<CancellationToken>()).Returns(Ficha("L-ANA", "Ana Gómez", Ana));
        _catalog.GetListingAsync("L-BETO", Arg.Any<CancellationToken>()).Returns(Ficha("L-BETO", "Beto Ruiz", Beto));
        _catalog.GetListingAsync("L-SIN", Arg.Any<CancellationToken>()).Returns(Ficha("L-SIN", "Sin Correo", null));
    }

    private static PropertyDetail Ficha(string id, string agente, string? correo) => new(
        new PropertyListing(id, id.ToLowerInvariant(), $"Inmueble {id}", "venta", "apartamento", 1m, "COP", "Bogotá", "Chicó",
            3, 2, 98, 6, 4.6736, -74.0556, string.Empty),
        "desc", Array.Empty<PropertySpec>(), Array.Empty<string>(), Array.Empty<string>(),
        new PropertyLocation(4.6736, -74.0556, "Cra 11", "Chicó", "Bogotá"),
        agente, "+57 300", correo);

    private StubLeadCaptureService Leads() => new(null, null, _catalog, () => new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero));

    private RealtyController Consola(ILeadCaptureService leads) => new(
        _catalog, Substitute.For<IVisitSchedulingService>(), Substitute.For<IMortgageCalculator>(), leads,
        Substitute.For<IUserCollection>(), Substitute.For<ISavedSearchService>(), Substitute.For<IPriceFormatter>(),
        _gate, new RealtyVisitLedger());

    private void ConSesion(string correo, bool admin = false)
    {
        _gate.IsAuthenticated.Returns(true);
        _gate.CurrentMemberEmail.Returns(correo);
        _gate.HasAnyRole("agente,admin").Returns(true);
        _gate.HasAnyRole("admin").Returns(admin);
    }

    private static List<string> Ids(IActionResult resultado)
    {
        var ok = Assert.IsType<OkObjectResult>(resultado);
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, Web));
        return doc.RootElement.GetProperty("leads").EnumerateArray().Select(l => l.GetProperty("listingId").GetString()!).ToList();
    }

    [Fact]
    public async Task El_agente_ve_los_leads_de_los_inmuebles_que_declaran_su_correo()
    {
        var leads = Leads();
        await leads.CaptureAsync("L-ANA", new VisitContact("Mariana", "mariana@ejemplo.co"), "¿Sigue disponible?");
        await leads.CaptureAsync("L-BETO", new VisitContact("Julián", "julian@ejemplo.co"), "Quiero verla el sábado.");
        ConSesion(Ana);

        var deAna = Ids(await Consola(leads).AgentLeads(default));

        Assert.Equal(["L-ANA"], deAna);
    }

    [Fact]
    public async Task El_correo_se_compara_sin_mayusculas_ni_espacios()
    {
        var leads = Leads();
        await leads.CaptureAsync("L-ANA", new VisitContact("Mariana", "mariana@ejemplo.co"), "¿Sigue disponible?");
        ConSesion(" ANA@inmo.co ");

        Assert.Equal(["L-ANA"], Ids(await Consola(leads).AgentLeads(default)));
    }

    [Fact]
    public async Task Un_lead_de_un_inmueble_sin_correo_no_es_de_ningun_agente_y_lo_ve_el_administrador()
    {
        var leads = Leads();
        await leads.CaptureAsync("L-SIN", new VisitContact("Sofía", "sofia@ejemplo.co"), "¿Acepta crédito?");
        await leads.CaptureAsync("L-ANA", new VisitContact("Mariana", "mariana@ejemplo.co"), "¿Sigue disponible?");

        ConSesion(Beto);
        Assert.Empty(Ids(await Consola(leads).AgentLeads(default)));

        ConSesion("jefa@inmo.co", admin: true);
        var delAdministrador = Ids(await Consola(leads).AgentLeads(default));
        // Los de la demostración también quedan «sin agente»: sus fichas no existen en este catálogo.
        Assert.Contains("L-SIN", delAdministrador);
        Assert.DoesNotContain("L-ANA", delAdministrador);
    }

    [Fact]
    public async Task Un_administrador_que_es_agente_ve_los_suyos_y_los_sin_agente_cada_uno_una_vez()
    {
        var leads = Leads();
        await leads.CaptureAsync("L-ANA", new VisitContact("Mariana", "mariana@ejemplo.co"), "¿Sigue disponible?");
        await leads.CaptureAsync("L-SIN", new VisitContact("Sofía", "sofia@ejemplo.co"), "¿Acepta crédito?");
        ConSesion(Ana, admin: true);

        var vistos = Ids(await Consola(leads).AgentLeads(default));

        Assert.Single(vistos, id => id == "L-ANA");
        Assert.Single(vistos, id => id == "L-SIN");
    }

    [Fact]
    public async Task Quien_publica_queda_como_el_agente_del_inmueble()
    {
        _catalog.PublishListingAsync(Arg.Any<PropertyDraft>(), Arg.Any<CancellationToken>())
            .Returns(Ficha("L-NUEVO", "Ana Gómez", Ana));
        ConSesion(Ana);
        var pedido = new RealtyController.PublishListingRequest(Title: "Casa", Type: "casa", Operation: "sale", Price: 1m, City: "Bogotá");

        await Consola(Leads()).PublishListing(pedido, default);

        await _catalog.Received(1).PublishListingAsync(Arg.Is<PropertyDraft>(d => d.AgentEmail == Ana), Arg.Any<CancellationToken>());
    }
}
