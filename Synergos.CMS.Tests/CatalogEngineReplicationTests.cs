using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using NSubstitute;
using Xunit;

namespace Synergos.CMS.Tests;

/// <summary>
/// Los 4 verticales que replicaron el motor transversal (T5 Ola 0, PASO 8). Cubre lo que el
/// refactor CAMBIÓ y lo que debía PRESERVAR — no el motor en sí, que ya tiene sus 71 tests.
/// </summary>
public class CatalogEngineReplicationTests
{
    // ── PROPIEDADES ────────────────────────────────────────────────────────────────

    private static IPropertyCatalogProvider Realty() => new StubPropertyCatalogProvider();

    [Fact] // El chip MENTÍA: decía "3 (2)" y al pulsarlo devolvía los de 3 Y los de 4.
    public async Task Realty_ChipDeHabitaciones_PrometeLoQueElFiltroEntrega()
    {
        var svc = Realty();

        var sinFiltro = await svc.SearchAsync(new PropertyQuery());
        var beds = sinFiltro.Facets.Single(f => f.Name == "beds");

        // Los chips son umbrales declarados ("3+"), no conteos exactos, porque el filtro
        // SIEMPRE fue `l.Beds >= query.Beds` — ahora el chip dice la verdad.
        var chip3 = beds.Values.SingleOrDefault(v => v.Value == "3");
        Assert.NotNull(chip3);

        var alPulsar = await svc.SearchAsync(new PropertyQuery(Beds: 3));
        Assert.Equal(chip3!.Count, alPulsar.Listings.Count);
        Assert.All(alPulsar.Listings, l => Assert.True(l.Beds >= 3));
    }

    [Fact] // Sin el LABEL, el conteo deja de mentir y la etiqueta empieza: "1" con 6 inmuebles
           // cuando el catálogo no tiene NINGUNO de 1 habitación. El umbral hay que LEERLO.
    public async Task Realty_ChipDeHabitaciones_SeLeeComoUmbral_NoComoNumeroPelado()
    {
        var beds = (await Realty().SearchAsync(new PropertyQuery())).Facets.Single(f => f.Name == "beds");

        Assert.All(beds.Values, v => Assert.False(
            string.IsNullOrWhiteSpace(v.Label),
            $"El chip {v.Value} viaja sin label: se pintaría como un número exacto."));
        Assert.Equal("1+ habitación", beds.Values.Single(v => v.Value == "1").Label);
        Assert.Equal("3+ habitaciones", beds.Values.Single(v => v.Value == "3").Label);

        // Y ascendente, como los lee cualquiera.
        Assert.Equal(new[] { "1", "2", "3", "4" }, beds.Values.Select(v => v.Value));
    }

    [Fact] // El orden histórico: destacados primero, luego precio ascendente.
    public async Task Realty_SinTexto_PreservaElOrdenHistorico()
    {
        var listings = (await Realty().SearchAsync(new PropertyQuery())).Listings;

        var esperado = listings.OrderByDescending(l => l.Featured).ThenBy(l => l.Price).Select(l => l.Id);
        Assert.Equal(esperado, listings.Select(l => l.Id));
    }

    [Fact] // Buscar un BARRIO por ?location= debe seguir funcionando: no es la faceta `city`.
    public async Task Realty_Location_SigueBuscandoPorBarrioYPorSubstring()
    {
        var svc = Realty();
        var todas = (await svc.SearchAsync(new PropertyQuery())).Listings;
        var barrio = todas.Select(l => l.Neighborhood).First(n => !string.IsNullOrWhiteSpace(n));

        var porBarrio = await svc.SearchAsync(new PropertyQuery(Location: barrio));
        Assert.NotEmpty(porBarrio.Listings);

        // Y por substring parcial de la ciudad, como antes ("medell" → Medellín).
        var ciudad = todas.Select(l => l.City).First(c => c.Length > 5);
        var porSubstring = await svc.SearchAsync(new PropertyQuery(Location: ciudad[..5]));
        Assert.NotEmpty(porSubstring.Listings);
    }

    [Fact] // El rango de precio es continuo (tecleado), no chips: se filtra antes del motor.
    public async Task Realty_RangoDePrecio_AcotaYLasFacetasCuentanDentroDelRango()
    {
        var svc = Realty();
        var todas = (await svc.SearchAsync(new PropertyQuery())).Listings;
        var mediana = todas.OrderBy(l => l.Price).ElementAt(todas.Count / 2).Price;

        var baratas = await svc.SearchAsync(new PropertyQuery(MaxPrice: mediana));

        Assert.NotEmpty(baratas.Listings);
        Assert.All(baratas.Listings, l => Assert.True(l.Price <= mediana));
        // Las facetas describen el universo acotado, no el catálogo entero.
        var tipos = baratas.Facets.Single(f => f.Name == "type").Values.Sum(v => v.Count);
        Assert.Equal(baratas.Listings.Count, tipos);
    }

    [Fact] // Sin tildes, a través de la fachada.
    public async Task Realty_SinTildes_Encuentra()
    {
        var conTilde = await Realty().SearchAsync(new PropertyQuery(Text: "Medellín"));
        var sinTilde = await Realty().SearchAsync(new PropertyQuery(Text: "medellin"));

        Assert.NotEmpty(conTilde.Listings);
        Assert.Equal(conTilde.Listings.Select(l => l.Id), sinTilde.Listings.Select(l => l.Id));
    }

    // ── TRÁMITES ───────────────────────────────────────────────────────────────────

    private static ITramiteCatalogProvider Gov() => new StubTramiteCatalogProvider();

    [Fact] // El orden histórico: alfabético por nombre.
    public async Task Gov_SinTexto_PreservaElOrdenAlfabetico()
    {
        var tramites = await Gov().SearchAsync(null, null);

        Assert.Equal(
            tramites.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).Select(t => t.Id),
            tramites.Select(t => t.Id));
    }

    [Fact] // Esta seam promete TODAS las coincidencias: el default Take=24 del motor las truncaría.
    public async Task Gov_SinFiltro_DevuelveElCatalogoEntero_NoUnaPagina()
    {
        var todos = await Gov().SearchAsync(null, null);

        Assert.True(todos.Count > 0);
        // El seed son 7 trámites; si el motor paginara por defecto se verían 24 como mucho,
        // pero el punto es que NINGUNO se pierde.
        var porCategoria = new List<TramiteSummary>();
        foreach (var cat in todos.Select(t => t.Category).Distinct())
        {
            porCategoria.AddRange(await Gov().SearchAsync(null, cat));
        }
        Assert.Equal(todos.Count, porCategoria.Count);
    }

    // ── El truncado silencioso: lo que estas 4 seams NO pueden permitirse ───────────

    [Fact]
    public void SeamsSinPaginacion_NoTienenTopeDePagina()
    {
        // Los 4 verticales cuya seam devuelve la lista COMPLETA usan CatalogSettings.Unpaged.
        // Con el MaxTake por defecto (96), Math.Clamp(int.MaxValue, 1, 96) = 96 y un catálogo
        // de más de 96 ítems perdería el resto EN SILENCIO — y Eventos, Propiedades y
        // Educación tienen Publish*, así que crecen en runtime.
        Assert.Equal(int.MaxValue, CatalogSettings.Unpaged.MaxTake);
        Assert.Equal(96, new CatalogSettings().MaxTake);   // el default sigue protegiendo el wire
    }

    [Fact] // La prueba de verdad: 150 ítems (por encima del tope de 96) y no se pierde ninguno.
    public void MotorSinTope_DevuelveMasDe96Items()
    {
        var muchos = Enumerable.Range(0, 150)
            .Select(n => new TramiteSummary($"t{n:000}", $"slug-{n}", $"Trámite {n}", "x", "Cat", "Entidad", "web", 1, 0m, "COP"))
            .ToList();

        var conTope = new InMemoryCatalogIndex<TramiteSummary>(
            StubTramiteCatalogProvider.Descriptor, new CatalogSettings());
        var sinTope = new InMemoryCatalogIndex<TramiteSummary>(
            StubTramiteCatalogProvider.Descriptor, CatalogSettings.Unpaged);

        var query = new CatalogQuery(Take: int.MaxValue);

        Assert.Equal(96, conTope.Search(muchos, query).Items.Count);    // el truncado que había
        Assert.Equal(150, sinTope.Search(muchos, query).Items.Count);   // lo que la seam promete
    }

    [Fact]
    public async Task Gov_SinTildes_Encuentra()
    {
        var conTilde = await Gov().SearchAsync("Registraduría", null);
        var sinTilde = await Gov().SearchAsync("registraduria", null);

        Assert.NotEmpty(conTilde);
        Assert.Equal(conTilde.Select(t => t.Id), sinTilde.Select(t => t.Id));
    }

    [Fact] // Lo que el motor añade: prefijo. "registra" encuentra "Registraduría".
    public async Task Gov_CasaPorPrefijo()
        => Assert.NotEmpty(await Gov().SearchAsync("registra", null));

    // ── EVENTOS ────────────────────────────────────────────────────────────────────

    private static IEventCatalogProvider Events() => new StubEventCatalogProvider();

    [Fact] // El orden histórico de una agenda: lo próximo primero.
    public async Task Events_SinTexto_PreservaElOrdenPorFecha()
    {
        var events = await Events().SearchAsync(null);

        Assert.Equal(events.OrderBy(e => e.StartUtc).Select(e => e.Id), events.Select(e => e.Id));
    }

    [Fact] // EL riesgo del vertical: el organizador publica y lo ve en la MISMA pantalla.
    public async Task Events_ReadYourWrites_LoPublicadoApareceEnLaSiguienteBusqueda()
    {
        var svc = Events();
        var titulo = "Concierto Sinfónico de Prueba " + Guid.NewGuid().ToString("N")[..8];

        var antes = await svc.SearchAsync(titulo);
        Assert.Empty(antes);

        var publicado = await svc.PublishEventAsync(new EventDetail(
            Summary: new EventSummary(
                string.Empty, "slug-prueba", titulo, "Música", "Bogotá", "Teatro X",
                DateTimeOffset.UtcNow.AddDays(10), "", 90_000m, "COP", "general", null),
            Description: "", Organizer: "Org",
            Tiers: new[] { new EventTier("GEN", "General", 90_000m, "COP", 50, 50, 4) },
            SeatMap: null));

        // Sin caché que invalidar: el motor es puro y el catálogo se lee fresco.
        var despues = await svc.SearchAsync(titulo);
        Assert.Contains(despues, e => e.Id == publicado.Summary.Id);
    }

    [Fact]
    public async Task Events_SinTildes_Encuentra()
    {
        var conTilde = await Events().SearchAsync("Bogotá");
        var sinTilde = await Events().SearchAsync("bogota");

        Assert.NotEmpty(conTilde);
        Assert.Equal(conTilde.Select(e => e.Id), sinTilde.Select(e => e.Id));
    }

    // ── EDUCACIÓN ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Con el <see cref="StubContentStream"/> REAL, no un mock: la siembra de lecciones lee
    /// el item que devuelve <c>CreateAsync</c>, así que un doble sin configurar peta con un
    /// NullReference y estaríamos probando el mock. Mismo montaje que AcademyOla5SmokeTests.
    /// </summary>
    private static StubContentStream ContentStream()
    {
        var clock = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);
        return new StubContentStream(new StubSocialGraphService(), new StubReactionService(), () => clock);
    }

    private static ICourseCatalogProvider Academy() => new StubCourseCatalogProvider(ContentStream());

    [Fact] // El orden histórico: mejor calificados primero.
    public async Task Academy_SinTexto_PreservaElOrdenPorRating()
    {
        var result = await Academy().SearchAsync(new CourseQuery());

        Assert.Equal(
            result.Courses.OrderByDescending(c => c.Rating).Select(c => c.Id),
            result.Courses.Select(c => c.Id));
    }

    [Fact] // Buscar por INSTRUCTOR: su nombre no está en ningún título ni resumen.
    public async Task Academy_BuscaPorNombreDeInstructor()
    {
        var todos = await Academy().SearchAsync(new CourseQuery());
        Assert.NotEmpty(todos.Courses);

        // El instructor entra por un lambda del descriptor; el motor no sabe que existe.
        var porInstructor = await Academy().SearchAsync(new CourseQuery(Text: "Elena"));
        Assert.NotEmpty(porInstructor.Courses);
        Assert.True(porInstructor.Courses.Count < todos.Courses.Count, "El filtro por instructor no acotó nada.");
    }

    [Fact] // Total debe seguir siendo el de las coincidencias, no el de una página.
    public async Task Academy_TotalCoincideConLosCursosDevueltos()
    {
        var result = await Academy().SearchAsync(new CourseQuery());

        Assert.Equal(result.Courses.Count, result.Total);
    }

    [Fact] // La siembra al feed de Blogs NO puede morir en el refactor: es el primer await.
    public async Task Academy_SearchAsync_SigueSembrandoLasLeccionesAlFeed()
    {
        var stream = ContentStream();
        var svc = new StubCourseCatalogProvider(stream);

        await svc.SearchAsync(new CourseQuery());

        // Si EnsureLessonsSeededAsync se hubiera colado dentro del motor (síncrono y puro),
        // el feed quedaría vacío y el course-player mostraría lecciones sin cuerpo.
        var feed = await stream.GetFeedAsync(new FeedQuery(Scope: FeedScope.ForYou, PageSize: 100));
        Assert.Contains(feed.Items, i => string.Equals(i.Kind, "lesson", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Academy_SinTildes_Encuentra()
    {
        // "Sofía" y NO "Diseño": la ñ se PRESERVA a propósito, así que "Diseño" vs "diseño"
        // solo se diferencian en la mayúscula y no probarían el plegado de tildes en
        // absoluto — daban falsa confianza. Sofía es una instructora, así que además ejercita
        // el campo del descriptor que resuelve el JOIN por lambda.
        var conTilde = await Academy().SearchAsync(new CourseQuery(Text: "Sofía"));
        var sinTilde = await Academy().SearchAsync(new CourseQuery(Text: "sofia"));

        Assert.NotEmpty(conTilde.Courses);
        Assert.Equal(conTilde.Courses.Select(c => c.Id), sinTilde.Courses.Select(c => c.Id));
    }
}
