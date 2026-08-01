using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services.Catalog;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Cubre las reglas que convierten un <c>stayListing</c> autorado en la ficha de estadía que
/// sirve la cara viajera de Booking (ADR 0119).
/// </summary>
/// <remarks>
/// <para>El eje es el de Inmobiliaria: <b>omitir una propiedad es perder inventario</b>, así que
/// estas pruebas fijan las dos mitades — qué se descarta (lo que no se puede servir sin mentir)
/// y qué se sirve degradado (todo lo demás).</para>
///
/// <para>Lo propio de este vertical son otras dos cosas. La primera es que <b>la reputación no
/// se inventa</b>: un puntaje sin reseñas detrás no es prueba social, y el promedio no se
/// calcula a partir de los cuatro ejes. La segunda es que <b>el código de tipo de habitación es
/// la llave que ata cada oferta del buscador a su propiedad</b>: si dos hoteles declaran "DLX",
/// las ofertas de uno salen con el nombre y el pin del otro sin que nada falle.</para>
///
/// <para>Y el <b>contrato con el mapa</b>, heredado del ADR 0118: (0, 0) significa "sin pin" y
/// no "frente a África". Se exigen las dos coordenadas y dentro de rango.</para>
/// </remarks>
public sealed class StayContentRulesTests
{
    private static readonly StaySpecLabels SpecLabels = new(
        Rooms: "Habitaciones",
        MaxGuests: "Huéspedes máx. por habitación",
        RoomSize: "Tamaño promedio",
        CheckIn: "Check-in",
        CheckOut: "Check-out");

    private static readonly StayReviewLabels ReviewLabels = new(
        Cleanliness: "Limpieza",
        Location: "Ubicación",
        Service: "Servicio",
        Value: "Relación precio-calidad");

    private static StayListingContent Content(
        string? slug = "ctg-getsemani",
        string? name = "Casa Getsemaní Boutique",
        string? city = "Cartagena",
        string? region = "Bolívar",
        double? lat = 10.4224,
        double? lng = -75.5468)
        => new(slug, name, city, region, Lat: lat, Lng: lng);

    private static StayDetail? Build(StayListingContent content)
        => StayContentRules.BuildStay(content, SpecLabels, ReviewLabels).Value;

    // ── Lo que se descarta ───────────────────────────────────────────────────

    [Fact]
    public void Sin_slug_no_hay_ficha_porque_no_hay_nada_que_resolver()
    {
        var result = StayContentRules.BuildStay(Content(slug: "   "), SpecLabels, ReviewLabels);

        Assert.Null(result.Value);
        Assert.NotEmpty(result.Issues);
    }

    [Fact]
    public void Sin_nombre_se_omite_en_vez_de_caer_al_nombre_del_nodo()
    {
        // "Estadía (1)" no solo sale en la tarjeta: es el Title que hereda cada oferta del
        // buscador de hotel.
        Assert.Null(Build(Content(name: null)));
    }

    [Fact]
    public void Sin_ciudad_se_omite_porque_no_agrupa_ni_se_encuentra()
    {
        Assert.Null(Build(Content(city: "  ")));
    }

    [Fact]
    public void Sin_tarifa_NO_se_descarta_porque_el_precio_no_vive_en_el_contenido()
    {
        // Es la diferencia con Inmobiliaria: allá el precio ≤ 0 omite el inmueble. Acá el
        // precio lo cotiza IRoomAvailabilityProvider por fecha y ocupación, y esta capa
        // describe la propiedad — nunca la tarifa.
        Assert.NotNull(Build(Content()));
    }

    // ── Lo que se sirve degradado ────────────────────────────────────────────

    [Fact]
    public void Sin_departamento_la_direccion_se_arma_sin_comas_colgantes()
    {
        var conRegion = Build(Content())!;
        var sinRegion = Build(Content(region: null))!;

        Assert.Equal("Cartagena, Bolívar", conRegion.Address);
        Assert.Equal("Cartagena", sinRegion.Address);
    }

    [Fact]
    public void La_direccion_autorada_gana_sobre_la_armada()
    {
        var detail = Build(Content() with { Address = "Calle de la Sierpe #29-108" });

        Assert.Equal("Calle de la Sierpe #29-108", detail!.Address);
    }

    [Fact]
    public void El_slug_es_el_stayId_y_se_normaliza()
    {
        var detail = Build(Content(slug: "  CTG-Getsemani  "));

        Assert.Equal("ctg-getsemani", detail!.StayId);
    }

    [Fact]
    public void Las_amenidades_en_blanco_se_descartan()
    {
        var content = Content() with { Amenities = new[] { "Piscina exterior", "   ", "Wifi" } };

        Assert.Equal(new[] { "Piscina exterior", "Wifi" }, Build(content)!.Amenities);
    }

    [Fact]
    public void La_galeria_conserva_el_orden_y_descarta_los_huecos()
    {
        var content = Content() with
        {
            Gallery = new[] { "/media/booking/a.jpg", "  ", "/media/booking/b.jpg" },
        };

        Assert.Equal(new[] { "/media/booking/a.jpg", "/media/booking/b.jpg" }, Build(content)!.Gallery);
    }

    // ── Geo: el contrato con el mapa ─────────────────────────────────────────

    [Fact]
    public void Con_las_dos_coordenadas_en_rango_la_propiedad_sale_en_el_mapa()
    {
        var detail = Build(Content(lat: 10.4224, lng: -75.5468));

        Assert.Equal(10.4224, detail!.Geo.Lat);
        Assert.Equal(-75.5468, detail.Geo.Lng);
    }

    [Fact]
    public void Sin_geo_la_propiedad_se_sirve_en_cero_cero_y_NO_se_descarta()
    {
        var result = StayContentRules.BuildStay(Content(lat: null, lng: null), SpecLabels, ReviewLabels);

        Assert.NotNull(result.Value);
        Assert.Equal(0d, result.Value!.Geo.Lat);
        Assert.Equal(0d, result.Value.Geo.Lng);
        // Y no avisa: no tener geo es legítimo, no un error del hotelero.
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void Media_coordenada_no_es_medio_pin()
    {
        var result = StayContentRules.BuildStay(Content(lat: 10.4224, lng: null), SpecLabels, ReviewLabels);

        Assert.Equal(0d, result.Value!.Geo.Lat);
        Assert.Equal(0d, result.Value.Geo.Lng);
        Assert.NotEmpty(result.Issues);
    }

    [Theory]
    [InlineData(95d, -75.54)]   // latitud imposible
    [InlineData(10.42, -200d)]  // longitud imposible
    public void Una_coordenada_fuera_de_rango_saca_del_mapa_pero_no_del_catalogo(double lat, double lng)
    {
        var result = StayContentRules.BuildStay(Content(lat: lat, lng: lng), SpecLabels, ReviewLabels);

        Assert.NotNull(result.Value);
        Assert.Equal(0d, result.Value!.Geo.Lat);
        Assert.NotEmpty(result.Issues);
    }

    // ── Características derivadas ────────────────────────────────────────────

    [Fact]
    public void Las_caracteristicas_salen_de_los_campos_tipados_y_en_orden()
    {
        var content = Content() with
        {
            Rooms = 12,
            MaxGuests = 2,
            RoomSizeM2 = 28,
            CheckIn = new TimeOnly(15, 0),
            CheckOut = new TimeOnly(12, 0),
        };

        var specs = Build(content)!.Specs;

        Assert.Equal(
            new[] { "Habitaciones", "Huéspedes máx. por habitación", "Tamaño promedio", "Check-in", "Check-out" },
            specs.Select(s => s.Label));
        Assert.Equal("12", specs[0].Value);
        Assert.Equal("28 m²", specs[2].Value);
    }

    [Fact]
    public void Una_caracteristica_en_cero_o_sin_autorar_NO_sale()
    {
        // "Tamaño promedio: 0 m²" no informa, desinforma.
        var content = Content() with { Rooms = 8, MaxGuests = 0, RoomSizeM2 = 0, CheckIn = null };

        var specs = Build(content)!.Specs;

        Assert.Equal(new[] { "Habitaciones" }, specs.Select(s => s.Label));
    }

    [Fact]
    public void Las_etiquetas_vienen_de_afuera_y_no_estan_escritas_en_la_regla()
    {
        // Es lo que permite que vivan en el Dictionary uSync: si estuvieran hardcodeadas,
        // traducir "Check-in" exigiría un despliegue.
        var english = new StaySpecLabels("Rooms", "Max. guests", "Room size", "Check-in", "Check-out");
        var content = Content() with { Rooms = 12, RoomSizeM2 = 28 };

        var specs = StayContentRules.BuildSpecs(content, english);

        Assert.Equal(new[] { "Rooms", "Room size" }, specs.Select(s => s.Label));
    }

    [Theory]
    [InlineData(15, 0, "3:00 p. m.")]
    [InlineData(12, 0, "12:00 m.")]     // mediodía en Colombia se escribe "m.", no "p. m."
    [InlineData(11, 0, "11:00 a. m.")]
    [InlineData(0, 30, "12:30 a. m.")]  // la madrugada es 12:xx a. m., no 0:xx
    [InlineData(12, 30, "12:30 p. m.")] // pasado el mediodía ya es tarde
    [InlineData(13, 5, "1:05 p. m.")]   // los minutos van con dos dígitos
    public void La_hora_se_escribe_como_la_lee_un_viajero_colombiano(int hour, int minute, string expected)
    {
        // Se DERIVA y no se teclea: un campo libre daría "15:00", "3pm" y "3:00 PM" en el mismo
        // sitio. Y se compone a mano porque ninguna cultura de .NET produce "12:00 m." — que
        // además haría que la cadena cambie con la versión de ICU, sin que nada falle.
        Assert.Equal(expected, StayContentRules.FormatTime(new TimeOnly(hour, minute)));
    }

    // ── Reseñas: la reputación no se inventa ─────────────────────────────────

    [Fact]
    public void El_desglose_sale_de_los_cuatro_ejes_fijos_y_en_orden()
    {
        var content = Content() with
        {
            ReviewAverage = 9.2m,
            ReviewCount = 418,
            ReviewHighlight = "  Ubicación excepcional  ",
            ScoreCleanliness = 9.4m,
            ScoreLocation = 9.8m,
            ScoreService = 9.1m,
            ScoreValue = 8.7m,
        };

        var reviews = Build(content)!.Reviews;

        Assert.Equal(9.2d, reviews.Average);
        Assert.Equal(418, reviews.Count);
        Assert.Equal("Ubicación excepcional", reviews.Highlight);
        Assert.Equal(
            new[] { "Limpieza", "Ubicación", "Servicio", "Relación precio-calidad" },
            reviews.Categories.Select(c => c.Category));
    }

    [Fact]
    public void Un_eje_en_cero_es_un_campo_sin_diligenciar_y_no_sale()
    {
        var content = Content() with
        {
            ReviewAverage = 8.6m, ReviewCount = 120,
            ScoreCleanliness = 8.8m, ScoreLocation = 0m, ScoreService = 8.5m, ScoreValue = 0m,
        };

        var reviews = Build(content)!.Reviews;

        Assert.Equal(new[] { "Limpieza", "Servicio" }, reviews.Categories.Select(c => c.Category));
    }

    [Fact]
    public void Un_puntaje_sin_resenas_detras_NO_es_prueba_social_y_el_bloque_va_vacio()
    {
        // Ningún portal del rubro muestra "9,2" respaldado por cero opiniones: emitirlo sería
        // inventar reputación. La propiedad NO se pierde — sale sin bloque de reseñas.
        var content = Content() with
        {
            ReviewAverage = 9.2m, ReviewCount = 0, ReviewHighlight = "Impecable",
            ScoreCleanliness = 9.4m,
        };

        var result = StayContentRules.BuildStay(content, SpecLabels, ReviewLabels);

        Assert.NotNull(result.Value);
        Assert.Equal(0d, result.Value!.Reviews.Average);
        Assert.Equal(0, result.Value.Reviews.Count);
        Assert.Equal(string.Empty, result.Value.Reviews.Highlight);
        Assert.Empty(result.Value.Reviews.Categories);
        Assert.NotEmpty(result.Issues);
    }

    [Fact]
    public void Una_propiedad_recien_abierta_sin_nada_de_reputacion_no_genera_ruido()
    {
        // Sin reseñas Y sin puntajes es lo normal el primer día; avisar ahí sería ruido en el
        // log de cada arranque.
        var result = StayContentRules.BuildStay(Content(), SpecLabels, ReviewLabels);

        Assert.Equal(0, result.Value!.Reviews.Count);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void El_promedio_NO_se_calcula_a_partir_de_los_ejes()
    {
        // La media de los cuatro ejes no es la calificación general en ningún portal del rubro:
        // derivarla sería inventar un número con pinta de dato.
        var content = Content() with
        {
            ReviewAverage = 0m, ReviewCount = 200,
            ScoreCleanliness = 9m, ScoreLocation = 9m, ScoreService = 9m, ScoreValue = 9m,
        };

        var result = StayContentRules.BuildStay(content, SpecLabels, ReviewLabels);

        Assert.Equal(0d, result.Value!.Reviews.Average);
        // Pero sí avisa: un desglose sin encabezado es una ficha a medias.
        Assert.NotEmpty(result.Issues);
    }

    [Theory]
    [InlineData(92, 10d)]   // el hotelero pensó sobre 100
    [InlineData(-3, 0d)]
    public void Un_puntaje_fuera_de_la_escala_se_recorta_sin_perder_la_propiedad(int raw, double expected)
    {
        var content = Content() with { ReviewAverage = raw, ReviewCount = 50 };

        var result = StayContentRules.BuildStay(content, SpecLabels, ReviewLabels);

        Assert.NotNull(result.Value);
        Assert.Equal(expected, result.Value!.Reviews.Average);
        Assert.NotEmpty(result.Issues);
    }

    [Fact]
    public void Un_eje_fuera_de_la_escala_se_recorta_y_sigue_en_el_desglose()
    {
        var content = Content() with { ReviewAverage = 9m, ReviewCount = 50, ScoreCleanliness = 94m };

        var result = StayContentRules.BuildStay(content, SpecLabels, ReviewLabels);

        Assert.Equal(10d, result.Value!.Reviews.Categories.Single().Score);
        Assert.NotEmpty(result.Issues);
    }

    // ── Tipos de habitación: la llave con el buscador ────────────────────────

    [Fact]
    public void Los_codigos_se_normalizan_a_mayusculas_y_sin_repetidos()
    {
        // El cruce con RoomOffer.RoomTypeCode se resuelve por comparación: un "dlx " con espacio
        // es una oferta sin nombre, sin foto y sin pin.
        var content = Content() with { RoomTypeCodes = new[] { " dlx ", "STD", "Dlx", "  " } };

        Assert.Equal(new[] { "DLX", "STD" }, Build(content)!.RoomTypeCodes);
    }

    [Fact]
    public void Una_propiedad_direct_only_sin_codigos_es_legitima_y_no_avisa()
    {
        // Una finca sin cupo en el motor de disponibilidad tiene ficha y no tiene ofertas.
        var result = StayContentRules.BuildStay(Content(), SpecLabels, ReviewLabels);

        Assert.Empty(result.Value!.RoomTypeCodes);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void Dos_propiedades_con_el_mismo_codigo_es_un_ERROR_del_catalogo_entero()
    {
        // GetStayAsync devuelve la PRIMERA que lo declara: las ofertas de la segunda saldrían
        // con el nombre, la foto y el pin de la primera. No falla nada — por eso hay que
        // gritarlo.
        var uno = Build(Content(slug: "ctg-getsemani") with { RoomTypeCodes = new[] { "DLX" } })!;
        var dos = Build(Content(slug: "med-provenza") with { RoomTypeCodes = new[] { "dlx", "STD" } })!;

        var issues = StayContentRules.FindRoomTypeCollisions(new[] { uno, dos });

        var issue = Assert.Single(issues);
        Assert.Equal(EventContentIssueLevel.Error, issue.Level);
        Assert.Contains("DLX", issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Un_catalogo_con_codigos_unicos_no_reporta_colisiones()
    {
        var uno = Build(Content(slug: "ctg-getsemani") with { RoomTypeCodes = new[] { "STD" } })!;
        var dos = Build(Content(slug: "med-provenza") with { RoomTypeCodes = new[] { "DLX" } })!;

        Assert.Empty(StayContentRules.FindRoomTypeCollisions(new[] { uno, dos }));
    }

    [Fact]
    public void Un_catalogo_vacio_no_reporta_colisiones()
    {
        Assert.Empty(StayContentRules.FindRoomTypeCollisions(Array.Empty<StayDetail>()));
    }

    // ── Contrato del llamador ────────────────────────────────────────────────

    [Fact]
    public void Un_contenido_nulo_es_un_error_de_programacion_y_lanza()
    {
        Assert.Throws<ArgumentNullException>(
            () => StayContentRules.BuildStay(null!, SpecLabels, ReviewLabels));
        Assert.Throws<ArgumentNullException>(
            () => StayContentRules.BuildStay(Content(), null!, ReviewLabels));
        Assert.Throws<ArgumentNullException>(
            () => StayContentRules.BuildStay(Content(), SpecLabels, null!));
    }
}
