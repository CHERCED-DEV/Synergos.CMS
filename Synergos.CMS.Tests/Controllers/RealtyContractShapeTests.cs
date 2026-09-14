using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Controllers;
using Xunit;

namespace Synergos.CMS.Tests.Controllers;

/// <summary>
/// El cruce de contrato de <c>&lt;synergos-realty&gt;</c>: estos tests afirman la forma que
/// la UI LEE y el cuerpo que la UI MANDA, no lo que el controller decidió emitir.
/// </summary>
/// <remarks>
/// Los dos sitios donde este vertical se rompe caro —dice <c>CLAUDE.md</c>— son el precio y
/// el mapa. El precio estaba bien (viaja como número, no como texto es-CO); <b>el mapa no</b>:
/// el pin del wizard llegaba plano (<c>lat</c>/<c>lng</c>) y el borde solo leía
/// <c>geo:{lat,lng}</c>, así que todo inmueble publicado se guardaba en (0,0) y no salía en
/// el mapa sin que nada fallara. Y agendar una visita moría en el binding —la UI manda
/// <c>slot</c> como objeto y el borde lo declaraba <c>string</c>—, o sea el 100 % de las
/// veces.
/// <para>Se afirma sobre el JSON serializado con <see cref="JsonSerializerDefaults.Web"/>
/// (lo que usa MVC) y sobre cuerpos DESERIALIZADOS del JSON real del cliente: comparar
/// propiedades de C# no ve el casing ni una clave que el binder descarta.</para>
/// </remarks>
public sealed class RealtyContractShapeTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private readonly IPropertyCatalogProvider _catalog = Substitute.For<IPropertyCatalogProvider>();
    private readonly IVisitSchedulingService _visits = Substitute.For<IVisitSchedulingService>();
    private readonly IMortgageCalculator _mortgage = Substitute.For<IMortgageCalculator>();
    private readonly ILeadCaptureService _leads = Substitute.For<ILeadCaptureService>();
    private readonly IUserCollection _collections = Substitute.For<IUserCollection>();
    private readonly ISavedSearchService _savedSearches = Substitute.For<ISavedSearchService>();
    private readonly IPriceFormatter _priceFormatter = Substitute.For<IPriceFormatter>();
    private readonly IMemberAccessGate _gate = Substitute.For<IMemberAccessGate>();

    public RealtyContractShapeTests()
    {
        _priceFormatter.Format(Arg.Any<decimal>(), Arg.Any<string?>()).Returns("$ 0");
        _gate.IsAuthenticated.Returns(true);
        _gate.CurrentMemberEmail.Returns("agente@inmo.co");
        _gate.HasAnyRole(Arg.Any<string?>()).Returns(true);
    }

    private RealtyController BuildSut() => new(
        _catalog, _visits, _mortgage, _leads, _collections, _savedSearches, _priceFormatter, _gate);

    private static JsonElement Json(IActionResult result)
    {
        var ok = Assert.IsType<OkObjectResult>(result);
        return JsonSerializer.SerializeToElement(ok.Value, Web);
    }

    private static PropertyListing Listing(
        string id = "L-1",
        string operation = "venta",
        decimal price = 850_000_000m,
        int area = 98,
        double lat = 4.6736,
        double lng = -74.0556) => new(
        Id: id,
        Slug: "apartamento-chico",
        Title: "Apartamento en Chicó",
        Operation: operation,
        Type: "apartamento",
        Price: price,
        Currency: "COP",
        City: "Bogotá",
        Neighborhood: "Chicó",
        Beds: 3,
        Baths: 2,
        AreaM2: area,
        Stratum: 6,
        Lat: lat,
        Lng: lng,
        ImageUrl: "/media/realty/chico.jpg");

    private void CatalogReturns(params PropertyListing[] listings) =>
        _catalog.SearchAsync(Arg.Any<PropertyQuery>(), Arg.Any<CancellationToken>())
            .Returns(new PropertySearchResult(listings, Array.Empty<PropertyFacet>()));

    private static PropertyQuery CapturedQuery(IPropertyCatalogProvider catalog) =>
        (PropertyQuery)catalog.ReceivedCalls()
            .Single(c => c.GetMethodInfo().Name == nameof(IPropertyCatalogProvider.SearchAsync))
            .GetArguments()[0]!;

    // ── Search: los tres parámetros que entraban y se perdían ─────────────────────

    [Fact] // happy: "Arriendo" tiene que llegar al catálogo, traducido al vocabulario del dominio
    public async Task Listings_TraduceYAplicaLaOperacion()
    {
        CatalogReturns(Listing());

        await BuildSut().Listings(null, null, null, null, null, null, "rent", null, null, default);

        Assert.Equal("arriendo", CapturedQuery(_catalog).Operation);
    }

    [Fact] // empty: sin operación NO se inventa una — el portal no arranca filtrado
    public async Task Listings_SinOperacion_NoFiltra()
    {
        CatalogReturns(Listing());

        await BuildSut().Listings(null, null, null, null, null, null, null, null, null, default);

        Assert.Null(CapturedQuery(_catalog).Operation);
    }

    [Fact] // filter: el desplegable de orden no hacía nada contra el servidor real
    public async Task Listings_OrdenaPorPrecio()
    {
        CatalogReturns(Listing("L-1", price: 850_000_000m), Listing("L-2", price: 420_000_000m));

        var body = Json(await BuildSut().Listings(null, null, null, null, null, null, null, "price-asc", null, default));

        var ids = body.GetProperty("listings").EnumerateArray()
            .Select(l => l.GetProperty("id").GetString()).ToList();
        Assert.Equal(new[] { "L-2", "L-1" }, ids);
    }

    [Fact] // filter: "buscar al mover el mapa" recorta por viewport; `total` cuenta lo recortado
    public async Task Listings_RecortaPorElRectanguloDelMapa()
    {
        CatalogReturns(
            Listing("L-1", lat: 4.6736, lng: -74.0556),   // Bogotá — dentro
            Listing("L-2", lat: 6.2442, lng: -75.5812));  // Medellín — fuera

        var body = Json(await BuildSut().Listings(
            null, null, null, null, null, null, null, null, "4.5,-74.2,4.8,-74.0", default));

        var listado = body.GetProperty("listings");
        Assert.Equal(1, listado.GetArrayLength());
        Assert.Equal("L-1", listado[0].GetProperty("id").GetString());
        Assert.Equal(1, body.GetProperty("total").GetInt32());
    }

    [Fact] // empty: un bounds ilegible se IGNORA, no vacía el catálogo
    public async Task Listings_BoundsIlegible_NoVaciaElCatalogo()
    {
        CatalogReturns(Listing());

        var body = Json(await BuildSut().Listings(
            null, null, null, null, null, null, null, null, "no-es-un-rectangulo", default));

        Assert.Equal(1, body.GetProperty("listings").GetArrayLength());
    }

    // ── Escritura: agendar una visita ─────────────────────────────────────────────

    [Fact] // happy: el cuerpo REAL manda slot como OBJETO; declarado string moría en el binding
    public async Task Visit_AceptaElSlotComoObjeto_YLoResuelveContraLaAgenda()
    {
        var inicio = new DateTimeOffset(2026, 7, 10, 9, 0, 0, TimeSpan.Zero);
        _visits.GetSlotsAsync("L-1", Arg.Any<CancellationToken>())
            .Returns(new[] { new Synergos.CMS.Interfaces.VisitSlot("L-1-202607100900", inicio) });
        _visits.BookAsync("L-1", "L-1-202607100900", Arg.Any<VisitContact>(), Arg.Any<CancellationToken>())
            .Returns(new VisitResult("visit_1", "Confirmed"));

        // El cuerpo tal cual lo serializa `scheduleVisit()` del cliente Angular.
        var request = JsonSerializer.Deserialize<RealtyController.VisitRequest>(
            """
            {
              "listingId": "L-1",
              "slot": { "date": "2026-07-10", "time": "09:00" },
              "contact": { "name": "Ana Ruiz", "email": "ana@correo.co", "phone": "3001234567" },
              "mode": "in-person"
            }
            """, Web);

        var body = Json(await BuildSut().Visit(request, default));

        await _visits.Received(1).BookAsync("L-1", "L-1-202607100900", Arg.Any<VisitContact>(), Arg.Any<CancellationToken>());

        // Sin `id` en la raíz del objeto `visit`, el normalizador del cliente devuelve null
        // y da la cita por caída — inventando una confirmada.
        var visita = body.GetProperty("visit");
        Assert.Equal("visit_1", visita.GetProperty("id").GetString());
        Assert.Equal("L-1", visita.GetProperty("listingId").GetString());
        Assert.Equal("2026-07-10", visita.GetProperty("slot").GetProperty("date").GetString());
        Assert.Equal("09:00", visita.GetProperty("slot").GetProperty("time").GetString());
    }

    [Fact] // filter: una franja que el agente no atiende se RECHAZA, no se inventa un id
    public async Task Visit_FranjaFueraDeLaAgenda_SeRechaza_YNoTocaElSeam()
    {
        _visits.GetSlotsAsync("L-1", Arg.Any<CancellationToken>())
            .Returns(Array.Empty<Synergos.CMS.Interfaces.VisitSlot>());

        var request = JsonSerializer.Deserialize<RealtyController.VisitRequest>(
            """
            {
              "listingId": "L-1",
              "slot": { "date": "2026-07-10", "time": "16:00" },
              "contact": { "name": "Ana Ruiz", "email": "ana@correo.co", "phone": "3001234567" }
            }
            """, Web);

        Assert.IsType<BadRequestObjectResult>(await BuildSut().Visit(request, default));
        await _visits.DidNotReceive().BookAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<VisitContact>(), Arg.Any<CancellationToken>());
    }

    [Fact] // idempotent-ish: la forma anterior (slot como id) sigue entrando
    public async Task Visit_AceptaTambienElSlotComoCadena()
    {
        _visits.BookAsync("L-1", "L-1-202607100900", Arg.Any<VisitContact>(), Arg.Any<CancellationToken>())
            .Returns(new VisitResult("visit_1", "Confirmed"));
        _visits.GetSlotsAsync("L-1", Arg.Any<CancellationToken>())
            .Returns(Array.Empty<Synergos.CMS.Interfaces.VisitSlot>());

        var request = JsonSerializer.Deserialize<RealtyController.VisitRequest>(
            """
            {
              "listingId": "L-1",
              "slot": "L-1-202607100900",
              "contact": { "name": "Ana Ruiz", "email": "ana@correo.co" }
            }
            """, Web);

        Assert.IsType<OkObjectResult>(await BuildSut().Visit(request, default));
        await _visits.Received(1).BookAsync("L-1", "L-1-202607100900", Arg.Any<VisitContact>(), Arg.Any<CancellationToken>());
    }

    // ── Escritura: publicar un inmueble (wizard SH-6) ─────────────────────────────

    [Fact] // happy: EL PIN. Llega plano y se tiraba → todo inmueble publicado en (0,0)
    public async Task PublishListing_ConservaElPinDelMapa_YElAreaConstruida()
    {
        _catalog.PublishListingAsync(Arg.Any<PropertyDraft>(), Arg.Any<CancellationToken>())
            .Returns(new PropertyDetail(
                Listing(), "desc", Array.Empty<PropertySpec>(), Array.Empty<string>(),
                Array.Empty<string>(), new PropertyLocation(4.6736, -74.0556, "Cra 11 #93-45", "Chicó", "Bogotá"),
                "Agente", ""));

        // El cuerpo tal cual lo serializa `publishListing()` (PublishListingRequest del
        // realty.model.ts): lat/lng PLANOS y `areaBuilt`.
        var request = JsonSerializer.Deserialize<RealtyController.PublishListingRequest>(
            """
            {
              "title": "Apartamento en Chicó",
              "operation": "sale",
              "type": "apartamento",
              "price": 850000000,
              "city": "Bogotá",
              "neighborhood": "Chicó",
              "address": "Cra 11 #93-45",
              "lat": 4.6736,
              "lng": -74.0556,
              "beds": 3,
              "baths": 2,
              "areaBuilt": 98,
              "stratum": 6
            }
            """, Web);

        var body = Json(await BuildSut().PublishListing(request, default));

        var draft = (PropertyDraft)_catalog.ReceivedCalls()
            .Single(c => c.GetMethodInfo().Name == nameof(IPropertyCatalogProvider.PublishListingAsync))
            .GetArguments()[0]!;
        Assert.Equal(4.6736, draft.Geo.Lat);
        Assert.Equal(-74.0556, draft.Geo.Lng);
        Assert.Equal(98, draft.Area);
        // La calle que escribió el agente, que el record no declaraba: se descartaba en el
        // binding y los dos catálogos rellenaban con «{barrio}, {ciudad}». La ficha quedaba
        // diciendo "Chicó, Bogotá" — plausible, y a tres cuadras de la puerta (#110).
        Assert.Equal("Cra 11 #93-45", draft.Address);
        // Y en el vocabulario del dominio: guardado como "sale" ningún filtro lo encuentra.
        Assert.Equal("venta", draft.Operation);

        Assert.Equal("L-1", body.GetProperty("id").GetString());
        Assert.Equal("active", body.GetProperty("status").GetString());
    }

    // ── Consola del agente ────────────────────────────────────────────────────────

    [Fact] // happy: el tablero lee new|contacted|visit; el borde emitía "Contactado"
    public async Task AgentLeads_EmiteElVocabularioDelTablero_YElTituloDelInmueble()
    {
        _leads.GetForAgentAsync("agente@inmo.co", Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                new AgentLead("lead-1", "agente@inmo.co", "L-1", "Ana Ruiz", "ana@correo.co",
                    "3001234567", "Me interesa", LeadStatus.Contactado,
                    new DateTimeOffset(2026, 6, 20, 0, 0, 0, TimeSpan.Zero)),
            });
        _catalog.GetListingAsync("L-1", Arg.Any<CancellationToken>())
            .Returns(new PropertyDetail(
                Listing(), "desc", Array.Empty<PropertySpec>(), Array.Empty<string>(),
                Array.Empty<string>(), new PropertyLocation(0, 0, "", "Chicó", "Bogotá"), "Agente", ""));

        var lead = Json(await BuildSut().AgentLeads(default)).GetProperty("leads")[0];

        Assert.Equal("contacted", lead.GetProperty("status").GetString());
        Assert.Equal("Apartamento en Chicó", lead.GetProperty("listingTitle").GetString());
    }

    // ── Cuenta: búsquedas guardadas ───────────────────────────────────────────────

    [Fact] // happy: sin `createdAt` la tarjeta rellenaba con HOY — toda búsqueda "de hoy"
    public async Task Saved_EmiteCreatedAt()
    {
        _collections.GetAsync("agente@inmo.co", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<UserCollectionItem>());
        _savedSearches.GetForOwnerAsync("agente@inmo.co", Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                new SavedSearch("bus-1", "agente@inmo.co", "Apartamentos en Chicó",
                    new PropertyQuery(Text: "Chicó", Operation: "venta"),
                    new DateTimeOffset(2026, 6, 20, 0, 0, 0, TimeSpan.Zero)),
            });

        var busqueda = Json(await BuildSut().Saved(default)).GetProperty("searches")[0];

        Assert.Equal("2026-06-20", busqueda.GetProperty("createdAt").GetString());
        Assert.Equal("sale", busqueda.GetProperty("operation").GetString());
    }

    [Fact] // happy: el texto viaja como `q` y el borde solo leía `text` → se guardaba vacía
    public async Task SaveSearch_ConservaElTextoQueLaUiManda()
    {
        _savedSearches.SaveAsync(Arg.Any<string>(), Arg.Any<PropertyQuery>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(ci => new SavedSearch("bus-1", "agente@inmo.co", "Apartamentos en Chicó",
                ci.ArgAt<PropertyQuery>(1), DateTimeOffset.UnixEpoch));

        // El cuerpo tal cual lo serializa `saveSearch()` (SavedSearchRequest del modelo).
        var request = JsonSerializer.Deserialize<RealtyController.SaveSearchRequest>(
            """
            {
              "label": "Apartamentos en Chicó",
              "operation": "sale",
              "alert": true,
              "criteria": {
                "q": "Chicó",
                "operation": "sale",
                "type": "apartamento",
                "minPrice": 0,
                "maxPrice": 0,
                "beds": 3,
                "location": "Bogotá",
                "sort": "relevance"
              }
            }
            """, Web);

        await BuildSut().SaveSearch(request, default);

        var query = (PropertyQuery)_savedSearches.ReceivedCalls()
            .Single(c => c.GetMethodInfo().Name == nameof(ISavedSearchService.SaveAsync))
            .GetArguments()[1]!;
        Assert.Equal("Chicó", query.Text);
        Assert.Equal("venta", query.Operation);
    }
}
