using System.Globalization;
using System.Text.RegularExpressions;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Tests.Proxies;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Ata el fallback SSR de los ocho blocks <c>elementSyn*</c> con contenido
/// editorial (hero, FAQ, feature grid, media+text, testimonios, quote, stat,
/// KPI).
/// </summary>
/// <remarks>
/// <para>El bug que estos tests cierran: <c>SynHostEmitRequest.FallbackHtml</c>
/// existía y <c>DefaultSynHostEmitter</c> lo honraba, pero NINGUNO de los 87
/// partials de <c>Views/Partials/SynHost/</c> lo pasaba. Como
/// <c>HttpBundleRegistryClient</c> todavía no existe (ADR 0012), en cualquier
/// deploy sin CDN local el registry nunca resuelve y cada block se renderizaba
/// como un <c>&lt;div aria-hidden&gt;</c> vacío: el contenido estaba en el CMS,
/// la página se renderizaba en el servidor, y no se veía nada. Los buscadores
/// tampoco veían nada.</para>
/// <para>Ninguna de esas tres cosas —marcador de upgrade, degradación del JSON
/// malformado, labels desde el Dictionary— tenía test. Por eso el hueco duró.</para>
/// </remarks>
public sealed class SynHostFallbackBuilderTests
{
    private const string Faqs =
        """[{"question":"¿Hay envío gratis?","answer":"Sí, desde $150.000."}]""";

    private const string Features =
        """[{"heading":"Rápido","body":"Carga en menos de un segundo."},{"heading":"Seguro","body":"Pagos con PSE."}]""";

    private const string Testimonials =
        """[{"name":"Ana Ruiz","role":"Gerente","quote":"Nos cambió la operación.","avatarSrc":"https://ejemplo.co/a.png"}]""";

    // ── El marcador de upgrade ───────────────────────────────────────

    [Theory]
    [MemberData(nameof(TodosLosFallbacks))]
    public void Todo_fallback_cuelga_de_una_sola_raiz_marcada_para_el_bundle(string html)
    {
        // El contrato con el bundle: remueve o reemplaza [data-syn-ssr-fallback]
        // al upgradear. Si hubiera dos raíces marcadas, el bundle removería una
        // y la otra quedaría duplicando el render.
        var occurrences = CountOccurrences(html, SynHostFallbackBuilder.MarkerAttribute);
        Assert.Equal(1, occurrences);
        Assert.StartsWith("<", html, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(TodosLosFallbacks))]
    public void Ningun_fallback_se_esconde_de_los_lectores_ni_de_los_buscadores(string html)
    {
        // Este markup existe justamente para ser indexado. El placeholder vacío
        // de DefaultSynHostEmitter sí lleva aria-hidden; este no puede.
        Assert.DoesNotContain("aria-hidden", html, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [MemberData(nameof(TodosLosFallbacks))]
    public void Ningun_fallback_trae_estilo_inline_ni_color_hardcodeado(string html)
    {
        // COMPONER-NUNCA-HARDCODEAR: la pinta vive en syn-ssr-fallback.css.
        Assert.DoesNotContain("style=", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch(new Regex("#[0-9a-fA-F]{3,8}\\b"), html);
        Assert.DoesNotContain("rgb(", html, StringComparison.OrdinalIgnoreCase);
    }

    public static TheoryData<string> TodosLosFallbacks()
    {
        var data = new TheoryData<string>();
        foreach (var html in EveryFallback()) data.Add(html);
        return data;
    }

    private static IEnumerable<string> EveryFallback()
    {
        yield return SynHostFallbackBuilder.HeroBanner(
            "Bienvenido", "Todo en un solo lugar", "/media/hero.jpg", "Vista de la sede",
            "Ver planes", "/planes", "Sección destacada")!;
        yield return SynHostFallbackBuilder.FaqSection("Preguntas", Faqs, "light", "Preguntas frecuentes")!;
        yield return SynHostFallbackBuilder.FeatureGrid("Ventajas", Features, "2", "dark", "Características")!;
        yield return SynHostFallbackBuilder.MediaText(
            "Nuestra historia", "Nacimos en Bogotá.\nHoy estamos en cinco ciudades.",
            "/media/foto.jpg", "Equipo fundador", "right", "light", "Contenido con imagen")!;
        yield return SynHostFallbackBuilder.TestimonialSection("Clientes", Testimonials, null, "Testimonios")!;
        yield return SynHostFallbackBuilder.QuoteAnimated("La constancia gana.", "Refrán")!;
        yield return SynHostFallbackBuilder.StatTicker("1200", "Clientes activos", "+", "k")!;
        yield return SynHostFallbackBuilder.KpiCard(
            "Ventas", "$12.400.000", "up", "8,2%", "vs. mes anterior", "Tendencia al alza")!;
    }

    // ── Semántica y contenido ────────────────────────────────────────

    [Fact]
    public void El_hero_emite_titulo_subtitulo_imagen_y_CTA_navegable()
    {
        var html = SynHostFallbackBuilder.HeroBanner(
            title: "Bienvenido",
            subtitle: "Todo en un solo lugar",
            imageUrl: "/media/hero.jpg",
            imageAlt: "Vista de la sede",
            ctaLabel: "Ver planes",
            ctaUrl: "/planes",
            ariaLabel: "IRRELEVANTE");

        Assert.NotNull(html);
        Assert.Contains("<section", html);
        Assert.Contains("<h2 class=\"syn-ssr-hero__title\">Bienvenido</h2>", html);
        Assert.Contains("Todo en un solo lugar", html);
        Assert.Contains("<img class=\"syn-ssr-hero__media\" src=\"/media/hero.jpg\" alt=\"Vista de la sede\"", html);
        // Un CTA sin href no es un CTA: tiene que ser un enlace real.
        Assert.Contains("<a class=\"syn-ssr-hero__cta\" href=\"/planes\">Ver planes</a>", html);
    }

    [Fact]
    public void El_hero_sin_CTA_completo_no_emite_un_enlace_a_ninguna_parte()
    {
        var html = SynHostFallbackBuilder.HeroBanner(
            "Bienvenido", null, null, null, "Ver planes", null, null);

        Assert.NotNull(html);
        Assert.DoesNotContain("<a ", html);
    }

    [Fact]
    public void El_hero_toma_el_titulo_como_nombre_accesible_y_solo_cae_al_Dictionary_sin_titulo()
    {
        var conTitulo = SynHostFallbackBuilder.HeroBanner(
            "Bienvenido", null, null, null, null, null, "LABEL-DEL-DICTIONARY");
        var sinTitulo = SynHostFallbackBuilder.HeroBanner(
            null, "Todo en un solo lugar", null, null, null, null, "LABEL-DEL-DICTIONARY");

        Assert.Contains("aria-label=\"Bienvenido\"", conTitulo);
        Assert.Contains("aria-label=\"LABEL-DEL-DICTIONARY\"", sinTitulo);
    }

    [Fact]
    public void El_FAQ_emite_una_lista_de_definicion_real()
    {
        var html = SynHostFallbackBuilder.FaqSection("Preguntas", Faqs, null, "aria");

        Assert.NotNull(html);
        Assert.Contains("<dl class=\"syn-ssr-faq__list\">", html);
        Assert.Contains("<dt class=\"syn-ssr-faq__question\">¿Hay envío gratis?</dt>", html);
        Assert.Contains("<dd class=\"syn-ssr-faq__answer\">Sí, desde $150.000.</dd>", html);
    }

    [Fact]
    public void El_feature_grid_emite_un_item_por_feature_con_su_encabezado()
    {
        var html = SynHostFallbackBuilder.FeatureGrid("Ventajas", Features, "2", null, "aria");

        Assert.NotNull(html);
        Assert.Equal(2, CountOccurrences(html!, "<li class=\"syn-ssr-features__item\">"));
        Assert.Contains("<h3 class=\"syn-ssr-features__title\">Rápido</h3>", html);
        Assert.Contains("Pagos con PSE.", html);
        Assert.Contains("syn-ssr-features--cols-2", html);
    }

    [Theory]
    [InlineData("7")]
    [InlineData("0")]
    [InlineData("dos")]
    [InlineData("")]
    public void Un_numero_de_columnas_fuera_del_rango_no_inventa_una_clase_sin_CSS(string columns)
    {
        var html = SynHostFallbackBuilder.FeatureGrid(null, Features, columns, null, "aria");

        Assert.NotNull(html);
        Assert.DoesNotContain("syn-ssr-features--cols-", html);
    }

    [Fact]
    public void El_media_text_envuelve_la_imagen_en_figure_y_parte_el_cuerpo_en_parrafos()
    {
        var html = SynHostFallbackBuilder.MediaText(
            heading: "Nuestra historia",
            body: "Nacimos en Bogotá.\n\nHoy estamos en cinco ciudades.",
            imageUrl: "/media/foto.jpg",
            imageAlt: "Equipo fundador",
            mediaPosition: "right",
            theme: null,
            ariaLabel: "aria");

        Assert.NotNull(html);
        Assert.Contains("<figure class=\"syn-ssr-media-text__figure\">", html);
        Assert.Contains("alt=\"Equipo fundador\"", html);
        Assert.Equal(2, CountOccurrences(html!, "<p class=\"syn-ssr-media-text__text\">"));
        Assert.Contains("syn-ssr-media-text--media-right", html);
    }

    [Fact]
    public void Los_testimonios_salen_como_blockquote_con_su_atribucion()
    {
        var html = SynHostFallbackBuilder.TestimonialSection("Clientes", Testimonials, null, "aria");

        Assert.NotNull(html);
        Assert.Contains("<blockquote class=\"syn-ssr-testimonials__quote\">", html);
        Assert.Contains("Nos cambió la operación.", html);
        Assert.Contains("Ana Ruiz", html);
        Assert.Contains("Gerente", html);
    }

    [Fact]
    public void El_avatar_del_testimonio_NO_se_renderiza_en_el_fallback()
    {
        // avatarSrc es una URL cruda tecleada por el editor; el fallback no
        // tiene por qué disparar fetches a hosts arbitrarios.
        var html = SynHostFallbackBuilder.TestimonialSection(null, Testimonials, null, "aria");

        Assert.NotNull(html);
        Assert.DoesNotContain("ejemplo.co", html);
        Assert.DoesNotContain("<img", html);
    }

    [Fact]
    public void El_quote_animado_cae_al_quote_estatico()
    {
        var html = SynHostFallbackBuilder.QuoteAnimated("La constancia gana.", "Refrán");

        Assert.NotNull(html);
        Assert.Contains("<figure", html);
        Assert.Contains("La constancia gana.", html);
        Assert.Contains("<figcaption class=\"syn-ssr-quote__attribution\">Refrán</figcaption>", html);
    }

    [Fact]
    public void El_stat_ticker_cae_a_la_cifra_final_con_prefijo_y_sufijo()
    {
        var html = SynHostFallbackBuilder.StatTicker("1200", "Clientes activos", "+", "k");

        Assert.NotNull(html);
        Assert.Contains("1200", html);
        Assert.Contains("<span class=\"syn-ssr-stat__affix\">+</span>", html);
        Assert.Contains("<span class=\"syn-ssr-stat__affix\">k</span>", html);
        Assert.Contains("<figcaption class=\"syn-ssr-stat__label\">Clientes activos</figcaption>", html);
    }

    [Fact]
    public void El_KPI_emite_label_y_valor_como_lista_de_definicion()
    {
        var html = SynHostFallbackBuilder.KpiCard(
            "Ventas", "$12.400.000", "up", "8,2%", "vs. mes anterior", "Tendencia al alza");

        Assert.NotNull(html);
        Assert.Contains("<dt class=\"syn-ssr-kpi__label\">Ventas</dt>", html);
        Assert.Contains("<dd class=\"syn-ssr-kpi__value\">$12.400.000</dd>", html);
        Assert.Contains("syn-ssr-kpi__trend--up", html);
    }

    [Theory]
    [InlineData("sideways")]
    [InlineData("UP ")]
    [InlineData(null)]
    public void Un_trend_desconocido_no_se_cuela_dentro_de_un_nombre_de_clase(string? trend)
    {
        var key = SynHostFallbackBuilder.TrendKey(trend);
        var html = SynHostFallbackBuilder.KpiCard("Ventas", "10", trend, null, null, "Etiqueta");

        Assert.NotNull(html);
        if (key is null)
        {
            Assert.DoesNotContain("syn-ssr-kpi__trend--", html);
        }
        else
        {
            Assert.Contains($"syn-ssr-kpi__trend--{key}", html);
        }
    }

    // ── Vacío: sin contenido no hay sección ──────────────────────────

    [Fact]
    public void Sin_ninguna_propiedad_diligenciada_no_se_emite_fallback()
    {
        Assert.Null(SynHostFallbackBuilder.HeroBanner(null, null, null, null, null, null, "aria"));
        Assert.Null(SynHostFallbackBuilder.FaqSection(null, null, null, "aria"));
        Assert.Null(SynHostFallbackBuilder.FeatureGrid(null, null, null, null, "aria"));
        Assert.Null(SynHostFallbackBuilder.MediaText(null, null, null, null, null, null, "aria"));
        Assert.Null(SynHostFallbackBuilder.TestimonialSection(null, null, null, "aria"));
        Assert.Null(SynHostFallbackBuilder.QuoteAnimated(null, null));
        Assert.Null(SynHostFallbackBuilder.StatTicker(null, null, "+", "k"));
        Assert.Null(SynHostFallbackBuilder.KpiCard(null, null, "up", "8%", "mes", "Tendencia al alza"));
    }

    [Fact]
    public void Un_encabezado_sin_items_tampoco_emite_una_seccion_vacia()
    {
        // Una <section> con título y nada adentro es ruido para el lector de
        // pantalla y para el crawler.
        Assert.Null(SynHostFallbackBuilder.FaqSection("Preguntas", "[]", null, "aria"));
        Assert.Null(SynHostFallbackBuilder.FeatureGrid("Ventajas", "[{}]", null, null, "aria"));
        Assert.Null(SynHostFallbackBuilder.TestimonialSection("Clientes", "[]", null, "aria"));
    }

    // ── itemsJson malformado: degradar, nunca reventar ───────────────

    [Theory]
    [InlineData("{ esto no es json")]
    [InlineData("[{\"question\": }]")]
    [InlineData("no es json en absoluto")]
    [InlineData("{\"question\":\"suelta\"}")]   // objeto, no array
    [InlineData("\"solo un string\"")]
    [InlineData("42")]
    [InlineData("null")]
    [InlineData("[1,2,3]")]                     // array sin objetos
    public void Un_itemsJson_malformado_degrada_a_sin_seccion_y_NO_revienta(string itemsJson)
    {
        // Una excepción en Razor se lleva la página entera. El blob lo teclea
        // el editor a mano, así que el camino malo es el camino esperado.
        var faq = Record.Exception(() => SynHostFallbackBuilder.FaqSection("Preguntas", itemsJson, null, "aria"));
        var features = Record.Exception(() => SynHostFallbackBuilder.FeatureGrid("Ventajas", itemsJson, "3", null, "aria"));
        var testimonials = Record.Exception(() => SynHostFallbackBuilder.TestimonialSection("Clientes", itemsJson, null, "aria"));

        Assert.Null(faq);
        Assert.Null(features);
        Assert.Null(testimonials);

        Assert.Null(SynHostFallbackBuilder.FaqSection("Preguntas", itemsJson, null, "aria"));
        Assert.Null(SynHostFallbackBuilder.FeatureGrid("Ventajas", itemsJson, "3", null, "aria"));
        Assert.Null(SynHostFallbackBuilder.TestimonialSection("Clientes", itemsJson, null, "aria"));
        Assert.Null(SynHostFallbackBuilder.TryParseItems(itemsJson));
    }

    [Fact]
    public void Un_itemsJson_con_items_buenos_y_basura_conserva_los_buenos()
    {
        var mixed = """[{"question":"Buena","answer":"Sí"}, 7, null, {"nada":""}]""";

        var html = SynHostFallbackBuilder.FaqSection(null, mixed, null, "aria");

        Assert.NotNull(html);
        Assert.Equal(1, CountOccurrences(html!, "<dt class=\"syn-ssr-faq__question\">"));
        Assert.Contains("Buena", html);
    }

    // ── Escaping ─────────────────────────────────────────────────────

    [Fact]
    public void El_texto_del_editor_se_escapa_porque_el_emitter_lo_inyecta_verbatim()
    {
        var html = SynHostFallbackBuilder.QuoteAnimated(
            "<script>alert('x')</script>", "Ana & Cía \"la firma\"");

        Assert.NotNull(html);
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("&amp;", html);
        Assert.Contains("&quot;", html);
    }

    [Fact]
    public void Una_URL_con_comillas_no_se_escapa_del_atributo()
    {
        var html = SynHostFallbackBuilder.HeroBanner(
            "T", null, "/media/\"><img src=x onerror=alert(1)>.jpg", "alt", null, null, "aria");

        Assert.NotNull(html);
        // El payload sigue ahí como TEXTO, pero no como markup: sin `"` ni `<`
        // sin escapar no hay forma de salirse del atributo.
        Assert.DoesNotContain("<img src=x", html);
        Assert.Contains("&quot;&gt;&lt;img src=x", html);
    }

    // ── Integración con el emitter (ADR 0015) ────────────────────────

    [Fact]
    public async Task Cuando_el_registry_NO_resuelve_el_fallback_reemplaza_al_placeholder_vacio()
    {
        var emitter = new DefaultSynHostEmitter(new FakeBundleRegistryClient());
        var fallback = SynHostFallbackBuilder.FaqSection("Preguntas", Faqs, null, "aria");

        var result = await emitter.EmitAsync(new SynHostEmitRequest(
            "faq-section", null, null, CultureInfo.GetCultureInfo("es-CO"), fallback));

        Assert.False(result.RegistryResolved);
        Assert.Contains("¿Hay envío gratis?", result.ElementHtml);
        Assert.Contains("data-syn-ssr-fallback", result.ElementHtml);
        // El div vacío con aria-hidden era exactamente el bug.
        Assert.DoesNotContain("syn-cdn-offline-fallback", result.ElementHtml);
    }

    [Fact]
    public async Task Cuando_el_registry_SI_resuelve_el_fallback_igual_va_en_el_light_DOM()
    {
        // Progressive enhancement, no duplicado: un Custom Element muestra sus
        // hijos hasta que el bundle lo upgradea, así que la ventana previa al
        // upgrade existe en TODA carga de página, con CDN o sin CDN.
        var descriptor = new BundleDescriptor(
            MainEntryUri: new Uri("https://cdn.example.com/synergos/faq-section/latest/main.js"),
            Dependencies: Array.Empty<Uri>(),
            Version: "0.1.0");
        var emitter = new DefaultSynHostEmitter(new FakeBundleRegistryClient(descriptor));
        var fallback = SynHostFallbackBuilder.FaqSection("Preguntas", Faqs, null, "aria");

        var result = await emitter.EmitAsync(new SynHostEmitRequest(
            "faq-section", null, null, CultureInfo.GetCultureInfo("es-CO"), fallback));

        Assert.True(result.RegistryResolved);
        Assert.Contains("data-syn-ssr-fallback", result.ElementHtml);
        Assert.Contains("¿Hay envío gratis?", result.ElementHtml);
        Assert.StartsWith("<synergos-faq-section", result.ElementHtml, StringComparison.Ordinal);
        Assert.EndsWith("</synergos-faq-section>", result.ElementHtml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sin_contenido_editorial_el_emitter_vuelve_al_placeholder_de_siempre()
    {
        var emitter = new DefaultSynHostEmitter(new FakeBundleRegistryClient());

        var result = await emitter.EmitAsync(new SynHostEmitRequest(
            "faq-section", null, null, CultureInfo.GetCultureInfo("es-CO"),
            SynHostFallbackBuilder.FaqSection(null, "{roto", null, "aria")));

        Assert.Contains("syn-cdn-offline-fallback", result.ElementHtml);
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = haystack.IndexOf(needle, StringComparison.Ordinal);
        while (index >= 0)
        {
            count++;
            index = haystack.IndexOf(needle, index + needle.Length, StringComparison.Ordinal);
        }
        return count;
    }
}
