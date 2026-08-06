using System.Text;
using System.Text.Json;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Builds the light-DOM SSR fallback markup that the content-bearing
/// <c>Views/Partials/SynHost/*.cshtml</c> partials hand to
/// <c>SynHostEmitRequest.FallbackHtml</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> <c>DefaultSynHostEmitter</c> has honoured
/// <c>FallbackHtml</c> since Cap-300, but no partial ever passed it. With
/// <c>HttpBundleRegistryClient</c> still unwritten (ADR 0012), every
/// deploy without a local CDN resolved no bundle and every
/// <c>elementSyn*</c> block collapsed to an empty <c>aria-hidden</c> div:
/// the editor's content sat in the CMS, the page was server-rendered, and
/// both humans and crawlers saw nothing.
/// </para>
/// <para>
/// <b>The contract with the bundle.</b> This markup is progressive
/// enhancement, not a duplicate render. Custom Elements display their
/// children until the definition upgrades them, so the fallback is emitted
/// on <i>every</i> page load — CDN online or not. Every fragment has a
/// single root element carrying <c>data-syn-ssr-fallback="true"</c>; the
/// bundle <b>removes or replaces</b> <c>[data-syn-ssr-fallback]</c> when it
/// upgrades the host, and until it does, the <c>:defined</c> rules in
/// <c>wwwroot/css/syn-ssr-fallback.css</c> hide it so nothing
/// double-renders.
/// </para>
/// <para>
/// <b>COMPONER-NUNCA-HARDCODEAR.</b> Styling is expressed only through
/// <c>syn-ssr-*</c> classes; no inline colour, spacing or hex ever leaves
/// this class. Every label that is not editor-authored arrives as a
/// parameter — the caller resolves it from the uSync Dictionary — so no
/// human-readable copy is hardcoded here.
/// </para>
/// <para>
/// <b>Defensive by construction.</b> <c>itemsJson</c> is editor-authored
/// free text: malformed JSON degrades to "no fallback section"
/// (<c>null</c>) and never throws, because a Razor exception takes the
/// whole page down. Every interpolated value is HTML-escaped — the emitter
/// injects this string verbatim.
/// </para>
/// </remarks>
public static class SynHostFallbackBuilder
{
    /// <summary>Attribute the bundle looks for to remove/replace the SSR node.</summary>
    public const string MarkerAttribute = "data-syn-ssr-fallback";

    private const string Marker = " data-syn-ssr-fallback=\"true\"";
    private const string BaseClass = "syn-ssr-fallback";

    // ── Hero banner ──────────────────────────────────────────────────

    /// <summary>
    /// <c>elementSynHeroBanner</c> — title/subtitle/media/CTA.
    /// </summary>
    /// <param name="ariaLabel">Dictionary-resolved name for the section,
    /// used only when the editor left the title empty.</param>
    public static string? HeroBanner(
        string? title,
        string? subtitle,
        string? imageUrl,
        string? imageAlt,
        string? ctaLabel,
        string? ctaUrl,
        string? ariaLabel)
    {
        title = Clean(title);
        subtitle = Clean(subtitle);
        imageUrl = Clean(imageUrl);
        ctaLabel = Clean(ctaLabel);
        ctaUrl = Clean(ctaUrl);

        if (title is null && subtitle is null && imageUrl is null && ctaLabel is null)
            return null;

        var sb = new StringBuilder();
        sb.Append("<section class=\"").Append(BaseClass).Append(" syn-ssr-hero\"")
          .Append(Marker)
          .Append(AriaLabel(title ?? ariaLabel))
          .Append('>');

        if (imageUrl is not null)
        {
            sb.Append("<img class=\"syn-ssr-hero__media\" src=\"").Append(Esc(imageUrl))
              .Append("\" alt=\"").Append(Esc(Clean(imageAlt) ?? title ?? string.Empty))
              .Append("\" />");
        }

        sb.Append("<div class=\"syn-ssr-hero__body\">");
        if (title is not null)
            sb.Append("<h2 class=\"syn-ssr-hero__title\">").Append(Esc(title)).Append("</h2>");
        if (subtitle is not null)
            sb.Append("<p class=\"syn-ssr-hero__subtitle\">").Append(Esc(subtitle)).Append("</p>");
        if (ctaLabel is not null && ctaUrl is not null)
        {
            sb.Append("<a class=\"syn-ssr-hero__cta\" href=\"").Append(Esc(ctaUrl)).Append("\">")
              .Append(Esc(ctaLabel)).Append("</a>");
        }
        sb.Append("</div></section>");
        return sb.ToString();
    }

    // ── FAQ section ──────────────────────────────────────────────────

    /// <summary>
    /// <c>elementSynFaqSection</c> — heading + <c>itemsJson</c> array of
    /// <c>{"question","answer"}</c> rendered as a definition list.
    /// </summary>
    public static string? FaqSection(
        string? heading,
        string? itemsJson,
        string? theme,
        string? ariaLabel)
    {
        var items = TryParseItems(itemsJson);
        if (items is null) return null;

        heading = Clean(heading);

        var sb = new StringBuilder();
        sb.Append("<section class=\"").Append(BaseClass).Append(" syn-ssr-faq")
          .Append(ThemeClass(theme)).Append('"')
          .Append(Marker)
          .Append(AriaLabel(heading ?? ariaLabel))
          .Append('>');

        if (heading is not null)
            sb.Append("<h2 class=\"syn-ssr-faq__heading\">").Append(Esc(heading)).Append("</h2>");

        sb.Append("<dl class=\"syn-ssr-faq__list\">");
        var rendered = 0;
        foreach (var item in items)
        {
            var question = Field(item, "question");
            var answer = Field(item, "answer");
            if (question is null && answer is null) continue;
            sb.Append("<dt class=\"syn-ssr-faq__question\">").Append(Esc(question ?? string.Empty)).Append("</dt>");
            sb.Append("<dd class=\"syn-ssr-faq__answer\">").Append(Esc(answer ?? string.Empty)).Append("</dd>");
            rendered++;
        }
        sb.Append("</dl></section>");

        return rendered == 0 ? null : sb.ToString();
    }

    // ── Feature grid ─────────────────────────────────────────────────

    /// <summary>
    /// <c>elementSynFeatureGrid</c> — heading + <c>itemsJson</c> array of
    /// <c>{"heading","body","icon"}</c>. The icon is intentionally dropped:
    /// icon names belong to the CDN bundle's sprite, not to the SSR shell.
    /// </summary>
    public static string? FeatureGrid(
        string? heading,
        string? itemsJson,
        string? columns,
        string? theme,
        string? ariaLabel)
    {
        var items = TryParseItems(itemsJson);
        if (items is null) return null;

        heading = Clean(heading);

        var sb = new StringBuilder();
        sb.Append("<section class=\"").Append(BaseClass).Append(" syn-ssr-features")
          .Append(ColumnsClass(columns)).Append(ThemeClass(theme)).Append('"')
          .Append(Marker)
          .Append(AriaLabel(heading ?? ariaLabel))
          .Append('>');

        if (heading is not null)
            sb.Append("<h2 class=\"syn-ssr-features__heading\">").Append(Esc(heading)).Append("</h2>");

        sb.Append("<ul class=\"syn-ssr-features__list\">");
        var rendered = 0;
        foreach (var item in items)
        {
            var itemHeading = Field(item, "heading") ?? Field(item, "title");
            var body = Field(item, "body");
            if (itemHeading is null && body is null) continue;
            sb.Append("<li class=\"syn-ssr-features__item\">");
            if (itemHeading is not null)
                sb.Append("<h3 class=\"syn-ssr-features__title\">").Append(Esc(itemHeading)).Append("</h3>");
            if (body is not null)
                sb.Append("<p class=\"syn-ssr-features__body\">").Append(Esc(body)).Append("</p>");
            sb.Append("</li>");
            rendered++;
        }
        sb.Append("</ul></section>");

        return rendered == 0 ? null : sb.ToString();
    }

    // ── Media + text ─────────────────────────────────────────────────

    /// <summary>
    /// <c>elementSynMediaText</c> — figure plus prose, side by side.
    /// </summary>
    public static string? MediaText(
        string? heading,
        string? body,
        string? imageUrl,
        string? imageAlt,
        string? mediaPosition,
        string? theme,
        string? ariaLabel)
    {
        heading = Clean(heading);
        imageUrl = Clean(imageUrl);
        var paragraphs = Paragraphs(body);

        if (heading is null && imageUrl is null && paragraphs.Count == 0)
            return null;

        var sb = new StringBuilder();
        sb.Append("<section class=\"").Append(BaseClass).Append(" syn-ssr-media-text")
          .Append(MediaPositionClass(mediaPosition)).Append(ThemeClass(theme)).Append('"')
          .Append(Marker)
          .Append(AriaLabel(heading ?? ariaLabel))
          .Append('>');

        if (imageUrl is not null)
        {
            sb.Append("<figure class=\"syn-ssr-media-text__figure\">")
              .Append("<img class=\"syn-ssr-media-text__image\" src=\"").Append(Esc(imageUrl))
              .Append("\" alt=\"").Append(Esc(Clean(imageAlt) ?? string.Empty)).Append("\" />")
              .Append("</figure>");
        }

        sb.Append("<div class=\"syn-ssr-media-text__body\">");
        if (heading is not null)
            sb.Append("<h2 class=\"syn-ssr-media-text__heading\">").Append(Esc(heading)).Append("</h2>");
        foreach (var paragraph in paragraphs)
            sb.Append("<p class=\"syn-ssr-media-text__text\">").Append(Esc(paragraph)).Append("</p>");
        sb.Append("</div></section>");
        return sb.ToString();
    }

    // ── Testimonials ─────────────────────────────────────────────────

    /// <summary>
    /// <c>elementSynTestimonialSection</c> — <c>itemsJson</c> array of
    /// <c>{"name","quote","role","avatarSrc"}</c>. <c>avatarSrc</c> is
    /// deliberately not rendered: it is a raw editor-typed URL and the
    /// fallback has no business fetching arbitrary remote images.
    /// </summary>
    public static string? TestimonialSection(
        string? heading,
        string? itemsJson,
        string? theme,
        string? ariaLabel)
    {
        var items = TryParseItems(itemsJson);
        if (items is null) return null;

        heading = Clean(heading);

        var sb = new StringBuilder();
        sb.Append("<section class=\"").Append(BaseClass).Append(" syn-ssr-testimonials")
          .Append(ThemeClass(theme)).Append('"')
          .Append(Marker)
          .Append(AriaLabel(heading ?? ariaLabel))
          .Append('>');

        if (heading is not null)
            sb.Append("<h2 class=\"syn-ssr-testimonials__heading\">").Append(Esc(heading)).Append("</h2>");

        sb.Append("<ul class=\"syn-ssr-testimonials__list\">");
        var rendered = 0;
        foreach (var item in items)
        {
            var quote = Field(item, "quote");
            var name = Field(item, "name");
            var role = Field(item, "role");
            if (quote is null && name is null) continue;

            sb.Append("<li class=\"syn-ssr-testimonials__item\">")
              .Append("<figure class=\"syn-ssr-testimonials__figure\">");
            if (quote is not null)
            {
                sb.Append("<blockquote class=\"syn-ssr-testimonials__quote\"><p>")
                  .Append(Esc(quote)).Append("</p></blockquote>");
            }
            if (name is not null || role is not null)
            {
                sb.Append("<figcaption class=\"syn-ssr-testimonials__attribution\">");
                if (name is not null)
                    sb.Append("<span class=\"syn-ssr-testimonials__name\">").Append(Esc(name)).Append("</span>");
                if (role is not null)
                    sb.Append("<span class=\"syn-ssr-testimonials__role\">").Append(Esc(role)).Append("</span>");
                sb.Append("</figcaption>");
            }
            sb.Append("</figure></li>");
            rendered++;
        }
        sb.Append("</ul></section>");

        return rendered == 0 ? null : sb.ToString();
    }

    // ── Animated quote ───────────────────────────────────────────────

    /// <summary>
    /// <c>elementSynQuoteAnimated</c> — the animation is the bundle's job;
    /// the fallback is the quote itself, statically.
    /// </summary>
    public static string? QuoteAnimated(string? quote, string? attribution)
    {
        quote = Clean(quote);
        attribution = Clean(attribution);
        if (quote is null && attribution is null) return null;

        var sb = new StringBuilder();
        sb.Append("<figure class=\"").Append(BaseClass).Append(" syn-ssr-quote\"").Append(Marker).Append('>');
        if (quote is not null)
        {
            sb.Append("<blockquote class=\"syn-ssr-quote__text\"><p>")
              .Append(Esc(quote)).Append("</p></blockquote>");
        }
        if (attribution is not null)
        {
            sb.Append("<figcaption class=\"syn-ssr-quote__attribution\">")
              .Append(Esc(attribution)).Append("</figcaption>");
        }
        sb.Append("</figure>");
        return sb.ToString();
    }

    // ── Stat ticker ──────────────────────────────────────────────────

    /// <summary>
    /// <c>elementSynStatTicker</c> — the count-up is the bundle's job; the
    /// fallback shows the final figure with its label.
    /// </summary>
    public static string? StatTicker(
        string? statValue,
        string? statLabel,
        string? statPrefix,
        string? statSuffix)
    {
        statValue = Clean(statValue);
        statLabel = Clean(statLabel);
        statPrefix = Clean(statPrefix);
        statSuffix = Clean(statSuffix);
        if (statValue is null && statLabel is null) return null;

        var sb = new StringBuilder();
        sb.Append("<figure class=\"").Append(BaseClass).Append(" syn-ssr-stat\"").Append(Marker).Append('>');
        if (statValue is not null)
        {
            sb.Append("<p class=\"syn-ssr-stat__value\">");
            if (statPrefix is not null)
                sb.Append("<span class=\"syn-ssr-stat__affix\">").Append(Esc(statPrefix)).Append("</span>");
            sb.Append(Esc(statValue));
            if (statSuffix is not null)
                sb.Append("<span class=\"syn-ssr-stat__affix\">").Append(Esc(statSuffix)).Append("</span>");
            sb.Append("</p>");
        }
        if (statLabel is not null)
            sb.Append("<figcaption class=\"syn-ssr-stat__label\">").Append(Esc(statLabel)).Append("</figcaption>");
        sb.Append("</figure>");
        return sb.ToString();
    }

    // ── KPI card ─────────────────────────────────────────────────────

    /// <summary>
    /// <c>elementSynKpiCard</c> — label/value as a definition list plus the
    /// trend line.
    /// </summary>
    /// <param name="trendLabel">Dictionary-resolved wording for the trend
    /// (<c>up</c> / <c>down</c> / <c>flat</c>). Without it the arrow would
    /// be pure decoration and the trend unreadable by a screen reader, so
    /// the trend line is skipped when the caller supplies nothing.</param>
    public static string? KpiCard(
        string? kpiLabel,
        string? kpiValue,
        string? kpiTrend,
        string? kpiDelta,
        string? kpiPeriod,
        string? trendLabel)
    {
        kpiLabel = Clean(kpiLabel);
        kpiValue = Clean(kpiValue);
        kpiDelta = Clean(kpiDelta);
        kpiPeriod = Clean(kpiPeriod);
        trendLabel = Clean(trendLabel);
        if (kpiLabel is null && kpiValue is null) return null;

        var trend = TrendKey(kpiTrend);

        var sb = new StringBuilder();
        sb.Append("<div class=\"").Append(BaseClass).Append(" syn-ssr-kpi\"").Append(Marker).Append('>');
        sb.Append("<dl class=\"syn-ssr-kpi__list\">");
        if (kpiLabel is not null)
            sb.Append("<dt class=\"syn-ssr-kpi__label\">").Append(Esc(kpiLabel)).Append("</dt>");
        if (kpiValue is not null)
            sb.Append("<dd class=\"syn-ssr-kpi__value\">").Append(Esc(kpiValue)).Append("</dd>");
        sb.Append("</dl>");

        if (trendLabel is not null || kpiDelta is not null || kpiPeriod is not null)
        {
            sb.Append("<p class=\"syn-ssr-kpi__trend");
            if (trend is not null) sb.Append(" syn-ssr-kpi__trend--").Append(trend);
            sb.Append("\">");
            if (trendLabel is not null)
                sb.Append("<span class=\"syn-ssr-kpi__trend-label\">").Append(Esc(trendLabel)).Append("</span>");
            if (kpiDelta is not null)
                sb.Append("<span class=\"syn-ssr-kpi__delta\">").Append(Esc(kpiDelta)).Append("</span>");
            if (kpiPeriod is not null)
                sb.Append("<span class=\"syn-ssr-kpi__period\">").Append(Esc(kpiPeriod)).Append("</span>");
            sb.Append("</p>");
        }

        sb.Append("</div>");
        return sb.ToString();
    }

    /// <summary>
    /// Normalises the <c>DTSelectKpiTrend</c> value to the closed set the
    /// CSS modifiers cover. Anything else yields <c>null</c> so an unknown
    /// value never leaks into a class name.
    /// </summary>
    public static string? TrendKey(string? kpiTrend) => Clean(kpiTrend)?.ToLowerInvariant() switch
    {
        "up" => "up",
        "down" => "down",
        "flat" => "flat",
        _ => null,
    };

    // ── Internals ────────────────────────────────────────────────────

    /// <summary>
    /// Parses an editor-authored <c>itemsJson</c> blob into flat string
    /// maps. Returns <c>null</c> — never throws — when the blob is empty,
    /// malformed, not an array, or carries no usable object.
    /// </summary>
    public static IReadOnlyList<IReadOnlyDictionary<string, string>>? TryParseItems(string? itemsJson)
    {
        if (string.IsNullOrWhiteSpace(itemsJson)) return null;

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(itemsJson);
        }
        catch (JsonException)
        {
            return null;
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;

            var list = new List<IReadOnlyDictionary<string, string>>();
            foreach (var element in doc.RootElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object) continue;
                var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var property in element.EnumerateObject())
                {
                    var value = property.Value.ValueKind switch
                    {
                        JsonValueKind.String => property.Value.GetString(),
                        JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False
                            => property.Value.ToString(),
                        _ => null,
                    };
                    if (!string.IsNullOrWhiteSpace(value)) map[property.Name] = value!.Trim();
                }
                if (map.Count > 0) list.Add(map);
            }

            return list.Count == 0 ? null : list;
        }
    }

    /// <summary>Splits a plain-text body into paragraphs (blank-line agnostic).</summary>
    public static IReadOnlyList<string> Paragraphs(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return Array.Empty<string>();
        var stripped = body.Replace("\r\n", "\n").Replace('\r', '\n');
        return stripped
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();
    }

    private static string? Field(IReadOnlyDictionary<string, string> item, string key) =>
        item.TryGetValue(key, out var value) ? Clean(value) : null;

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string AriaLabel(string? label) =>
        label is null ? string.Empty : $" aria-label=\"{Esc(label)}\"";

    private static string ThemeClass(string? theme) => Clean(theme)?.ToLowerInvariant() switch
    {
        "dark" => " syn-ssr-fallback--theme-dark",
        "light" => " syn-ssr-fallback--theme-light",
        _ => string.Empty,
    };

    private static string MediaPositionClass(string? position) =>
        Clean(position)?.ToLowerInvariant() == "right"
            ? " syn-ssr-media-text--media-right"
            : " syn-ssr-media-text--media-left";

    private static string ColumnsClass(string? columns) => Clean(columns) switch
    {
        "1" => " syn-ssr-features--cols-1",
        "2" => " syn-ssr-features--cols-2",
        "3" => " syn-ssr-features--cols-3",
        "4" => " syn-ssr-features--cols-4",
        _ => string.Empty,
    };

    private static string Esc(string value) =>
        value.Replace("&", "&amp;")
             .Replace("<", "&lt;")
             .Replace(">", "&gt;")
             .Replace("\"", "&quot;")
             .Replace("'", "&#39;");
}
