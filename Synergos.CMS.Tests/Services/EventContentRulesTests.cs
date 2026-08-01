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
        var result = EventContentRules.BuildTiers(Slug, null, Cop);

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

        var tier = Assert.Single(EventContentRules.BuildTiers(Slug, new[] { draft }, Cop).Value);

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
            Cop);

        var tier = Assert.Single(result.Value);
        Assert.Equal(380_000, tier.Price);
        Assert.Contains(result.Issues, i => i.Level == EventContentIssueLevel.Error);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Una_localidad_sin_aforo_no_se_pone_a_la_venta(int capacity)
    {
        var result = EventContentRules.BuildTiers(Slug, new[] { Tier(capacity: capacity) }, Cop);

        Assert.Empty(result.Value);
        Assert.Contains(result.Issues, i => i.Level == EventContentIssueLevel.Warning);
    }

    [Fact]
    public void Un_precio_negativo_se_descarta_en_vez_de_pagarle_al_comprador()
    {
        var result = EventContentRules.BuildTiers(Slug, new[] { Tier(price: -1) }, Cop);

        Assert.Empty(result.Value);
        Assert.Contains(result.Issues, i => i.Level == EventContentIssueLevel.Error);
    }

    [Fact]
    public void Sin_codigo_o_sin_nombre_la_localidad_se_omite()
    {
        var result = EventContentRules.BuildTiers(
            Slug,
            new[] { Tier(code: "   "), Tier(code: "vip", name: null) },
            Cop);

        Assert.Empty(result.Value);
        Assert.Equal(2, result.Issues.Count);
    }

    [Fact]
    public void El_tope_por_compra_nunca_supera_el_aforo()
    {
        // Ofrecer "hasta 10" sobre una localidad de 4 es prometer seis entradas que no existen.
        var tier = Assert.Single(
            EventContentRules.BuildTiers(Slug, new[] { Tier(capacity: 4, maxPerOrder: 10) }, Cop).Value);

        Assert.Equal(4, tier.MaxPerOrder);
    }

    [Fact]
    public void Sin_tope_declarado_se_aplica_el_de_la_casa()
    {
        var tier = Assert.Single(
            EventContentRules.BuildTiers(Slug, new[] { Tier(maxPerOrder: 0) }, Cop).Value);

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
            Cop);

        Assert.Equal("general", Assert.Single(result.Value.Where(t => t.Featured)).Code);
        Assert.Contains(result.Issues, i => i.Level == EventContentIssueLevel.Warning);
    }

    [Fact]
    public void Lo_que_queda_arranca_igual_al_aforo_declarado()
    {
        // El contenido declara CUÁNTO hay, no cuánto queda: lo vendido lo sabe el ledger de
        // reservas, que esta capa no ve. Es una limitación conocida, no un descuido.
        var tier = Assert.Single(EventContentRules.BuildTiers(Slug, new[] { Tier(capacity: 500) }, Cop).Value);

        Assert.Equal(500, tier.Capacity);
        Assert.Equal(500, tier.Remaining);
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
        var tiers = EventContentRules.BuildTiers(Slug, new[] { Tier(code: "vip") }, Cop).Value;

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
        var tiers = EventContentRules.BuildTiers(Slug, new[] { Tier(code: "general") }, Cop).Value;

        var result = EventContentRules.BuildSeatMap(
            Slug, new[] { Zone(tierCode: "vip") }, tiers, Cop, "Teatro");

        Assert.Null(result.Value);
        Assert.Contains(result.Issues, i => i.Level == EventContentIssueLevel.Error);
    }

    [Fact]
    public void Precio_0_en_la_zona_significa_cobrar_el_de_su_localidad()
    {
        var tiers = EventContentRules.BuildTiers(Slug, new[] { Tier(code: "vip", price: 380_000) }, Cop).Value;

        var map = EventContentRules.BuildSeatMap(Slug, new[] { Zone(price: 0) }, tiers, Cop, "Teatro").Value;

        Assert.Equal(380_000, Assert.Single(map!.Zones).Price);
    }

    [Fact]
    public void Un_precio_propio_de_zona_gana_sobre_el_de_la_localidad()
    {
        var tiers = EventContentRules.BuildTiers(Slug, new[] { Tier(code: "vip", price: 380_000) }, Cop).Value;

        var map = EventContentRules.BuildSeatMap(Slug, new[] { Zone(price: 450_000) }, tiers, Cop, "Teatro").Value;

        Assert.Equal(450_000, Assert.Single(map!.Zones).Price);
    }

    [Fact]
    public void Un_precio_negativo_de_zona_cae_al_de_su_localidad()
    {
        var tiers = EventContentRules.BuildTiers(Slug, new[] { Tier(code: "vip", price: 380_000) }, Cop).Value;

        var result = EventContentRules.BuildSeatMap(Slug, new[] { Zone(price: -1) }, tiers, Cop, "Teatro");

        Assert.Equal(380_000, Assert.Single(result.Value!.Zones).Price);
        Assert.Contains(result.Issues, i => i.Level == EventContentIssueLevel.Error);
    }

    [Fact]
    public void Una_zona_que_pasa_el_techo_de_asientos_se_omite_ENTERA()
    {
        // Recortarla dejaría media zona vendiendo asientos que no existen — y eso lo descubre
        // el asistente en la puerta, no el build.
        var tiers = EventContentRules.BuildTiers(Slug, new[] { Tier(code: "vip") }, Cop).Value;

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
        var tiers = EventContentRules.BuildTiers(Slug, new[] { Tier(code: "vip") }, Cop).Value;

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
        var tiers = EventContentRules.BuildTiers(Slug, new[] { Tier(code: "vip") }, Cop).Value;

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
        var tiers = EventContentRules.BuildTiers(Slug, new[] { Tier(code: "vip") }, Cop).Value;

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
}
