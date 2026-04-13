using System.Text.Json;
using System.Text.Json.Serialization;
using Umbraco.Cms.Core.Models.PublishedContent;
using Synergos.CMS.Application.Components;
using Synergos.CMS.Application.Output;
using Synergos.CMS.Application.Output.Config;
using Synergos.CMS.Schema.Constants;

namespace Synergos.CMS.Application.Mapping;

/// <summary>
/// Convierte IPublishedContent → PageConfig (JSON-serializable).
///
/// Reutiliza PageAssembler para no duplicar la lógica de lectura de propiedades.
/// Solo añade la capa de serialización necesaria para el API JSON → Angular.
///
/// Flujo:
///   PageAssembler.Assemble*() → ContentPageView
///     → MapSeo()              → SeoConfig
///     → MapBlock()            → BlockConfig (con Data serializado en camelCase)
///     → PageConfig
/// </summary>
public sealed class PageConfigAssembler
{
    private readonly PageAssembler _assembler;

    /// <summary>
    /// Document type aliases that require the article assembler.
    /// Uses constants from ContentTypeKeys.Aliases to avoid hardcoded strings.
    /// Note: BlogPost is NOT included — it has its own assembler (BlogAssembler)
    /// with different property aliases (postTitle, postBody, postAuthor as ContentPicker).
    /// </summary>
    private static readonly HashSet<string> ArticleAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ContentTypeKeys.Aliases.ArticlePage
    };

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNamingPolicy   = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public PageConfigAssembler(PageAssembler assembler) => _assembler = assembler;

    public PageConfig Build(IPublishedContent page)
    {
        ContentPageView view = ArticleAliases.Contains(page.ContentType.Alias)
            ? _assembler.AssembleArticlePage(page)
            : _assembler.AssemblePage(page);

        return new PageConfig(
            Title:    view.Title,
            Subtitle: view.Subtitle,
            Seo:      MapSeo(view.Seo),
            Dom:      new PageDomConfig(view.PageCssId, view.PageCssClass),
            Sections: view.Sections
                          .Select(MapBlock)
                          .OfType<BlockConfig>()
                          .ToList()
        );
    }

    // ── Mappers privados ──────────────────────────────────────────────────────

    private static SeoConfig MapSeo(Domain.Shared.SeoMetadata seo) => new(
        Title:        seo.Title,
        Description:  seo.Description,
        ImageUrl:     seo.Image?.Src,
        NoIndex:      seo.NoIndex,
        CanonicalUrl: seo.CanonicalUrl);

    private static BlockConfig? MapBlock(SectionView sv)
    {
        if (sv.Section is not ComponentSection cs) return null;

        var vm = cs.ViewModel;

        // Serialize the concrete ViewModel type so all its properties are included.
        // JsonSerializer uses the runtime type when the argument is typed as object.
        var data = JsonSerializer.SerializeToElement((object)vm, _jsonOpts);

        return new BlockConfig(vm.Alias, vm.BlockClass, data);
    }
}
