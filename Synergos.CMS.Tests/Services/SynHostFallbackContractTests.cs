using System.Text.RegularExpressions;
using System.Xml.Linq;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Ata el fallback SSR a los dos artefactos que viven FUERA del C# y que un
/// <c>dotnet build</c> jamás mira: el CSS de <c>wwwroot/css</c> y las claves
/// del Dictionary de uSync.
/// </summary>
/// <remarks>
/// <para><b>Por qué hace falta.</b> El gate <c>tools/check-css-parity.mjs</c>
/// solo escanea <c>Views/**/*.cshtml</c>; las clases del fallback las emite
/// una clase C#, así que el gate no las ve. Y los labels generados salen del
/// Dictionary, que tampoco compila. Sin estos tests, borrar una regla CSS o
/// una clave de Dictionary sería verde en CI y roto en pantalla.</para>
/// <para>Los partials de <c>Views/Partials/SynHost/</c> son Razor y en este
/// proyecto compilan en runtime (<c>RazorCompileOnBuild=false</c>), así que
/// aquí se leen como texto.</para>
/// </remarks>
public sealed class SynHostFallbackContractTests
{
    /// <summary>Los ocho partials que ganaron fallback SSR.</summary>
    private static readonly string[] PartialsConFallback =
    {
        "HeroBanner", "FaqSection", "FeatureGrid", "MediaText",
        "TestimonialSection", "QuoteAnimated", "StatTicker", "KpiCard",
    };

    /// <summary>Claves de Dictionary que los partials resuelven en runtime.</summary>
    private static readonly string[] ClavesDeDictionary =
    {
        "Synhost.Hero.Aria",
        "Synhost.Faq.Aria",
        "Synhost.Features.Aria",
        "Synhost.MediaText.Aria",
        "Synhost.Testimonials.Aria",
        "Synhost.Kpi.Trend.Up",
        "Synhost.Kpi.Trend.Down",
        "Synhost.Kpi.Trend.Flat",
    };

    // ── CSS ──────────────────────────────────────────────────────────

    [Fact]
    public void Toda_clase_syn_ssr_que_emite_el_builder_tiene_regla_CSS()
    {
        var css = File.ReadAllText(RepoFile("Synergos.CMS.Web", "wwwroot", "css", "syn-ssr-fallback.css"));
        var conRegla = new HashSet<string>(
            Regex.Matches(StripComments(css), @"\.(syn-[a-z0-9_-]+)")
                 .Select(m => m.Groups[1].Value),
            StringComparer.Ordinal);

        var emitidas = new HashSet<string>(StringComparer.Ordinal);
        foreach (var html in TodoElMarkupPosible())
        {
            foreach (Match m in Regex.Matches(html, "class=\"([^\"]+)\""))
            {
                foreach (var cls in m.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (cls.StartsWith("syn-", StringComparison.Ordinal)) emitidas.Add(cls);
                }
            }
        }

        Assert.NotEmpty(emitidas);
        var huerfanas = emitidas.Where(c => !conRegla.Contains(c)).OrderBy(c => c, StringComparer.Ordinal).ToList();
        Assert.True(
            huerfanas.Count == 0,
            "Clases emitidas por SynHostFallbackBuilder sin regla en syn-ssr-fallback.css: "
            + string.Join(", ", huerfanas));
    }

    [Fact]
    public void El_CSS_del_fallback_se_carga_desde_el_layout()
    {
        var layout = File.ReadAllText(RepoFile("Synergos.CMS.Web", "Views", "_Layout.cshtml"));

        Assert.Contains("css/syn-ssr-fallback.css", layout, StringComparison.Ordinal);
    }

    [Fact]
    public void El_CSS_esconde_el_fallback_de_cada_tag_ya_upgradeado()
    {
        // Sin la regla `:defined` el block renderiza DOS veces mientras el
        // bundle no remueva el nodo por su cuenta.
        var css = File.ReadAllText(RepoFile("Synergos.CMS.Web", "wwwroot", "css", "syn-ssr-fallback.css"));

        foreach (var tag in new[]
                 {
                     "synergos-hero-banner", "synergos-faq-section", "synergos-feature-grid",
                     "synergos-media-text", "synergos-testimonial-section",
                     "synergos-quote-animated", "synergos-stat-ticker", "synergos-kpi-card",
                 })
        {
            Assert.Contains(
                $"{tag}:defined > [{SynHostFallbackBuilder.MarkerAttribute}]",
                css,
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public void El_CSS_del_fallback_no_hardcodea_color()
    {
        // COMPONER-NUNCA-HARDCODEAR: todo por tokens --syn-*.
        var css = StripComments(File.ReadAllText(
            RepoFile("Synergos.CMS.Web", "wwwroot", "css", "syn-ssr-fallback.css")));

        Assert.DoesNotMatch(new Regex("#[0-9a-fA-F]{3,8}\\b"), css);
        Assert.DoesNotContain("rgb(", css, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hsl(", css, StringComparison.OrdinalIgnoreCase);
    }

    // ── Dictionary ───────────────────────────────────────────────────

    [Fact]
    public void Cada_clave_de_Dictionary_existe_en_uSync_con_las_dos_traducciones()
    {
        var dir = RepoDir("Synergos.CMS.Web", "uSync", "v9", "Dictionary");

        foreach (var alias in ClavesDeDictionary)
        {
            var file = Path.Combine(dir, alias.ToLowerInvariant() + ".config");
            Assert.True(File.Exists(file), $"Falta el archivo de Dictionary para '{alias}': {file}");

            var xml = XDocument.Load(file).Root!;
            Assert.Equal(alias, xml.Attribute("Alias")!.Value);
            Assert.Equal("1", xml.Attribute("Level")!.Value);
            Assert.Equal("Synhost", xml.Element("Info")!.Element("Parent")!.Value);

            var translations = xml.Element("Translations")!.Elements("Translation")
                .ToDictionary(t => t.Attribute("Language")!.Value, t => t.Value, StringComparer.Ordinal);

            Assert.True(translations.TryGetValue("en-US", out var en) && en.Trim().Length > 0,
                $"'{alias}' no tiene traducción en-US.");
            Assert.True(translations.TryGetValue("es-CO", out var es) && es.Trim().Length > 0,
                $"'{alias}' no tiene traducción es-CO.");
            Assert.NotEqual(en, es);
        }
    }

    [Fact]
    public void El_grupo_raiz_Synhost_existe_como_nodo_de_nivel_cero()
    {
        var root = XDocument.Load(Path.Combine(
            RepoDir("Synergos.CMS.Web", "uSync", "v9", "Dictionary"), "synhost.config")).Root!;

        Assert.Equal("Synhost", root.Attribute("Alias")!.Value);
        Assert.Equal("0", root.Attribute("Level")!.Value);
        Assert.Empty(root.Element("Translations")!.Elements());
    }

    [Fact]
    public void Ninguna_clave_de_Dictionary_queda_declarada_sin_consumidor()
    {
        var partials = PartialsConFallback
            .Select(name => File.ReadAllText(RepoFile(
                "Synergos.CMS.Web", "Views", "Partials", "SynHost", name + ".cshtml")))
            .ToList();

        foreach (var alias in ClavesDeDictionary)
        {
            Assert.True(
                partials.Any(p => p.Contains($"GetDictionaryValue(\"{alias}\"", StringComparison.Ordinal)),
                $"La clave '{alias}' está en uSync pero ningún partial de SynHost la lee.");
        }
    }

    // ── Los partials ─────────────────────────────────────────────────

    [Fact]
    public void Los_ocho_partials_pasan_FallbackHtml_al_emitter()
    {
        // Este es EXACTAMENTE el defecto original: el parámetro existía y nadie
        // lo pasaba.
        foreach (var name in PartialsConFallback)
        {
            var partial = File.ReadAllText(RepoFile(
                "Synergos.CMS.Web", "Views", "Partials", "SynHost", name + ".cshtml"));

            Assert.Contains("FallbackHtml:", partial, StringComparison.Ordinal);
            Assert.Contains("SynHostFallbackBuilder.", partial, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Los_partials_documentan_el_contrato_de_upgrade_con_el_bundle()
    {
        foreach (var name in PartialsConFallback)
        {
            var partial = File.ReadAllText(RepoFile(
                "Synergos.CMS.Web", "Views", "Partials", "SynHost", name + ".cshtml"));

            Assert.Contains("[data-syn-ssr-fallback]", partial, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Ningun_partial_de_SynHost_hardcodea_un_label_generado()
    {
        // Los labels que NO escribe el editor solo pueden salir del Dictionary.
        // Si alguien inlinea el texto, la clave queda muerta y el sitio deja de
        // traducirse sin que nada se ponga rojo.
        foreach (var name in PartialsConFallback)
        {
            var partial = File.ReadAllText(RepoFile(
                "Synergos.CMS.Web", "Views", "Partials", "SynHost", name + ".cshtml"));

            foreach (var literal in new[]
                     {
                         "Preguntas frecuentes", "Características", "Testimonios",
                         "Contenido con imagen", "Sección destacada",
                         "Tendencia al alza", "Tendencia a la baja", "Tendencia estable",
                     })
            {
                var index = partial.IndexOf(literal, StringComparison.Ordinal);
                if (index < 0) continue;

                // Único uso permitido: el altText de GetDictionaryValue, que es
                // el default cuando la clave todavía no se importó al DB.
                var before = partial[..index];
                var call = before.LastIndexOf("GetDictionaryValue(", StringComparison.Ordinal);
                Assert.True(
                    call >= 0 && !before[call..].Contains(')'),
                    $"{name}.cshtml usa el literal '{literal}' fuera de GetDictionaryValue.");
            }
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static IEnumerable<string> TodoElMarkupPosible()
    {
        const string faqs = """[{"question":"P","answer":"R"}]""";
        const string features = """[{"heading":"H","body":"B"}]""";
        const string testimonials = """[{"name":"N","role":"R","quote":"Q"}]""";

        foreach (var theme in new string?[] { null, "light", "dark" })
        {
            yield return SynHostFallbackBuilder.FaqSection("H", faqs, theme, "a")!;
            yield return SynHostFallbackBuilder.TestimonialSection("H", testimonials, theme, "a")!;
            yield return SynHostFallbackBuilder.MediaText("H", "B", "/m.jpg", "alt", "right", theme, "a")!;
            yield return SynHostFallbackBuilder.MediaText("H", "B", "/m.jpg", "alt", "left", theme, "a")!;
            foreach (var cols in new[] { "1", "2", "3", "4" })
                yield return SynHostFallbackBuilder.FeatureGrid("H", features, cols, theme, "a")!;
        }

        yield return SynHostFallbackBuilder.HeroBanner("T", "S", "/m.jpg", "alt", "CTA", "/x", "a")!;
        yield return SynHostFallbackBuilder.QuoteAnimated("Q", "A")!;
        yield return SynHostFallbackBuilder.StatTicker("1", "L", "+", "%")!;
        foreach (var trend in new[] { "up", "down", "flat" })
            yield return SynHostFallbackBuilder.KpiCard("L", "V", trend, "D", "P", "Etiqueta")!;
    }

    private static string StripComments(string css) =>
        Regex.Replace(css, @"/\*[\s\S]*?\*/", string.Empty);

    private static string RepoFile(params string[] parts) => RepoPath(parts, expectFile: true);

    private static string RepoDir(params string[] parts) => RepoPath(parts, expectFile: false);

    private static string RepoPath(string[] parts, bool expectFile)
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && dir is not null; i++)
        {
            var candidate = Path.Combine(new[] { dir }.Concat(parts).ToArray());
            if (expectFile ? File.Exists(candidate) : Directory.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }

        throw new FileNotFoundException(
            $"No se encontró {string.Join('/', parts)} subiendo desde {AppContext.BaseDirectory}");
    }
}
