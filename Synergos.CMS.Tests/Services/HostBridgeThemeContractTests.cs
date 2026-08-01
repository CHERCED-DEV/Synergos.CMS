using System.Text.Json;
using System.Text.RegularExpressions;
using Synergos.CMS.Application.Constants;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Ata el contrato de identidad de ADR 0101 —
/// <c>pageThemeVariant</c> ↔ <c>data-theme</c> ↔ bloque de
/// <c>syn-tokens.css</c>, el mismo string verbatim— al lado JS del puente.
/// </summary>
/// <remarks>
/// El bug que estos tests cierran: el host bridge publicaba
/// <c>["light","dark","silvergold"]</c> escrito a mano en tres sitios. Tres
/// de las ocho variantes, y la tercera en una ortografía —todo-minúscula—
/// que no emite nadie: <c>_Layout</c> escribe <c>data-theme</c> verbatim
/// desde el editor, o sea <c>silverGold</c>. Consecuencia concreta: un
/// consumidor de <c>window.synergos</c> que hiciera
/// <c>available.includes(variant)</c> obtenía <c>false</c> justo cuando el
/// tema estaba activo, y los cinco temas de los verticales no existían para
/// la UI. El CSS estaba bien todo el tiempo; mentía el contrato tipado.
///
/// <para>Ninguno de los tres literales tenía test. Por eso el drift duró:
/// no había nada que se pusiera rojo.</para>
/// </remarks>
public sealed class HostBridgeThemeContractTests
{
    /// <summary>
    /// Sube desde el binario de test hasta la raíz del repo para leer el
    /// CSS real. Sin esto el test compararía la constante contra sí misma.
    /// </summary>
    private static string ReadTokensCss()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && dir is not null; i++)
        {
            var candidate = Path.Combine(
                dir, "Synergos.CMS.Web", "wwwroot", "css", "syn-tokens.css");
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }
            dir = Path.GetDirectoryName(dir);
        }

        throw new FileNotFoundException(
            "No se encontró syn-tokens.css subiendo desde " + AppContext.BaseDirectory);
    }

    [Fact]
    public void Every_published_variant_has_a_block_in_syn_tokens_css()
    {
        var css = ReadTokensCss();

        foreach (var variant in DropdownOptions.PageThemeVariant.All)
        {
            // "light" es el :root base y "brand" lo emite _BrandThemeStyle.cshtml
            // en runtime desde las props del siteRoot — ninguno de los dos vive
            // como bloque [data-theme=...] en el CSS estático.
            if (variant is DropdownOptions.PageThemeVariant.Light
                or DropdownOptions.PageThemeVariant.Brand)
            {
                continue;
            }

            Assert.True(
                css.Contains($"[data-theme=\"{variant}\"]", StringComparison.Ordinal),
                $"El bridge publica el tema '{variant}' pero syn-tokens.css no "
                + $"tiene un bloque [data-theme=\"{variant}\"]. La UI ofrecería "
                + "un tema que no pinta nada (ADR 0101).");
        }
    }

    [Fact]
    public void Every_data_theme_block_in_css_is_published_by_the_bridge()
    {
        var css = ReadTokensCss();

        var inCss = Regex
            .Matches(css, @"\[data-theme=""(?<v>[A-Za-z][A-Za-z0-9-]*)""\]")
            .Select(m => m.Groups["v"].Value)
            .Distinct(StringComparer.Ordinal)
            // "silver-gold" (kebab) es alias DEPRECADO conservado a propósito;
            // ADR 0101 §2 ratifica el camelCase como canónico.
            .Where(v => !v.Contains('-'))
            .ToList();

        var published = DropdownOptions.PageThemeVariant.All.ToHashSet(StringComparer.Ordinal);
        var missing = inCss.Where(v => !published.Contains(v)).ToList();

        Assert.True(
            missing.Count == 0,
            "syn-tokens.css define temas que el bridge no publica: "
            + string.Join(", ", missing)
            + ". Un editor puede elegirlos y la UI no sabe que existen.");
    }

    [Fact]
    public void SilverGold_is_camelCase_not_lowercase()
    {
        // El literal que se arregló decía "silvergold". Este test es la
        // trampa para que no vuelva por copiar-pegar.
        Assert.Contains("silverGold", DropdownOptions.PageThemeVariant.All);
        Assert.DoesNotContain("silvergold", DropdownOptions.PageThemeVariant.All);
    }

    [Fact]
    public void Inherit_is_not_published_as_an_available_theme()
    {
        // "inherit" es el centinela del resolver, no un tema. Publicarlo
        // haría que la UI ofreciera algo que no existe como bloque CSS.
        Assert.DoesNotContain(
            DropdownOptions.PageThemeVariant.Inherit,
            DropdownOptions.PageThemeVariant.All);
    }

    [Fact]
    public void Degraded_fallback_publishes_the_same_variant_list_as_the_happy_path()
    {
        // El camino degradado tenía su propia copia del literal y también
        // estaba rancia. Ahora deriva de la misma constante.
        Assert.Equal(
            DropdownOptions.PageThemeVariant.All,
            HostBridgeFallback.Context.Theme.Available);
    }

    [Fact]
    public void Degraded_fallback_variant_is_inside_its_own_available_list()
    {
        Assert.Contains(
            HostBridgeFallback.Context.Theme.Variant,
            HostBridgeFallback.Context.Theme.Available);
    }

    [Fact]
    public void Degraded_fallback_json_is_camelCase_and_parseable()
    {
        // Se inyecta con @Html.Raw dentro de un <script>, así que un JSON
        // malformado rompe la página entera, no solo el bridge.
        using var doc = JsonDocument.Parse(HostBridgeFallback.Json);

        var available = doc.RootElement
            .GetProperty("theme")
            .GetProperty("available")
            .EnumerateArray()
            .Select(e => e.GetString())
            .ToList();

        Assert.Equal(DropdownOptions.PageThemeVariant.All, available!);
    }
}
