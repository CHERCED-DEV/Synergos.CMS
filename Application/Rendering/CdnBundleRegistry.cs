namespace Synergos.CMS.Application.Rendering;

/// <summary>
/// Static mapping of element-type aliases to CDN bundle names.
///
/// Only elements whose SSR partial emits a <c>&lt;script type="module"&gt;</c>
/// need to appear here. Pure SSR elements (Card, Hero, Section, Column…)
/// and macro-only entries (CdnBanner, CdnLogoCloud via <c>elementIntMacroHost</c>)
/// are resolved separately.
///
/// Used by <see cref="BundlePrescanner"/> to emit
/// <c>&lt;link rel="modulepreload"&gt;</c> hints in the page <c>&lt;head&gt;</c>
/// before the browser discovers the scripts mid-body — measurable TTI win.
///
/// Keep in sync with the corresponding Razor partials under
/// <c>Views/Partials/Ssr/Components/</c>. When an element type gains a CDN
/// bundle, add its alias here.
/// </summary>
public static class CdnBundleRegistry
{
    private static readonly IReadOnlyDictionary<string, string> _aliasToBundle =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // Interactive typed containers (JSON aggregation + single script)
            ["elementCompTestimonialCarousel"] = "testimonial-carousel",
            ["elementCompAccordionGroup"]      = "accordion-group",

            // MacroHost: the backing CDN element is determined at runtime by
            // the editor's macroAlias pick — resolved separately by the scanner.
        };

    /// <summary>
    /// Resolves an element alias to its CDN bundle name, or null if the
    /// element doesn't ship a module script from its SSR partial.
    /// </summary>
    public static string? ResolveBundle(string? alias) =>
        alias is not null && _aliasToBundle.TryGetValue(alias, out var bundle) ? bundle : null;

    /// <summary>
    /// Macro alias → bundle name, for elements of type <c>elementIntMacroHost</c>
    /// whose editor-picked <c>macroAlias</c> selects the bundle at runtime.
    /// Convention: strip leading "cdn" and kebab-case the rest.
    /// </summary>
    public static string? ResolveMacroBundle(string? macroAlias)
    {
        if (string.IsNullOrWhiteSpace(macroAlias)) return null;
        if (!macroAlias.StartsWith("cdn", StringComparison.Ordinal)) return null;

        // "cdnTestimonialItem" → "testimonial-item"
        var tail = macroAlias[3..];
        if (tail.Length == 0) return null;

        var sb = new System.Text.StringBuilder(tail.Length + 6);
        sb.Append(char.ToLowerInvariant(tail[0]));
        for (var i = 1; i < tail.Length; i++)
        {
            var c = tail[i];
            if (char.IsUpper(c))
            {
                sb.Append('-');
                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }
}
