using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Cubre el catálogo de Eventos servido desde el CONTENIDO del editor (ADR 0117), que es lo
/// que se registra cuando <c>Synergos:Catalog:Sources:Events = cms</c>.
/// </summary>
/// <remarks>
/// Lo que se verifica no es "lee una lista": es el ORDEN entre las dos capas. Debajo está el
/// contenido; encima, lo que un organizador publicó desde la app. Si la de abajo ganara, el
/// organizador publicaría y no vería su cambio; si no se descartara por slug, la agenda
/// mostraría el mismo evento dos veces.
/// </remarks>
public sealed class CatalogEventCatalogProviderTests
{
    private const string Cop = "COP";

    /// <summary>Fuente de contenido de mentira: la lista que le pasemos, sin más.</summary>
    private sealed class FakeSource : ICatalogSource<EventDetail>
    {
        private readonly IReadOnlyList<EventDetail> _items;

        public FakeSource(params EventDetail[] items) => _items = items;

        public int Calls { get; private set; }

        public Task<IReadOnlyList<EventDetail>> GetAllAsync(
            string? scope = null,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(_items);
        }
    }

    private static EventDetail Event(string slug, string title = "Evento", string city = "Bogotá")
        => new(
            Summary: new EventSummary(
                Id: slug,
                Slug: slug,
                Title: title,
                Category: "Música",
                City: city,
                Venue: "Parque Simón Bolívar",
                StartUtc: new DateTimeOffset(2026, 7, 12, 20, 0, 0, TimeSpan.FromHours(-5)),
                ImageUrl: string.Empty,
                PriceFrom: 180_000m,
                Currency: Cop,
                Mode: "general"),
            Description: "Descripción",
            Organizer: "Synergos Live",
            Tiers: new[] { new EventTier("general", "General", 180_000m, Cop, 500, 500, 10) },
            SeatMap: null);

    private static CatalogEventCatalogProvider Build(IJsonEntityStore store, params EventDetail[] content)
        => new(new FakeSource(content), store);

    // ── Búsqueda ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Un_catalogo_vacio_devuelve_lista_vacia_y_no_revienta()
    {
        var sut = Build(new InMemoryJsonEntityStore());

        Assert.Empty(await sut.SearchAsync(null));
    }

    [Fact]
    public async Task Sirve_los_eventos_del_contenido_ordenados_por_fecha()
    {
        var store = new InMemoryJsonEntityStore();
        var tarde = Event("cierre") with
        {
            Summary = Event("cierre").Summary with
            {
                StartUtc = new DateTimeOffset(2026, 12, 1, 20, 0, 0, TimeSpan.FromHours(-5)),
            },
        };
        var sut = Build(store, tarde, Event("apertura"));

        var results = await sut.SearchAsync(null);

        Assert.Equal(new[] { "apertura", "cierre" }, results.Select(e => e.Slug));
    }

    [Fact]
    public async Task El_filtro_de_texto_es_el_MISMO_motor_que_usa_el_stub()
    {
        // La búsqueda no puede comportarse distinto según de dónde salgan los eventos: sería
        // una regresión invisible que solo aparece al mover el flag a 'cms'.
        var sut = Build(
            new InMemoryJsonEntityStore(),
            Event("festival-bogota", "Festival Estéreo", "Bogotá"),
            Event("teatro-medellin", "Noche de teatro", "Medellín"));

        var results = await sut.SearchAsync("medellín");

        Assert.Equal("teatro-medellin", Assert.Single(results).Slug);
    }

    // ── Ficha ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task La_ficha_se_resuelve_por_id_y_por_slug()
    {
        var sut = Build(new InMemoryJsonEntityStore(), Event("festival-estereo"));

        Assert.NotNull(await sut.GetEventAsync("festival-estereo"));
        Assert.NotNull(await sut.GetEventAsync("FESTIVAL-ESTEREO"));
    }

    [Fact]
    public async Task Una_ficha_que_no_existe_devuelve_null_en_vez_de_lanzar()
    {
        var sut = Build(new InMemoryJsonEntityStore(), Event("festival-estereo"));

        Assert.Null(await sut.GetEventAsync("no-existe"));
        Assert.Null(await sut.GetEventAsync("   "));
        Assert.Null(await sut.GetEventAsync(string.Empty));
    }

    // ── Publicación del organizador ──────────────────────────────────────────

    [Fact]
    public async Task Lo_que_publica_el_organizador_se_ve_en_la_MISMA_pantalla()
    {
        var sut = Build(new InMemoryJsonEntityStore());

        await sut.PublishEventAsync(Event("concierto-nuevo", "Concierto nuevo"));

        Assert.Equal("concierto-nuevo", Assert.Single(await sut.SearchAsync(null)).Slug);
    }

    [Fact]
    public async Task Lo_publicado_SOBREVIVE_a_un_provider_nuevo_sobre_el_mismo_store()
    {
        // El proxy de reinicio del repo: mismo store, servicio nuevo.
        var store = new InMemoryJsonEntityStore();

        await Build(store).PublishEventAsync(Event("concierto-nuevo"));
        var after = Build(store);

        Assert.NotNull(await after.GetEventAsync("concierto-nuevo"));
    }

    [Fact]
    public async Task Republicar_el_mismo_evento_lo_reemplaza_en_vez_de_duplicarlo()
    {
        var store = new InMemoryJsonEntityStore();
        var sut = Build(store);

        await sut.PublishEventAsync(Event("concierto", "Título viejo"));
        await sut.PublishEventAsync(Event("concierto", "Título nuevo"));

        var result = Assert.Single(await sut.SearchAsync(null));
        Assert.Equal("Título nuevo", result.Title);
    }

    [Fact]
    public async Task Lo_publicado_por_el_organizador_TAPA_al_contenido_con_el_mismo_slug()
    {
        // Publicar y no ver el cambio es peor que la ambigüedad.
        var store = new InMemoryJsonEntityStore();
        var sut = Build(store, Event("festival-estereo", "Como lo autoró el editor"));

        await sut.PublishEventAsync(Event("festival-estereo", "Como lo publicó el organizador"));

        var result = Assert.Single(await sut.SearchAsync(null));
        Assert.Equal("Como lo publicó el organizador", result.Title);

        var detail = await sut.GetEventAsync("festival-estereo");
        Assert.Equal("Como lo publicó el organizador", detail!.Summary.Title);
    }

    [Fact]
    public async Task Un_borrador_sin_id_recibe_el_slug_como_id()
    {
        // La fuente de contenido emite Id = slug. Acuñar un id nuevo dejaría dos fichas casi
        // iguales en la agenda en cuanto el editor autorara ese mismo evento.
        var sut = Build(new InMemoryJsonEntityStore());
        var draft = Event("concierto-nuevo") with
        {
            Summary = Event("concierto-nuevo").Summary with { Id = string.Empty },
        };

        var published = await sut.PublishEventAsync(draft);

        Assert.Equal("concierto-nuevo", published.Summary.Id);
    }

    [Fact]
    public async Task Un_borrador_sin_id_ni_slug_recibe_uno_acunado_y_estable()
    {
        var sut = Build(new InMemoryJsonEntityStore());
        var draft = Event("x") with
        {
            Summary = Event("x").Summary with { Id = string.Empty, Slug = string.Empty },
        };

        var published = await sut.PublishEventAsync(draft);

        Assert.False(string.IsNullOrWhiteSpace(published.Summary.Id));
        Assert.NotNull(await sut.GetEventAsync(published.Summary.Id));
    }

    [Fact]
    public async Task Dos_publicaciones_sin_slug_NO_se_pisan()
    {
        // El id acuñado no puede salir de un contador del proceso: este catálogo es durable y
        // tras un reinicio el contador volvería a 1, pisando lo ya publicado.
        var sut = Build(new InMemoryJsonEntityStore());
        var draft = Event("x") with
        {
            Summary = Event("x").Summary with { Id = string.Empty, Slug = string.Empty },
        };

        var first = await sut.PublishEventAsync(draft);
        var second = await sut.PublishEventAsync(draft);

        Assert.NotEqual(first.Summary.Id, second.Summary.Id);
        Assert.Equal(2, (await sut.SearchAsync(null)).Count);
    }

    [Fact]
    public async Task Un_id_con_separadores_de_ruta_no_escribe_fuera_de_su_recurso()
    {
        // La clave termina siendo un nombre de archivo en el adapter FileSystem. Un id con
        // "../" no es un id feo: es una escritura fuera del directorio del recurso.
        var sut = Build(new InMemoryJsonEntityStore());
        var draft = Event("x") with
        {
            Summary = Event("x").Summary with { Id = "../../etc/passwd", Slug = string.Empty },
        };

        var published = await sut.PublishEventAsync(draft);

        Assert.DoesNotContain('/', published.Summary.Id);
        Assert.DoesNotContain('.', published.Summary.Id);
    }

    [Fact]
    public async Task Publicar_null_es_un_error_de_programacion_y_lanza()
    {
        var sut = Build(new InMemoryJsonEntityStore());

        await Assert.ThrowsAsync<ArgumentNullException>(() => sut.PublishEventAsync(null!));
    }

    // ── Degradación ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Una_entrada_corrupta_en_el_store_se_salta_sin_tumbar_la_agenda()
    {
        var store = new InMemoryJsonEntityStore();
        await store.WriteAsync(CatalogEventCatalogProvider.ResourceType, "roto", "{ esto no es json ");

        var sut = Build(store, Event("festival-estereo"));

        Assert.Equal("festival-estereo", Assert.Single(await sut.SearchAsync(null)).Slug);
    }

    [Fact]
    public async Task Un_evento_publicado_conserva_sus_localidades_al_releerse()
    {
        // La ida y vuelta por JSON es lo que separa "se guardó" de "se guardó completo": una
        // ficha que vuelve sin localidades no se puede comprar.
        var store = new InMemoryJsonEntityStore();
        var draft = Event("concierto") with
        {
            Tiers = new[]
            {
                new EventTier("vip", "Platea VIP", 380_000m, Cop, 120, 120, 4,
                    ZoneId: "platea", Description: "Acceso preferencial",
                    Perks: new[] { "Ingreso prioritario" }, SaleWindow: "Hasta el 12 de julio",
                    Featured: true),
            },
        };

        await Build(store).PublishEventAsync(draft);
        var reread = await Build(store).GetEventAsync("concierto");

        var tier = Assert.Single(reread!.Tiers);
        Assert.Equal("vip", tier.Code);
        Assert.Equal(380_000m, tier.Price);
        Assert.Equal(4, tier.MaxPerOrder);
        Assert.True(tier.Featured);
        Assert.Equal(new[] { "Ingreso prioritario" }, tier.Perks);
    }

    [Fact]
    public async Task Un_mapa_de_asientos_publicado_conserva_sus_butacas()
    {
        var store = new InMemoryJsonEntityStore();
        var draft = Event("teatro") with
        {
            SeatMap = new EventSeatMap(
                "Teatro Metropolitano",
                new[]
                {
                    new EventZone("platea", "Platea", 380_000m, Cop, "general", new[]
                    {
                        new EventRow("A", new[]
                        {
                            new EventSeat("platea-A1", "A1", "free"),
                            new EventSeat("platea-A2", "A2", "free"),
                        }),
                    }),
                }),
        };

        await Build(store).PublishEventAsync(draft);
        var reread = await Build(store).GetEventAsync("teatro");

        var zone = Assert.Single(reread!.SeatMap!.Zones);
        Assert.Equal("Teatro Metropolitano", reread.SeatMap.VenueName);
        Assert.Equal(new[] { "platea-A1", "platea-A2" }, Assert.Single(zone.Rows).Seats.Select(s => s.Id));
    }

    [Fact]
    public async Task Sin_cache_la_fuente_se_relee_en_cada_busqueda()
    {
        // De ahí sale gratis el read-your-writes que este vertical necesita. Una caché aquí
        // haría que el organizador publicara y no lo viera hasta el siguiente reinicio.
        var source = new FakeSource(Event("festival-estereo"));
        var sut = new CatalogEventCatalogProvider(source, new InMemoryJsonEntityStore());

        await sut.SearchAsync(null);
        await sut.SearchAsync(null);

        Assert.Equal(2, source.Calls);
    }
}
