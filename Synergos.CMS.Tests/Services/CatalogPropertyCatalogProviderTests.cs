using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Cubre el portal inmobiliario servido desde el CONTENIDO del editor (ADR 0118), que es lo
/// que se registra cuando <c>Synergos:Catalog:Sources:Realty = cms</c>.
/// </summary>
/// <remarks>
/// <para>Dos propiedades, y las dos son de regresión silenciosa. La primera: el <b>buscador se
/// comporta igual</b> venga el inventario de donde venga — precio como rango continuo,
/// ubicación por substring sobre ciudad O barrio, habitaciones como umbral. Si eso cambiara al
/// mover el flag, en un portal inmobiliario habría cambiado el producto.</para>
///
/// <para>La segunda: el <b>orden entre las dos capas</b>. Debajo el contenido; encima, lo que
/// el agente publicó desde la consola. Si la de abajo ganara, el agente publicaría y no vería
/// su cambio.</para>
/// </remarks>
public sealed class CatalogPropertyCatalogProviderTests
{
    private const string Cop = "COP";

    private sealed class FakeSource : ICatalogSource<PropertyDetail>
    {
        private readonly IReadOnlyList<PropertyDetail> _items;

        public FakeSource(params PropertyDetail[] items) => _items = items;

        public Task<IReadOnlyList<PropertyDetail>> GetAllAsync(
            string? scope = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(_items);
    }

    private static PropertyDetail Listing(
        string slug,
        string title = "Apartamento",
        string city = "Bogotá",
        string neighborhood = "Chicó Norte",
        string type = "apartamento",
        string operation = "venta",
        decimal price = 850_000_000m,
        int beds = 3,
        bool featured = false)
        => new(
            Summary: new PropertyListing(
                Id: slug, Slug: slug, Title: title, Operation: operation, Type: type,
                Price: price, Currency: Cop, City: city, Neighborhood: neighborhood,
                Beds: beds, Baths: 2, AreaM2: 92, Stratum: 5,
                Lat: 4.6736, Lng: -74.0556, ImageUrl: string.Empty, Featured: featured),
            Description: "Descripción",
            Specs: Array.Empty<PropertySpec>(),
            Amenities: Array.Empty<string>(),
            Gallery: Array.Empty<string>(),
            Location: new PropertyLocation(4.6736, -74.0556, "Chicó Norte, Bogotá", neighborhood, city),
            AgentName: "Ana Gómez",
            AgentPhone: "+57 310 000 0000");

    private static CatalogPropertyCatalogProvider Build(IJsonEntityStore store, params PropertyDetail[] content)
        => new(new FakeSource(content), store);

    private static PropertyDraft Draft(
        string title = "Casa en Laureles",
        decimal price = 620_000_000m,
        PropertyGeo? geo = null)
        => new(
            Title: title, Type: "casa", Operation: "venta", Price: price,
            Beds: 4, Baths: 3, Area: 180, City: "Medellín",
            Geo: geo ?? new PropertyGeo(6.2447, -75.5916),
            Gallery: Array.Empty<string>(), Description: "Casa amplia", Neighborhood: "Laureles");

    // ── Búsqueda ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Un_portal_vacio_devuelve_listas_vacias_y_no_revienta()
    {
        var result = await Build(new InMemoryJsonEntityStore()).SearchAsync(new PropertyQuery());

        Assert.Empty(result.Listings);
        Assert.NotNull(result.Facets);
    }

    [Fact]
    public async Task Una_query_nula_se_trata_como_sin_filtros()
    {
        var sut = Build(new InMemoryJsonEntityStore(), Listing("a"), Listing("b"));

        Assert.Equal(2, (await sut.SearchAsync(null!)).Listings.Count);
    }

    [Fact]
    public async Task Los_destacados_van_primero_y_luego_por_precio()
    {
        var sut = Build(
            new InMemoryJsonEntityStore(),
            Listing("caro", price: 900_000_000m),
            Listing("barato", price: 300_000_000m),
            Listing("destacado", price: 800_000_000m, featured: true));

        var order = (await sut.SearchAsync(new PropertyQuery())).Listings.Select(l => l.Slug);

        Assert.Equal(new[] { "destacado", "barato", "caro" }, order);
    }

    [Fact]
    public async Task El_rango_de_precio_es_continuo_y_acota_el_universo()
    {
        var sut = Build(
            new InMemoryJsonEntityStore(),
            Listing("barato", price: 300_000_000m),
            Listing("medio", price: 600_000_000m),
            Listing("caro", price: 900_000_000m));

        var result = await sut.SearchAsync(new PropertyQuery(MinPrice: 400_000_000m, MaxPrice: 700_000_000m));

        Assert.Equal("medio", Assert.Single(result.Listings).Slug);
    }

    [Fact]
    public async Task La_ubicacion_busca_por_SUBSTRING_sobre_ciudad_o_barrio()
    {
        // "laureles" es un barrio y "medell" es media ciudad: una faceta por igualdad exacta
        // devolvería cero en los dos casos.
        var sut = Build(
            new InMemoryJsonEntityStore(),
            Listing("bogota", city: "Bogotá", neighborhood: "Chicó Norte"),
            Listing("medellin", city: "Medellín", neighborhood: "Laureles"));

        Assert.Equal("medellin", Assert.Single((await sut.SearchAsync(new PropertyQuery(Location: "laureles"))).Listings).Slug);
        Assert.Equal("medellin", Assert.Single((await sut.SearchAsync(new PropertyQuery(Location: "medell"))).Listings).Slug);
    }

    [Fact]
    public async Task Las_habitaciones_filtran_como_UMBRAL_no_como_valor_exacto()
    {
        // Quien busca casa pide "3 habitaciones o más".
        var sut = Build(
            new InMemoryJsonEntityStore(),
            Listing("dos", beds: 2),
            Listing("tres", beds: 3),
            Listing("cuatro", beds: 4));

        var result = await sut.SearchAsync(new PropertyQuery(Beds: 3));

        Assert.Equal(new[] { "cuatro", "tres" }, result.Listings.Select(l => l.Slug).OrderBy(s => s, StringComparer.Ordinal));
    }

    [Fact]
    public async Task Las_facetas_cuentan_DENTRO_del_universo_ya_acotado_por_precio()
    {
        // El rango reduce el universo, y los chips tienen que contar ahí — no sobre el total.
        var sut = Build(
            new InMemoryJsonEntityStore(),
            Listing("casa-cara", type: "casa", price: 900_000_000m),
            Listing("apto-barato", type: "apartamento", price: 300_000_000m));

        var result = await sut.SearchAsync(new PropertyQuery(MaxPrice: 500_000_000m));

        var tipo = result.Facets.Single(f => string.Equals(f.Name, "type", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("apartamento", Assert.Single(tipo.Values).Value);
    }

    [Fact]
    public async Task La_faceta_de_habitaciones_viaja_con_su_Kind_y_su_Label()
    {
        // Sin el Kind la UI pintaba checkboxes, y marcar "4+" y "1+" a la vez devolvía TODO.
        // Sin el Label el chip decía "3" mientras significaba "3 o más".
        var sut = Build(new InMemoryJsonEntityStore(), Listing("a", beds: 3));

        var beds = (await sut.SearchAsync(new PropertyQuery())).Facets
            .Single(f => string.Equals(f.Name, "beds", StringComparison.OrdinalIgnoreCase));

        Assert.Equal("Threshold", beds.Kind);
        Assert.All(beds.Values, v => Assert.False(string.IsNullOrWhiteSpace(v.Label)));
    }

    // ── Ficha ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task La_ficha_se_resuelve_por_id_y_por_slug_y_null_si_no_existe()
    {
        var sut = Build(new InMemoryJsonEntityStore(), Listing("apto-chico-norte"));

        Assert.NotNull(await sut.GetListingAsync("apto-chico-norte"));
        Assert.NotNull(await sut.GetListingAsync("APTO-CHICO-NORTE"));
        Assert.Null(await sut.GetListingAsync("no-existe"));
        Assert.Null(await sut.GetListingAsync("   "));
    }

    // ── Publicación del agente ───────────────────────────────────────────────

    [Fact]
    public async Task Lo_que_publica_el_agente_se_ve_en_la_MISMA_pantalla()
    {
        var sut = Build(new InMemoryJsonEntityStore());

        await sut.PublishListingAsync(Draft());

        Assert.Single((await sut.SearchAsync(new PropertyQuery())).Listings);
    }

    [Fact]
    public async Task Lo_publicado_SOBREVIVE_a_un_provider_nuevo_sobre_el_mismo_store()
    {
        var store = new InMemoryJsonEntityStore();

        var published = await Build(store).PublishListingAsync(Draft());

        Assert.NotNull(await Build(store).GetListingAsync(published.Summary.Id));
    }

    [Fact]
    public async Task Republicar_el_mismo_inmueble_lo_reemplaza_en_vez_de_duplicarlo()
    {
        var store = new InMemoryJsonEntityStore();
        var sut = Build(store);

        await sut.PublishListingAsync(Draft(price: 600_000_000m));
        await sut.PublishListingAsync(Draft(price: 650_000_000m));

        var result = Assert.Single((await sut.SearchAsync(new PropertyQuery())).Listings);
        Assert.Equal(650_000_000m, result.Price);
    }

    [Fact]
    public async Task Lo_publicado_por_el_agente_TAPA_al_contenido_con_el_mismo_slug()
    {
        var store = new InMemoryJsonEntityStore();
        var sut = Build(store, Listing("casa-en-laureles", title: "Como lo autoró el editor"));

        await sut.PublishListingAsync(Draft(title: "Casa en Laureles"));

        var result = Assert.Single((await sut.SearchAsync(new PropertyQuery())).Listings);
        Assert.Equal("Casa en Laureles", result.Title);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Publicar_sin_titulo_LANZA_porque_el_llamador_es_un_formulario(string title)
    {
        var sut = Build(new InMemoryJsonEntityStore());

        await Assert.ThrowsAsync<ArgumentException>(() => sut.PublishListingAsync(Draft(title: title)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Publicar_con_un_precio_que_no_es_precio_LANZA(decimal price)
    {
        // La misma regla que la proyección de contenido aplica omitiendo: una sola, a los dos
        // lados. Aquí lanza porque un 400 con la razón se pinta en el campo que está mal.
        var sut = Build(new InMemoryJsonEntityStore());

        await Assert.ThrowsAsync<ArgumentException>(() => sut.PublishListingAsync(Draft(price: price)));
    }

    [Fact]
    public async Task Publicar_null_es_un_error_de_programacion_y_lanza()
    {
        var sut = Build(new InMemoryJsonEntityStore());

        await Assert.ThrowsAsync<ArgumentNullException>(() => sut.PublishListingAsync(null!));
    }

    [Fact]
    public async Task Un_titulo_sin_nada_utilizable_recibe_un_id_acunado_y_estable()
    {
        var sut = Build(new InMemoryJsonEntityStore());

        var first = await sut.PublishListingAsync(Draft(title: "###"));
        var second = await sut.PublishListingAsync(Draft(title: "###"));

        // No sale de un contador del proceso: este catálogo es durable, y tras un reinicio el
        // contador volvería a 1 y el segundo inmueble pisaría al primero.
        Assert.NotEqual(first.Summary.Id, second.Summary.Id);
        Assert.NotNull(await sut.GetListingAsync(first.Summary.Id));
    }

    [Fact]
    public async Task Un_titulo_con_separadores_de_ruta_no_escribe_fuera_de_su_recurso()
    {
        var sut = Build(new InMemoryJsonEntityStore());

        var published = await sut.PublishListingAsync(Draft(title: "../../etc/passwd"));

        Assert.DoesNotContain('/', published.Summary.Id);
        Assert.DoesNotContain('.', published.Summary.Id);
    }

    // ── Degradación ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Una_entrada_corrupta_en_el_store_se_salta_sin_tumbar_el_portal()
    {
        var store = new InMemoryJsonEntityStore();
        await store.WriteAsync(CatalogPropertyCatalogProvider.ResourceType, "roto", "{ esto no es json ");

        var sut = Build(store, Listing("apto-chico-norte"));

        Assert.Equal("apto-chico-norte", Assert.Single((await sut.SearchAsync(new PropertyQuery())).Listings).Slug);
    }

    [Fact]
    public async Task Un_inmueble_publicado_conserva_su_ubicacion_al_releerse()
    {
        // La ida y vuelta por JSON separa "se guardó" de "se guardó completo": una ficha que
        // vuelve sin coordenadas desaparece del mapa.
        var store = new InMemoryJsonEntityStore();

        var published = await Build(store).PublishListingAsync(Draft(geo: new PropertyGeo(6.2447, -75.5916)));
        var reread = await Build(store).GetListingAsync(published.Summary.Id);

        Assert.Equal(6.2447, reread!.Summary.Lat);
        Assert.Equal(-75.5916, reread.Location.Lng);
        Assert.Equal("Laureles, Medellín", reread.Location.Address);
    }
}
