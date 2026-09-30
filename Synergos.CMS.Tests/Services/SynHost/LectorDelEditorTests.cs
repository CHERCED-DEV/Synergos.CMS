using System.Text.Json;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Synergos.CMS.Web.Services.SynHost;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Routing;
using Umbraco.Cms.Core.Strings;

namespace Synergos.CMS.Tests.Services.SynHost;

/// <summary>
/// Cubre las lecturas de <see cref="LectorDelEditor"/> que traducen un DataType de Umbraco a lo
/// que viaja (ADR 0135): cada una se escribe UNA vez y la usan todos los resolvers. Las lecturas
/// del piloto (texto, números, JSON) las cubren los tests de sus resolvers.
/// </summary>
public sealed class LectorDelEditorTests
{
    private readonly ILogger _log = Substitute.For<ILogger>();

    private int Anotados() => _log.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(ILogger.Log));

    /// <summary>Lo que quedó anotado, ya formateado: el editor lo lee para saber qué corregir.</summary>
    private IReadOnlyList<string> Anotaciones() => _log.ReceivedCalls()
        .Where(c => c.GetMethodInfo().Name == nameof(ILogger.Log))
        .Select(c => c.GetArguments()[2]?.ToString() ?? string.Empty)
        .ToList();

    private LectorDelEditor Lector(IPublishedElement elemento, IPublishedUrlProvider? urls = null)
        => new(elemento, ElementoFalso.Fallback, _log, urls ?? ElementoFalso.Urls());

    // ── Medio (Umbraco.MediaPicker3) ────────────────────────────────────────────────────────

    [Fact]
    public void Medio_sin_elegir_no_viaja()
    {
        Assert.Null(Lector(ElementoFalso.Con()).Medio("audioFile"));
        Assert.Equal(0, Anotados());
    }

    [Fact]
    public void Medio_elegido_viaja_como_su_url_absoluta_y_su_texto_alternativo()
    {
        var urls = ElementoFalso.Urls();
        var medio = ElementoFalso.Medio("/media/ana.jpg", "  Ana Gómez, directora  ");

        var leido = Lector(ElementoFalso.Con(("avatarImage", medio)), urls).Medio("avatarImage");

        Assert.Equal(new MedioDelEditor("/media/ana.jpg", "Ana Gómez, directora"), leido);
        urls.Received(1).GetMediaUrl(medio, UrlMode.Absolute, null, "umbracoFile", null);
    }

    [Fact]
    public void Medio_sin_texto_alternativo_viaja_sin_alt()
    {
        var leido = Lector(ElementoFalso.Con(("audioFile", ElementoFalso.Medio("/media/a.mp3")))).Medio("audioFile");

        Assert.Equal(new MedioDelEditor("/media/a.mp3", null), leido);
    }

    [Theory]
    [InlineData("")]
    [InlineData("#")]
    public void Medio_sin_fichero_no_viaja_y_se_anota(string url)
    {
        var leido = Lector(ElementoFalso.Con(("videoFile", ElementoFalso.Medio(url)))).Medio("videoFile");

        Assert.Null(leido);
        Assert.Equal(1, Anotados());
    }

    [Fact]
    public void Medio_que_llega_como_lista_toma_el_primero()
    {
        IEnumerable<IPublishedContent> varios = [ElementoFalso.Medio("/media/1.jpg"), ElementoFalso.Medio("/media/2.jpg")];

        var leido = Lector(ElementoFalso.Con(("media", varios))).Medio("media");

        Assert.Equal("/media/1.jpg", leido?.Url);
    }

    [Fact]
    public void Medio_leido_dos_veces_da_lo_mismo()
    {
        var elemento = ElementoFalso.Con(("posterImage", ElementoFalso.Medio("/media/p.jpg", "Póster")));
        var lector = Lector(elemento);

        Assert.Equal(lector.Medio("posterImage"), lector.Medio("posterImage"));
    }

    [Fact]
    public void Medio_sin_proveedor_de_urls_es_un_defecto_del_resolver_y_lanza()
    {
        var lector = new LectorDelEditor(ElementoFalso.Con(("audioFile", ElementoFalso.Medio("/a.mp3"))), ElementoFalso.Fallback);

        Assert.Throws<InvalidOperationException>(() => lector.Medio("audioFile"));
    }

    // ── Enlace (Umbraco.MultiUrlPicker) ─────────────────────────────────────────────────────

    [Fact]
    public void Enlace_sin_poner_no_viaja()
    {
        Assert.Null(Lector(ElementoFalso.Con()).Enlace("ctaLink"));
        Assert.Equal(0, Anotados());
    }

    [Fact]
    public void Enlace_puesto_viaja_con_su_destino_su_texto_y_donde_abre()
    {
        var leido = Lector(ElementoFalso.Con(("policyLink", ElementoFalso.Enlace(" /privacidad ", " Política de privacidad ", "_blank"))))
            .Enlace("policyLink");

        Assert.Equal(new EnlaceDelEditor("/privacidad", "Política de privacidad", "_blank"), leido);
    }

    [Fact]
    public void Enlace_sin_texto_ni_ventana_nueva_viaja_solo_con_su_destino()
    {
        var leido = Lector(ElementoFalso.Con(("actionLink", ElementoFalso.Enlace("https://wa.me/573001234567", "", null))))
            .Enlace("actionLink");

        Assert.Equal(new EnlaceDelEditor("https://wa.me/573001234567", null, null), leido);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" # ")]
    public void Enlace_sin_destino_no_viaja_y_se_anota(string url)
    {
        Assert.Null(Lector(ElementoFalso.Con(("shareLink", ElementoFalso.Enlace(url, "Compartir")))).Enlace("shareLink"));
        Assert.Equal(1, Anotados());
    }

    [Fact]
    public void Enlace_que_llega_como_lista_toma_el_primero()
    {
        IEnumerable<Link> varios = [ElementoFalso.Enlace("/uno"), ElementoFalso.Enlace("/dos")];

        Assert.Equal("/uno", Lector(ElementoFalso.Con(("ctaLink", varios))).Enlace("ctaLink")?.Url);
    }

    [Fact]
    public void Enlace_leido_dos_veces_da_lo_mismo()
    {
        var lector = Lector(ElementoFalso.Con(("ctaLink", ElementoFalso.Enlace("/reservas", "Reservar"))));

        Assert.Equal(lector.Enlace("ctaLink"), lector.Enlace("ctaLink"));
    }

    // ── Opciones (Umbraco.DropDown.Flexible múltiple) ───────────────────────────────────────

    [Fact]
    public void Opciones_sin_marcar_no_viajan()
    {
        Assert.Null(Lector(ElementoFalso.Con()).Opciones("platforms"));
        Assert.Null(Lector(ElementoFalso.Con(("platforms", Array.Empty<string>()))).Opciones("platforms"));
    }

    [Fact]
    public void Opciones_marcadas_viajan_como_lista_en_su_orden()
    {
        var leidas = Lector(ElementoFalso.Con(("platforms", new[] { "whatsapp", "twitter", "email" }))).Opciones("platforms");

        Assert.Equal(new[] { "whatsapp", "twitter", "email" }, leidas);
    }

    [Fact]
    public void Opciones_vacias_o_repetidas_no_viajan_dos_veces()
    {
        var leidas = Lector(ElementoFalso.Con(("platforms", new[] { " whatsapp ", "", "  ", "whatsapp", "email" }))).Opciones("platforms");

        Assert.Equal(new[] { "whatsapp", "email" }, leidas);
    }

    [Fact]
    public void Un_desplegable_simple_se_lee_como_una_lista_de_una()
    {
        Assert.Equal(new[] { "bottom-left" }, Lector(ElementoFalso.Con(("position", "bottom-left"))).Opciones("position"));
        Assert.Null(Lector(ElementoFalso.Con(("position", ""))).Opciones("position"));
    }

    [Fact]
    public void Opciones_leidas_dos_veces_dan_lo_mismo()
    {
        var lector = Lector(ElementoFalso.Con(("platforms", new[] { "facebook", "linkedin" })));

        Assert.Equal(lector.Opciones("platforms"), lector.Opciones("platforms"));
    }

    // ── Numero y Entero: lo que escribe un editor es-CO ────────────────────────────────────
    //
    // El punto es el separador de MILES del editor es-CO y la coma el decimal. Leer «500.000» con
    // la regla invariante da 500 —mil veces menos, en silencio—: la trampa que el motor de
    // catálogo ya pagó con «49.000». Lo inequívoco se lee; lo que admite dos lecturas no viaja y
    // se anota, en vez de adivinar.

    [Theory]
    [InlineData("4", 4)]
    [InlineData("-3", -3)]
    [InlineData("1234", 1234)]
    [InlineData("4,5", 4.5)]
    [InlineData("12,50", 12.5)]
    [InlineData("4.5", 4.5)]
    [InlineData("3.1416", 3.1416)]
    [InlineData("1.234.567", 1234567)]
    [InlineData("1.234,5", 1234.5)]
    [InlineData("1,234,567", 1234567)]
    [InlineData("1,234.5", 1234.5)]
    public void Numero_inequivoco_viaja_con_su_valor(string texto, double esperado)
    {
        Assert.Equal((decimal)esperado, Lector(ElementoFalso.Con(("valueNow", texto))).Numero("valueNow"));
        Assert.Equal(0, Anotados());
    }

    [Theory]
    [InlineData("500.000")]
    [InlineData("1.234")]
    [InlineData("1,234")]
    public void Numero_con_dos_lecturas_no_viaja_y_se_anota_en_vez_de_adivinar(string texto)
    {
        Assert.Null(Lector(ElementoFalso.Con(("valueNow", texto))).Numero("valueNow"));
        Assert.Contains("una sola lectura", Assert.Single(Anotaciones()), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("cuatro")]
    [InlineData("4.5.6")]
    [InlineData("1.23.456")]
    [InlineData("4,")]
    public void Numero_que_no_es_numero_no_viaja_y_se_anota(string texto)
    {
        Assert.Null(Lector(ElementoFalso.Con(("valueNow", texto))).Numero("valueNow"));
        Assert.Equal(1, Anotados());
    }

    [Theory]
    [InlineData("5", 5)]
    [InlineData("4000", 4000)]
    [InlineData("-2", -2)]
    [InlineData("1.000.000", 1000000)]
    public void Entero_inequivoco_viaja_con_su_valor(string texto, int esperado)
    {
        Assert.Equal(esperado, Lector(ElementoFalso.Con(("maxStars", texto))).Entero("maxStars"));
        Assert.Equal(0, Anotados());
    }

    [Theory]
    [InlineData("5.000")]
    [InlineData("5.5")]
    [InlineData("4,5")]
    [InlineData("99999999999")]
    [InlineData("cinco")]
    public void Entero_ambiguo_decimal_o_fuera_de_rango_no_viaja_y_se_anota(string texto)
    {
        Assert.Null(Lector(ElementoFalso.Con(("maxStars", texto))).Entero("maxStars"));
        Assert.Equal(1, Anotados());
    }

    // ── TextoPlano (Umbraco.TinyMCE) ────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("<p></p>")]
    [InlineData("<p>&nbsp;</p>")]
    public void TextoPlano_sin_texto_no_viaja(string? html)
    {
        Assert.Null(Lector(ElementoFalso.Con(("tooltipContent", html))).TextoPlano("tooltipContent"));
    }

    [Fact]
    public void TextoPlano_quita_el_marcado_y_no_pega_los_parrafos()
    {
        var leido = Lector(ElementoFalso.Con(("tooltipContent",
            new HtmlEncodedString("<p>El IVA se calcula <strong>sobre el total</strong>.</p><p>Aplica desde<br/>enero.</p>"))))
            .TextoPlano("tooltipContent");

        Assert.Equal("El IVA se calcula sobre el total. Aplica desde enero.", leido);
    }

    [Fact]
    public void TextoPlano_decodifica_las_entidades_y_descarta_scripts_y_estilos()
    {
        var leido = Lector(ElementoFalso.Con(("tooltipContent",
            "<p>Tama&ntilde;o &lt;b&gt; &amp; m&aacute;s</p><script>alert(1)</script><style>p{color:red}</style>")))
            .TextoPlano("tooltipContent");

        Assert.Equal("Tamaño <b> & más", leido);
    }

    [Fact]
    public void TextoPlano_leido_dos_veces_da_lo_mismo()
    {
        var lector = Lector(ElementoFalso.Con(("tooltipContent", "<ul><li>Uno</li><li>Dos</li></ul>")));

        Assert.Equal("Uno Dos", lector.TextoPlano("tooltipContent"));
        Assert.Equal(lector.TextoPlano("tooltipContent"), lector.TextoPlano("tooltipContent"));
    }

    // ── NumeroDe: un número dentro de un ítem de una lista JSON ─────────────────────────────

    private static JsonElement Item(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Theory]
    [InlineData("""{"lat": 4.6097}""", "lat", 4.6097)]
    [InlineData("""{"lng": -74.0817}""", "lng", -74.0817)]
    [InlineData("""{"value": 120}""", "value", 120)]
    [InlineData("""{"lat": "4,6097"}""", "lat", 4.6097)]
    [InlineData("""{"lng": "-74.0817"}""", "lng", -74.0817)]
    [InlineData("""{"value": "1.234.567"}""", "value", 1234567)]
    [InlineData("""{"value": " 12,5 "}""", "value", 12.5)]
    public void NumeroDe_un_item_se_lee_como_numero_json_o_con_la_lectura_es_CO(string json, string clave, double esperado)
    {
        Assert.Equal((decimal)esperado, Lector(ElementoFalso.Con()).NumeroDe("pinsJson", Item(json), clave));
        Assert.Equal(0, Anotados());
    }

    [Theory]
    [InlineData("""{"title": "Sin coordenadas"}""")]
    [InlineData("""{"lat": null}""")]
    [InlineData("""{"lat": ""}""")]
    [InlineData("""["no es un objeto"]""")]
    public void NumeroDe_un_item_sin_el_campo_no_viaja_y_no_se_anota(string json)
    {
        Assert.Null(Lector(ElementoFalso.Con()).NumeroDe("pinsJson", Item(json), "lat"));
        Assert.Equal(0, Anotados());
    }

    [Theory]
    [InlineData("""{"value": "500.000"}""")]
    [InlineData("""{"value": "ciento veinte"}""")]
    [InlineData("""{"value": true}""")]
    [InlineData("""{"value": {"n": 1}}""")]
    public void NumeroDe_un_item_ambiguo_o_que_no_es_numero_no_viaja_y_se_anota(string json)
    {
        Assert.Null(Lector(ElementoFalso.Con()).NumeroDe("dataJson", Item(json), "value"));
        Assert.Equal(1, Anotados());
    }

    [Fact]
    public void NumeroDe_un_item_ambiguo_dice_como_escribirlo()
    {
        Assert.Null(Lector(ElementoFalso.Con()).NumeroDe("dataJson", Item("""{"value": "1.234"}"""), "value"));
        Assert.Contains("una sola lectura", Assert.Single(Anotaciones()), StringComparison.Ordinal);
    }

    [Fact]
    public void NumeroDe_un_item_leido_dos_veces_da_lo_mismo()
    {
        var lector = Lector(ElementoFalso.Con());
        var item = Item("""{"lat": "4,6097"}""");

        Assert.Equal(lector.NumeroDe("pinsJson", item, "lat"), lector.NumeroDe("pinsJson", item, "lat"));
    }

    // ── Cadena: un ítem de una lista JSON que ES una cadena ─────────────────────────────────

    [Theory]
    [InlineData("\"#ff6600\"", "#ff6600")]
    [InlineData("\"  #0066FF \"", "#0066FF")]
    public void Un_item_que_es_una_cadena_se_lee_recortado(string json, string esperado)
    {
        Assert.Equal(esperado, LectorDelEditor.Cadena(Item(json)));
    }

    [Theory]
    [InlineData("\"\"")]
    [InlineData("\"   \"")]
    [InlineData("42")]
    [InlineData("null")]
    [InlineData("""{"hex": "#fff"}""")]
    public void Un_item_que_no_es_una_cadena_con_texto_no_viaja(string json)
    {
        Assert.Null(LectorDelEditor.Cadena(Item(json)));
    }

    [Fact]
    public void Un_item_cadena_leido_dos_veces_da_lo_mismo()
    {
        var item = Item("\"#123abc\"");

        Assert.Equal(LectorDelEditor.Cadena(item), LectorDelEditor.Cadena(item));
    }

    // ── FechaIso (un TextBox con ISO 8601) ──────────────────────────────────────────────────

    [Theory]
    [InlineData("2026-12-31")]
    [InlineData("2026-12-31T23:59")]
    [InlineData("2026-12-31T23:59:59")]
    [InlineData("2026-12-31T23:59:59Z")]
    [InlineData("2026-12-31T23:59:59-05:00")]
    [InlineData("2026-12-31T23:59:59.500Z")]
    public void Una_fecha_ISO_viaja_tal_como_la_escribio_el_editor(string texto)
    {
        Assert.Equal(texto, Lector(ElementoFalso.Con(("endDateTime", "  " + texto + " "))).FechaIso("endDateTime"));
        Assert.Equal(0, Anotados());
    }

    [Theory]
    [InlineData("31/12/2026")]
    [InlineData("2026-02-30")]
    [InlineData("2026-13-01")]
    [InlineData("2026-12-31 23:59")]
    [InlineData("2026-12-31T25:00:00Z")]
    [InlineData("mañana a las 8")]
    public void Lo_que_no_es_una_fecha_ISO_no_viaja_y_se_anota(string texto)
    {
        Assert.Null(Lector(ElementoFalso.Con(("endDateTime", texto))).FechaIso("endDateTime"));
        Assert.Contains("ISO 8601", Assert.Single(Anotaciones()), StringComparison.Ordinal);
    }

    [Fact]
    public void Una_fecha_vacia_no_viaja_y_no_se_anota()
    {
        Assert.Null(Lector(ElementoFalso.Con(("endDateTime", "   "))).FechaIso("endDateTime"));
        Assert.Equal(0, Anotados());
    }

    [Fact]
    public void Una_fecha_leida_dos_veces_da_lo_mismo()
    {
        var lector = Lector(ElementoFalso.Con(("endDateTime", "2026-12-31T23:59:59Z")));

        Assert.Equal(lector.FechaIso("endDateTime"), lector.FechaIso("endDateTime"));
    }
}
