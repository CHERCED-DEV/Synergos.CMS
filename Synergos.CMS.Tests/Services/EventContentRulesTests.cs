using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services.Catalog;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Cubre las reglas que convierten lo que el editor autoró en <c>eventPage</c> en una ficha
/// comprable (ADR 0117).
/// </summary>
/// <remarks>
/// <para>Estas reglas vivían dentro del lector de Umbraco y por eso no tenían un solo test:
/// <c>IPublishedContent.Value&lt;T&gt;</c> es un método de extensión sobre una cadena de
/// propiedades y fallbacks, así que simularlo cuesta más que el código que verifica. Sacarlas
/// a una clase pura fue lo que las hizo verificables — y son justo las que deciden si alguien
/// paga por un asiento que existe.</para>
///
/// <para>El eje de todas: <b>omitir nunca es lanzar, y servir a medias nunca es una opción</b>.
/// Un evento con una localidad mal escrita vende las otras; uno con una zona ambigua no vende
/// esa zona en vez de venderla mal.</para>
/// </remarks>
public sealed class EventContentRulesTests
{
    private const string Cop = "COP";
    private const string Slug = "festival-estereo";

    /// <summary>La zona del sitio por defecto: es la que dice qué día es «hasta el 14».</summary>
    private static readonly TimeZoneInfo Bogota = new ListadosSettings().Zona()!;

    private static EventTierContent Tier(
        string? code = "general",
        string? name = "Entrada general",
        int price = 180_000,
        int capacity = 500,
        int maxPerOrder = 0,
        string? zoneId = null,
        bool featured = false)
        => new(code, name, price, capacity, maxPerOrder, zoneId, Featured: featured);

    // ── Localidades ──────────────────────────────────────────────────────────

    [Fact]
    public void Sin_localidades_no_hay_nada_que_vender_y_tampoco_hay_error()
    {
        var result = EventContentRules.BuildTiers(Slug, null, Cop, Bogota);

        Assert.Empty(result.Value);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void Una_localidad_completa_se_sirve_entera()
    {
        var draft = new EventTierContent(
            Code: "  VIP  ",
            Name: "Platea VIP",
            Price: 380_000,
            Capacity: 120,
            MaxPerOrder: 4,
            ZoneId: "Platea",
            Description: " Acceso preferencial ",
            Perks: new[] { "Ingreso prioritario", "  ", "Bebida de bienvenida" },
            SaleWindow: "Hasta el 12 de julio",
            Featured: true);

        var tier = Assert.Single(EventContentRules.BuildTiers(Slug, new[] { draft }, Cop, Bogota).Value);

        // El código se normaliza porque es lo que el checkout compara.
        Assert.Equal("vip", tier.Code);
        Assert.Equal("platea", tier.ZoneId);
        Assert.Equal("Platea VIP", tier.Name);
        Assert.Equal(380_000, tier.Price);
        Assert.Equal(Cop, tier.Currency);
        Assert.Equal(4, tier.MaxPerOrder);
        Assert.Equal("Acceso preferencial", tier.Description);
        Assert.Equal(new[] { "Ingreso prioritario", "Bebida de bienvenida" }, tier.Perks);
        Assert.True(tier.Featured);
    }

    [Fact]
    public void El_codigo_repetido_conserva_la_PRIMERA_y_avisa()
    {
        // Con dos "vip" a distinto precio, cuál cobra el checkout dependería del orden de un
        // diccionario. El comprador pagaría lo que saliera.
        var result = EventContentRules.BuildTiers(
            Slug,
            new[]
            {
                Tier(code: "vip", name: "VIP", price: 380_000),
                Tier(code: "VIP", name: "VIP duplicada", price: 100_000),
            },
            Cop, Bogota);

        var tier = Assert.Single(result.Value);
        Assert.Equal(380_000, tier.Price);
        Assert.Contains(result.Issues, i => i.Level == EventContentIssueLevel.Error);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Una_localidad_sin_aforo_no_se_pone_a_la_venta(int capacity)
    {
        var result = EventContentRules.BuildTiers(Slug, new[] { Tier(capacity: capacity) }, Cop, Bogota);

        Assert.Empty(result.Value);
        Assert.Contains(result.Issues, i => i.Level == EventContentIssueLevel.Warning);
    }

    [Fact]
    public void Un_precio_negativo_se_descarta_en_vez_de_pagarle_al_comprador()
    {
        var result = EventContentRules.BuildTiers(Slug, new[] { Tier(price: -1) }, Cop, Bogota);

        Assert.Empty(result.Value);
        Assert.Contains(result.Issues, i => i.Level == EventContentIssueLevel.Error);
    }

    [Fact]
    public void Sin_codigo_o_sin_nombre_la_localidad_se_omite()
    {
        var result = EventContentRules.BuildTiers(
            Slug,
            new[] { Tier(code: "   "), Tier(code: "vip", name: null) },
            Cop, Bogota);

        Assert.Empty(result.Value);
        Assert.Equal(2, result.Issues.Count);
    }

    [Fact]
    public void El_tope_por_compra_nunca_supera_el_aforo()
    {
        // Ofrecer "hasta 10" sobre una localidad de 4 es prometer seis entradas que no existen.
        var tier = Assert.Single(
            EventContentRules.BuildTiers(Slug, new[] { Tier(capacity: 4, maxPerOrder: 10) }, Cop, Bogota).Value);

        Assert.Equal(4, tier.MaxPerOrder);
    }

    [Fact]
    public void Sin_tope_declarado_se_aplica_el_de_la_casa()
    {
        var tier = Assert.Single(
            EventContentRules.BuildTiers(Slug, new[] { Tier(maxPerOrder: 0) }, Cop, Bogota).Value);

        Assert.Equal(EventContentRules.DefaultMaxPerOrder, tier.MaxPerOrder);
    }

    [Fact]
    public void Solo_UNA_localidad_queda_recomendada()
    {
        var result = EventContentRules.BuildTiers(
            Slug,
            new[]
            {
                Tier(code: "general", featured: true),
                Tier(code: "vip", featured: true),
                Tier(code: "palco", featured: true),
            },
            Cop, Bogota);

        Assert.Equal("general", Assert.Single(result.Value, t => t.Featured).Code);
        Assert.Contains(result.Issues, i => i.Level == EventContentIssueLevel.Warning);
    }

    [Fact]
    public void Lo_que_queda_arranca_igual_al_aforo_declarado()
    {
        // El contenido declara CUÁNTO hay, no cuánto queda: lo vendido lo sabe el ledger de
        // reservas, que esta capa no ve. Es una limitación conocida, no un descuido.
        var tier = Assert.Single(EventContentRules.BuildTiers(Slug, new[] { Tier(capacity: 500) }, Cop, Bogota).Value);

        Assert.Equal(500, tier.Capacity);
        Assert.Equal(500, tier.Remaining);
    }

    // ── La ventana de venta (#195) ───────────────────────────────────────────
    //
    // El editor elige dos DÍAS; el checkout compara INSTANTES. La conversión es la regla: abre al
    // empezar el primer día y cierra al empezar el día siguiente al último, en la zona del sitio.

    private static EventTierContent ConVentana(DateTime? desde, DateTime? hasta)
        => Tier(code: "vip") with { SaleOpens = desde, SaleCloses = hasta };

    [Fact] // «hasta el 14» vende todo el 14 EN BOGOTÁ: cierra a las 00:00 del 15 de allá (05:00Z).
    public void La_ventana_abre_al_empezar_el_dia_y_cierra_al_empezar_el_siguiente_en_la_zona_del_sitio()
    {
        var tier = Assert.Single(EventContentRules.BuildTiers(
            Slug, new[] { ConVentana(new DateTime(2026, 8, 1), new DateTime(2026, 8, 14)) }, Cop, Bogota).Value);

        Assert.Equal(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.FromHours(-5)), tier.SaleOpensUtc);
        Assert.Equal(new DateTimeOffset(2026, 8, 15, 5, 0, 0, TimeSpan.Zero), tier.SaleClosesUtc);
    }

    [Fact] // la hora que el date picker arrastre no cuenta: el editor eligió un día.
    public void La_hora_del_dia_elegido_no_mueve_la_ventana()
    {
        var tier = Assert.Single(EventContentRules.BuildTiers(
            Slug, new[] { ConVentana(new DateTime(2026, 8, 1, 18, 30, 0), new DateTime(2026, 8, 14, 18, 30, 0)) }, Cop, Bogota).Value);

        Assert.Equal(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.FromHours(-5)), tier.SaleOpensUtc);
        Assert.Equal(new DateTimeOffset(2026, 8, 15, 0, 0, 0, TimeSpan.FromHours(-5)), tier.SaleClosesUtc);
    }

    [Fact] // vacío: sin límite por ese lado, y no un año 1 que cerraría la venta para siempre.
    public void Sin_fechas_la_localidad_no_tiene_ventana()
    {
        var tier = Assert.Single(EventContentRules.BuildTiers(Slug, new[] { ConVentana(null, null) }, Cop, Bogota).Value);

        Assert.Null(tier.SaleOpensUtc);
        Assert.Null(tier.SaleClosesUtc);
    }

    [Fact] // un mismo día de apertura y cierre se vende ese día entero.
    public void Abrir_y_cerrar_el_mismo_dia_vende_ese_dia()
    {
        var result = EventContentRules.BuildTiers(
            Slug, new[] { ConVentana(new DateTime(2026, 8, 14), new DateTime(2026, 8, 14)) }, Cop, Bogota);

        var tier = Assert.Single(result.Value);
        Assert.Equal(TimeSpan.FromDays(1), tier.SaleClosesUtc - tier.SaleOpensUtc);
        Assert.Empty(result.Issues);
    }

    [Fact] // al revés no se vende nunca: se omite con un error que nombra los dos campos.
    public void Una_ventana_al_reves_omite_la_localidad_con_error()
    {
        var result = EventContentRules.BuildTiers(
            Slug, new[] { ConVentana(new DateTime(2026, 8, 14), new DateTime(2026, 8, 1)), Tier(code: "general") }, Cop, Bogota);

        Assert.Equal("general", Assert.Single(result.Value).Code);
        var error = Assert.Single(result.Issues);
        Assert.Equal(EventContentIssueLevel.Error, error.Level);
        Assert.Contains("Venta desde", error.Message, StringComparison.Ordinal);
    }

    [Fact] // 31-12-9999 es «no cierra»: el día siguiente no existe y lanzar tumbaba TODOS los eventos.
    public void Venta_hasta_el_ultimo_dia_del_calendario_es_venta_sin_cierre_con_aviso()
    {
        var result = EventContentRules.BuildTiers(
            Slug, new[] { ConVentana(new DateTime(2026, 8, 1), new DateTime(9999, 12, 31)) }, Cop, Bogota);

        var tier = Assert.Single(result.Value);
        Assert.Equal(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.FromHours(-5)), tier.SaleOpensUtc);
        Assert.Null(tier.SaleClosesUtc);
        var aviso = Assert.Single(result.Issues);
        Assert.Equal(EventContentIssueLevel.Warning, aviso.Level);
        Assert.Contains("Venta hasta", aviso.Message, StringComparison.Ordinal);
    }

    [Fact] // 01-01-0001 es «abierta desde siempre»: en una zona al este de UTC, su medianoche no cabe.
    public void Venta_desde_el_primer_dia_del_calendario_es_venta_sin_apertura_con_aviso()
    {
        var tokio = new ListadosSettings { ZonaHoraria = "Asia/Tokyo" }.Zona()!;

        var result = EventContentRules.BuildTiers(
            Slug, new[] { ConVentana(new DateTime(1, 1, 1), new DateTime(2026, 8, 14)) }, Cop, tokio);

        var tier = Assert.Single(result.Value);
        Assert.Null(tier.SaleOpensUtc);
        Assert.Equal(new DateTimeOffset(2026, 8, 15, 0, 0, 0, TimeSpan.FromHours(9)), tier.SaleClosesUtc);
        var aviso = Assert.Single(result.Issues);
        Assert.Equal(EventContentIssueLevel.Warning, aviso.Level);
        Assert.Contains("Venta desde", aviso.Message, StringComparison.Ordinal);
    }

    [Fact] // una medianoche que se repite (La Habana atrasa el reloj a la 01:00) es la PRIMERA.
    public void Una_medianoche_que_el_cambio_de_hora_repite_abre_y_cierra_la_primera_vez()
    {
        // El 1-nov-2026 La Habana vuelve de UTC−4 a UTC−5 a la 01:00: de 00:00 a 01:00 pasa dos
        // veces. El día empieza la primera, a las 04:00Z; con el desfase estándar, una hora tarde.
        var habana = new ListadosSettings { ZonaHoraria = "America/Havana" }.Zona()!;

        var result = EventContentRules.BuildTiers(
            Slug,
            new[]
            {
                ConVentana(new DateTime(2026, 11, 1), null),
                Tier(code: "general") with { SaleCloses = new DateTime(2026, 10, 31) },
            },
            Cop,
            habana);

        var primeraVez = new DateTimeOffset(2026, 11, 1, 4, 0, 0, TimeSpan.Zero);
        Assert.Equal(primeraVez, result.Value.Single(t => t.Code == "vip").SaleOpensUtc);
        Assert.Equal(primeraVez, result.Value.Single(t => t.Code == "general").SaleClosesUtc);
    }

    // ── Mapa de asientos ─────────────────────────────────────────────────────

    private static EventZoneContent Zone(
        string? id = "platea",
        string? name = "Platea",
        string? tierCode = "vip",
        int price = 0,
        string[]? rows = null,
        int seatsPerRow = 3)
        => new(id, name, tierCode, price, rows ?? new[] { "A", "B" }, seatsPerRow);

    [Fact]
    public void Sin_zonas_no_hay_mapa_y_el_evento_es_de_cupo_general()
    {
        var result = EventContentRules.BuildSeatMap(Slug, null, Array.Empty<EventTier>(), Cop, "Teatro");

        Assert.Null(result.Value);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void Los_asientos_se_GENERAN_de_filas_por_butacas()
    {
        var tiers = EventContentRules.BuildTiers(Slug, new[] { Tier(code: "vip") }, Cop, Bogota).Value;

        var map = EventContentRules.BuildSeatMap(
            Slug, new[] { Zone(rows: new[] { "A", "B" }, seatsPerRow: 3) }, tiers, Cop, "Teatro Metropolitano").Value;

        var zone = Assert.Single(map!.Zones);
        Assert.Equal("Teatro Metropolitano", map.VenueName);
        Assert.Equal(2, zone.Rows.Count);
        Assert.Equal(new[] { "A1", "A2", "A3" }, zone.Rows[0].Seats.Select(s => s.Label));
        // El id lleva la zona: dos zonas del mismo recinto pueden tener una fila "A", y sin el
        // prefijo apartar A1 en Platea apartaría A1 en Palco.
        Assert.Equal("platea-A1", zone.Rows[0].Seats[0].Id);
        Assert.All(zone.Rows.SelectMany(r => r.Seats), s => Assert.Equal("free", s.Status));
    }

    [Fact]
    public void Una_zona_que_apunta_a_una_localidad_inexistente_se_descarta()
    {
        // Vendería asientos que el checkout no sabe cobrar.
        var tiers = EventContentRules.BuildTiers(Slug, new[] { Tier(code: "general") }, Cop, Bogota).Value;

        var result = EventContentRules.BuildSeatMap(
            Slug, new[] { Zone(tierCode: "vip") }, tiers, Cop, "Teatro");

        Assert.Null(result.Value);
        Assert.Contains(result.Issues, i => i.Level == EventContentIssueLevel.Error);
    }

    [Fact]
    public void Precio_0_en_la_zona_significa_cobrar_el_de_su_localidad()
    {
        var tiers = EventContentRules.BuildTiers(Slug, new[] { Tier(code: "vip", price: 380_000) }, Cop, Bogota).Value;

        var map = EventContentRules.BuildSeatMap(Slug, new[] { Zone(price: 0) }, tiers, Cop, "Teatro").Value;

        Assert.Equal(380_000, Assert.Single(map!.Zones).Price);
    }

    [Fact]
    public void Un_precio_propio_de_zona_gana_sobre_el_de_la_localidad()
    {
        var tiers = EventContentRules.BuildTiers(Slug, new[] { Tier(code: "vip", price: 380_000) }, Cop, Bogota).Value;

        var map = EventContentRules.BuildSeatMap(Slug, new[] { Zone(price: 450_000) }, tiers, Cop, "Teatro").Value;

        Assert.Equal(450_000, Assert.Single(map!.Zones).Price);
    }

    [Fact]
    public void Un_precio_negativo_de_zona_cae_al_de_su_localidad()
    {
        var tiers = EventContentRules.BuildTiers(Slug, new[] { Tier(code: "vip", price: 380_000) }, Cop, Bogota).Value;

        var result = EventContentRules.BuildSeatMap(Slug, new[] { Zone(price: -1) }, tiers, Cop, "Teatro");

        Assert.Equal(380_000, Assert.Single(result.Value!.Zones).Price);
        Assert.Contains(result.Issues, i => i.Level == EventContentIssueLevel.Error);
    }

    [Fact]
    public void Una_zona_que_pasa_el_techo_de_asientos_se_omite_ENTERA()
    {
        // Recortarla dejaría media zona vendiendo asientos que no existen — y eso lo descubre
        // el asistente en la puerta, no el build.
        var tiers = EventContentRules.BuildTiers(Slug, new[] { Tier(code: "vip") }, Cop, Bogota).Value;

        var result = EventContentRules.BuildSeatMap(
            Slug,
            new[] { Zone(rows: new[] { "A", "B" }, seatsPerRow: EventContentRules.MaxSeatsPerEvent) },
            tiers, Cop, "Teatro");

        Assert.Null(result.Value);
        Assert.Contains(result.Issues, i => i.Level == EventContentIssueLevel.Error);
    }

    [Fact]
    public void El_techo_de_asientos_es_por_EVENTO_no_por_zona()
    {
        var tiers = EventContentRules.BuildTiers(Slug, new[] { Tier(code: "vip") }, Cop, Bogota).Value;

        // Cada zona cabe sola; juntas no. La segunda es la que se cae.
        var half = EventContentRules.MaxSeatsPerEvent / 2 + 1;
        var result = EventContentRules.BuildSeatMap(
            Slug,
            new[]
            {
                Zone(id: "platea", rows: new[] { "A" }, seatsPerRow: half),
                Zone(id: "palco", rows: new[] { "A" }, seatsPerRow: half),
            },
            tiers, Cop, "Teatro");

        Assert.Equal("platea", Assert.Single(result.Value!.Zones).Id);
        Assert.Contains(result.Issues, i => i.Level == EventContentIssueLevel.Error);
    }

    [Fact]
    public void El_codigo_de_zona_repetido_conserva_la_primera()
    {
        var tiers = EventContentRules.BuildTiers(Slug, new[] { Tier(code: "vip") }, Cop, Bogota).Value;

        var result = EventContentRules.BuildSeatMap(
            Slug,
            new[] { Zone(id: "platea", name: "Platea"), Zone(id: "PLATEA", name: "Platea bis") },
            tiers, Cop, "Teatro");

        Assert.Equal("Platea", Assert.Single(result.Value!.Zones).Name);
        Assert.Contains(result.Issues, i => i.Level == EventContentIssueLevel.Error);
    }

    [Fact]
    public void Una_zona_sin_filas_o_sin_butacas_se_omite()
    {
        var tiers = EventContentRules.BuildTiers(Slug, new[] { Tier(code: "vip") }, Cop, Bogota).Value;

        var result = EventContentRules.BuildSeatMap(
            Slug,
            new[]
            {
                Zone(id: "a", rows: Array.Empty<string>()),
                Zone(id: "b", seatsPerRow: 0),
                Zone(id: "c", rows: new[] { "  ", "" }),
            },
            tiers, Cop, "Teatro");

        Assert.Null(result.Value);
        Assert.Equal(3, result.Issues.Count);
    }

    // ── Modo de venta ────────────────────────────────────────────────────────

    [Fact]
    public void Un_evento_marcado_reserved_SIN_mapa_se_sirve_como_general()
    {
        // Si no, el asistente cae en una pantalla de mapa vacía, sin forma de comprar.
        var result = EventContentRules.ResolveMode(Slug, "reserved", hasSeatMap: false);

        Assert.Equal("general", result.Value);
        Assert.NotEmpty(result.Issues);
    }

    [Fact]
    public void Un_evento_marcado_general_CON_mapa_se_sirve_como_reserved()
    {
        // Si no, se vendería por cantidad repartiendo asientos que el comprador creía elegir.
        var result = EventContentRules.ResolveMode(Slug, "general", hasSeatMap: true);

        Assert.Equal("reserved", result.Value);
        Assert.NotEmpty(result.Issues);
    }

    [Fact]
    public void Cuando_lo_declarado_coincide_no_se_avisa_nada()
    {
        Assert.Empty(EventContentRules.ResolveMode(Slug, "general", hasSeatMap: false).Issues);
        Assert.Empty(EventContentRules.ResolveMode(Slug, "reserved", hasSeatMap: true).Issues);
    }

    // ── Agenda ───────────────────────────────────────────────────────────────

    [Fact]
    public void La_agenda_se_ordena_por_hora_aunque_el_editor_la_escriba_al_reves()
    {
        var result = EventContentRules.BuildSessions(
            Slug,
            new[]
            {
                new EventSessionContent("22:00", "Cierre estelar", "Cordillera Eléctrica"),
                new EventSessionContent("14:00", "Apertura de puertas", null),
                new EventSessionContent("16:00", "Bandas emergentes", "La Sonora Bogotá"),
            });

        Assert.Equal(new[] { "14:00", "16:00", "22:00" }, result.Value.Select(s => s.Time));
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void Un_punto_de_agenda_sin_hora_o_sin_titulo_se_omite()
    {
        var result = EventContentRules.BuildSessions(
            Slug,
            new[]
            {
                new EventSessionContent(null, "Sin hora", null),
                new EventSessionContent("14:00", "  ", null),
                new EventSessionContent("15:00", "Válido", null),
            });

        Assert.Equal("Válido", Assert.Single(result.Value).Title);
        Assert.Equal(2, result.Issues.Count);
    }

    [Fact]
    public void Sin_ponente_se_sirve_vacio_y_no_se_inventa_un_nombre()
    {
        var session = Assert.Single(
            EventContentRules.BuildSessions(Slug, new[] { new EventSessionContent("14:00", "Apertura", null) }).Value);

        Assert.Equal(string.Empty, session.Speaker);
    }

    // ── Artista ──────────────────────────────────────────────────────────────

    [Fact]
    public void Sin_nombre_no_hay_perfil_de_artista()
    {
        Assert.Null(EventContentRules.BuildArtist("   ", "Headliner", 1000));
        Assert.Null(EventContentRules.BuildArtist(null, "Headliner", 1000));
    }

    [Fact]
    public void Un_numero_de_seguidores_negativo_se_sirve_como_cero()
    {
        var artist = EventContentRules.BuildArtist("Cordillera Eléctrica", null, -3);

        Assert.NotNull(artist);
        Assert.Equal(0, artist!.Followers);
        Assert.Equal(string.Empty, artist.Headline);
    }

    // ── Copy derivado de la tarjeta (movido desde EventosController, auditoría Medio #4) ──

    [Fact]
    public void El_subtitulo_junta_sede_y_ciudad()
    {
        Assert.Equal("Movistar Arena · Bogotá", EventContentRules.BuildSubtitle("Movistar Arena", "Bogotá"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Un_evento_sin_sede_emite_solo_la_ciudad_sin_separador_colgante(string? venue)
    {
        // Es el caso NORMAL de un evento online, no una excepción rara.
        var subtitle = EventContentRules.BuildSubtitle(venue, "Bogotá");

        Assert.Equal("Bogotá", subtitle);
        Assert.DoesNotContain("·", subtitle, StringComparison.Ordinal);
    }

    [Fact]
    public void Sin_ciudad_queda_la_sede_sola()
    {
        Assert.Equal("Movistar Arena", EventContentRules.BuildSubtitle("Movistar Arena", null));
    }

    [Fact]
    public void El_evento_que_ya_arranco_es_pasado_y_el_que_no_es_proximo()
    {
        var now = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal("past", EventContentRules.BuildStatus(now.AddMinutes(-1), now));
        Assert.Equal("upcoming", EventContentRules.BuildStatus(now.AddMinutes(1), now));
    }

    [Fact]
    public void El_evento_que_arranca_JUSTO_ahora_ya_es_pasado()
    {
        // El límite exacto: con el reloj adentro este caso no se podía escribir. Se fija <= y
        // no <, porque un evento que arranca en este instante ya no se vende.
        var now = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal("past", EventContentRules.BuildStatus(now, now));
    }

    [Fact]
    public void El_modo_reserved_promete_asientos_numerados()
    {
        Assert.Equal(new[] { "Asientos numerados" }, EventContentRules.BuildBadges("reserved"));
        Assert.Equal(new[] { "Asientos numerados" }, EventContentRules.BuildBadges("RESERVED"));
    }

    [Theory]
    [InlineData("general")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("cualquier-cosa")]
    public void Todo_lo_demas_cae_a_entrada_general(string? mode)
    {
        // Incluido el modo desconocido: general es el default del vertical, y prometer
        // "asientos numerados" sobre un evento que se vende por cantidad sería mentirle al
        // comprador en la tarjeta.
        Assert.Equal(new[] { "Entrada general" }, EventContentRules.BuildBadges(mode));
    }

    [Fact]
    public void El_chip_y_el_modo_resuelto_hablan_el_mismo_vocabulario()
    {
        // Las dos caras de la misma decisión. Si divergen, la tarjeta promete asientos
        // numerados sobre un evento que ResolveMode sirvió como general.
        var resolved = EventContentRules.ResolveMode("slug", declaredMode: "general", hasSeatMap: true);

        Assert.Equal(EventContentRules.ReservedMode, resolved.Value);
        Assert.Equal(new[] { "Asientos numerados" }, EventContentRules.BuildBadges(resolved.Value));
    }
}
