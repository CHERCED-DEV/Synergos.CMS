using System.Text.RegularExpressions;
using Umbraco.Cms.Core.Models.Blocks;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Extensions;
using Synergos.CMS.Domain.Shared;
using Synergos.CMS.Application.Mapping.Compositions;
using Synergos.CMS.Application.Output;
using Synergos.CMS.Application.Rendering;
using Synergos.CMS.Schema.Constants;

namespace Synergos.CMS.Application.Mapping;

/// <summary>
/// Ensambla las vistas de página completas a partir de nodos Umbraco.
///
/// Responsabilidad única: leer propiedades de nivel página y delegar
/// el mapeo de cada bloque al ElementResolver.
///
/// Arquitectura composable:
///   Las páginas son contenedores (PageBase, LandingPage, ArticlePage).
///   El contenido vive en los Element Types del Block Grid / Block List.
///   Este assembler no conoce los tipos de bloque — solo la lista de secciones.
///
/// Soporte dual de editor:
///   1. Block Grid (pageSections) — nueva arquitectura, PageBase.
///   2. Block List (sections)    — fallback de retro-compatibilidad.
///
/// v4.0.0 — Added ReadSectionTree for nested Block Grid areas.
/// </summary>
public sealed class PageAssembler
{
    private readonly ElementResolver _resolver;

    public PageAssembler(ElementResolver resolver) => _resolver = resolver;

    /// <summary>
    /// Universal assembler for PageBase — the composable page type.
    /// All content lives in the Block Grid; no page-level business properties.
    /// </summary>
    public ContentPageView AssemblePage(IPublishedContent page)
    {
        var blockGrid    = page.Value<BlockGridModel>("pageSections");
        var siteSettings = ResolveSiteSettings(page);

        return new(page)
        {
            Title        = page.Value<string>("pageTitle") ?? page.Value<string>("title") ?? page.Name,
            Subtitle     = page.Value<string>("pageSubtitle") ?? page.Value<string>("subtitle"),
            Seo          = ReadSeo(page, siteSettings),
            PageCssId    = page.Value<string>("cssId"),
            PageCssClass = page.Value<string>("cssClass"),
            BlockGrid    = blockGrid,
            Sections     = ReadSections(page),
            SectionTree  = ReadSectionTree(page),
            PageTags     = ReadPageTags(page)
        };
    }

    public ArticlePageView AssembleArticlePage(IPublishedContent page)
    {
        var blockGrid    = page.Value<BlockGridModel>("pageSections");
        var siteSettings = ResolveSiteSettings(page);

        return new(page)
        {
            Title         = page.Value<string>("pageTitle") ?? page.Value<string>("title") ?? page.Name,
            Subtitle      = page.Value<string>("pageSubtitle") ?? page.Value<string>("subtitle"),
            Seo           = ReadSeo(page, siteSettings),
            PageCssId     = page.Value<string>("cssId"),
            PageCssClass  = page.Value<string>("cssClass"),
            BlockGrid     = blockGrid,
            Sections      = ReadSections(page),
            SectionTree   = ReadSectionTree(page),
            PageTags      = ReadPageTags(page),
            Author        = page.Value<string>("author") ?? string.Empty,
            PublishDate   = page.Value<DateTime>("publishDate"),
            Body          = StripScriptTags(page.Value<Microsoft.AspNetCore.Html.IHtmlContent>("body")?.ToString() ?? string.Empty),
            Tags          = page.Value<IEnumerable<string>>("tags")?.ToList() ?? [],
            FeaturedImage = ReadImage(page, "featuredImage")
        };
    }

    // ──────────────────────────────────────────────────────────────────────
    // Lectores privados
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>Flat section list (backward compatible).</summary>
    private IReadOnlyList<SectionView> ReadSections(IPublishedContent page)
    {
        // Block Grid — nueva arquitectura (PageBase.pageSections)
        // BlockGridModel : IEnumerable<BlockGridItem> — iteramos los items raíz directamente.
        var blockGrid = page.Value<BlockGridModel>("pageSections");
        if (blockGrid is not null)
        {
            return blockGrid
                .Select(item => _resolver.Resolve(item))
                .Where(sv => sv is not null)
                .Cast<SectionView>()
                .ToList();
        }

        // Block List — fallback de retro-compatibilidad
        var blockList = page.Value<BlockListModel>("sections");
        if (blockList is not null)
        {
            return blockList
                .Select(b => _resolver.Resolve(b))
                .Where(sv => sv is not null)
                .Cast<SectionView>()
                .ToList();
        }

        return [];
    }

    /// <summary>
    /// Tree structure preserving Block Grid areas hierarchy.
    /// Used by PageSections.cshtml for nested rendering (Section > Grid > Column > content).
    /// </summary>
    private IReadOnlyList<SectionNode> ReadSectionTree(IPublishedContent page)
    {
        var blockGrid = page.Value<BlockGridModel>("pageSections");
        if (blockGrid is null) return [];

        return blockGrid
            .Select(item => _resolver.ResolveTree(item))
            .Where(node => node is not null)
            .Cast<SectionNode>()
            .ToList();
    }

    /// <summary>
    /// Walks the ancestor tree to find the SiteRoot's SiteSettings child.
    /// Used to apply site-level SEO defaults when a page has no explicit meta values.
    /// </summary>
    private static IPublishedContent? ResolveSiteSettings(IPublishedContent page)
    {
        var siteRoot = page.AncestorsOrSelf()
            .FirstOrDefault(n => n.ContentType.Alias == ContentTypeKeys.Aliases.SiteRoot);
        return siteRoot?.FirstChild(ContentTypeKeys.Aliases.SiteSettingsAlias);
    }

    /// <summary>
    /// Reads page-level SEO values with site-level defaults as fallback.
    /// - Title:       page.seoTitle (no suffix applied here — handled by view)
    /// - Description: page.seoDescription ?? siteSettings.seoDefaultDescription
    /// - Image:       page.seoImage ?? siteSettings.seoDefaultOgImage
    /// </summary>
    private static SeoMetadata ReadSeo(IPublishedContent page, IPublishedContent? siteSettings)
        => new(
            Title:        page.Value<string>(PropertyAliases.Seo.SeoTitle),
            Description:  page.Value<string>(PropertyAliases.Seo.SeoDescription)
                          ?? siteSettings?.Value<string>("seoDefaultDescription"),
            Image:        ReadImage(page, "seoImage")
                          ?? (siteSettings is not null ? ReadImage(siteSettings, "seoDefaultOgImage") : null),
            NoIndex:      page.Value<bool>("noIndex"),
            CanonicalUrl: page.Value<Umbraco.Cms.Core.Models.Link>("canonicalUrl")?.Url,
            TitleSuffix:  siteSettings?.Value<string>("seoTitleSuffix"));

    private static Image? ReadImage(IPublishedContent page, string alias)
    {
        var media = page.Value<IPublishedContent>(alias);
        if (media is null) return null;
        var w = media.Value<int>("umbracoWidth");
        var h = media.Value<int>("umbracoHeight");
        return new Image(media.Url(), media.Name, w > 0 ? w : null, h > 0 ? h : null);
    }

    /// <summary>
    /// Removes &lt;script&gt; blocks and inline event handlers from editor-provided HTML.
    /// Umbraco TinyMCE already filters most dangerous content, but this is a second
    /// defence layer for the cases where a super-admin pastes raw HTML.
    /// </summary>
    private static string StripScriptTags(string html)
    {
        if (string.IsNullOrEmpty(html)) return html;
        var noScript = Regex.Replace(html,
            @"<script\b[^<]*(?:(?!<\/script>)<[^<]*)*<\/script>",
            string.Empty, RegexOptions.IgnoreCase | RegexOptions.Singleline);
        return Regex.Replace(noScript,
            @"\s+on\w+\s*=\s*(?:""[^""]*""|'[^']*')",
            string.Empty, RegexOptions.IgnoreCase);
    }

    internal static IReadOnlyList<PageTagInfo> ReadPageTags(IPublishedContent page)
    {
        var picks = page.Value<IEnumerable<IPublishedContent>>("pageTags");
        if (picks is null) return [];

        return picks
            .Select(t => new PageTagInfo(
                Name: t.Value<string>("tagName") ?? t.Name,
                Slug: t.Value<string>("tagSlug"),
                Url:  t.Url()))
            .ToList();
    }
}
