using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Dos instancias de un stub de catálogo NO comparten lo publicado (auditoría, backlog #8 —
/// el test intermitente).
/// </summary>
/// <remarks>
/// <para><b>El síntoma.</b> <c>CatalogEngineReplicationTests.Realty_SinTildes_Encuentra</c>
/// fallaba aproximadamente 1 de cada 16 corridas completas de la suite, y era irreproducible:
/// 15 intentos seguidos en verde, y 0 de 8 aislando las clases sospechosas.</para>
///
/// <para><b>La causa.</b> <c>StubPropertyCatalogProvider</c> guardaba su catálogo en un
/// <c>static</c> de proceso, así que <c>new</c> por test no aislaba nada.
/// <c>StubPropertyCatalogProviderTests.Publish_Happy_AppearsInSearch</c> publica ahí un
/// inmueble en <b>Medellín</b>, y xUnit corre las clases en paralelo. El test que fallaba hace
/// DOS búsquedas de "Medellín"/"medellin" y compara las secuencias de ids: si el publish
/// aterrizaba justo ENTRE ellas, la segunda traía un id de más.</para>
///
/// <para><b>Por qué era tan esquivo.</b> El estático no se limpia nunca. Una vez publicado, el
/// listado extra sale en las DOS búsquedas y la igualdad vuelve a cumplirse — la ventana de
/// fallo es solo el hueco entre ambas llamadas. Eso explica la rareza observada, y es lo que
/// convirtió una hipótesis en diagnóstico: se reprodujo de forma determinista intercalando un
/// publish entre las dos búsquedas, y el test falló.</para>
///
/// <para><b>El arreglo.</b> El catálogo pasa a estado de INSTANCIA. En producción no cambia
/// nada —los tres stubs se registran con <c>AddSingleton</c>, así que hay exactamente uno—,
/// pero en tests cada <c>new</c> arranca limpio. Estos tests son los que impiden que el
/// <c>static</c> vuelva: si regresa, comparten estado y esto se pone rojo.</para>
/// </remarks>
public sealed class CatalogStubIsolationTests
{
    private static PropertyDraft PropertyInMedellin() => new(
        Title: "Apartamento de prueba de aislamiento", Type: "apartamento", Operation: "venta",
        Price: 450_000_000m, Beds: 2, Baths: 2, Area: 78, City: "Medellín",
        Geo: new PropertyGeo(6.1710, -75.5910),
        Gallery: new[] { "/media/realty/aislamiento.jpg" },
        Description: "Publicado por un test para comprobar que NO se filtra a otras instancias.");

    [Fact]
    public async Task Lo_publicado_en_un_provider_de_inmuebles_no_lo_ve_OTRO()
    {
        var publicador = new StubPropertyCatalogProvider();
        var observador = new StubPropertyCatalogProvider();

        var publicado = await publicador.PublishListingAsync(PropertyInMedellin());
        var visto = await observador.SearchAsync(new PropertyQuery(Text: "Medellín"));

        Assert.DoesNotContain(visto.Listings, l => l.Id == publicado.Summary.Id);
    }

    [Fact]
    public async Task Dos_busquedas_en_instancias_distintas_dan_el_MISMO_resultado()
    {
        // La forma exacta del test que fallaba: dos búsquedas equivalentes sobre instancias
        // distintas, con un publish de por medio. Con el catálogo estático, la segunda traía
        // un id de más y la igualdad se rompía.
        var antes = await new StubPropertyCatalogProvider().SearchAsync(new PropertyQuery(Text: "Medellín"));

        await new StubPropertyCatalogProvider().PublishListingAsync(PropertyInMedellin());

        var despues = await new StubPropertyCatalogProvider().SearchAsync(new PropertyQuery(Text: "medellin"));

        Assert.Equal(antes.Listings.Select(l => l.Id), despues.Listings.Select(l => l.Id));
    }

    [Fact]
    public async Task Un_provider_SI_ve_lo_que_el_mismo_publico()
    {
        // El contraveneno del test de arriba: aislar entre instancias no puede costar la
        // semántica de "leo lo que acabo de escribir" DENTRO de una. En producción hay un
        // único singleton, así que este es el caso que de verdad importa.
        var svc = new StubPropertyCatalogProvider();

        var publicado = await svc.PublishListingAsync(PropertyInMedellin());
        var visto = await svc.SearchAsync(new PropertyQuery(Text: "Medellín"));

        Assert.Contains(visto.Listings, l => l.Id == publicado.Summary.Id);
    }

    [Fact]
    public async Task Lo_publicado_en_un_provider_de_eventos_no_lo_ve_OTRO()
    {
        // Mismo patrón, mismo riesgo latente: StubEventCatalogProvider tenía el catálogo
        // estático igual, y CatalogEngineReplicationTests también publica eventos.
        var titulo = "Evento de prueba de aislamiento";
        var publicador = new StubEventCatalogProvider();
        var observador = new StubEventCatalogProvider();

        await publicador.PublishEventAsync(new EventDetail(
            Summary: new EventSummary(
                string.Empty, "slug-aislamiento", titulo, "Música", "Bogotá", "Teatro X",
                DateTimeOffset.UtcNow.AddDays(10), string.Empty, 90_000m, "COP", "general", null),
            Description: string.Empty, Organizer: "Org",
            Tiers: new[] { new EventTier("GEN", "General", 90_000m, "COP", 50, 50, 4) },
            SeatMap: null));

        Assert.Empty(await observador.SearchAsync(titulo));
    }

    [Fact]
    public async Task Los_contadores_de_id_tampoco_se_comparten()
    {
        // El contador también era estático. Compartido, el id de un publish dependía de
        // cuántas veces hubiera publicado OTRA clase de test antes — y cualquier aserción
        // sobre el id se volvía dependiente del orden de ejecución.
        var primero = await new StubPropertyCatalogProvider().PublishListingAsync(PropertyInMedellin());
        var segundo = await new StubPropertyCatalogProvider().PublishListingAsync(PropertyInMedellin());

        Assert.Equal(primero.Summary.Id, segundo.Summary.Id);
    }
}
