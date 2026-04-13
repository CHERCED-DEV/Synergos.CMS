using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.Blocks;
using Umbraco.Cms.Core.Models.PublishedContent;
using Synergos.CMS.Domain.Shared;

namespace Synergos.CMS.Application.Output;

/// <summary>Structured tag from a PageTag document type node.</summary>
public sealed record PageTagInfo(string Name, string? Slug, string Url);

/// <summary>
/// Base para todas las vistas de página.
///
/// Extiende ContentModel para compatibilidad con Umbraco's ContentModelBinder.
/// Esto permite que CurrentTemplate(model) funcione sin ModelBindingException
/// independientemente de si la vista usa @model o @inherits UmbracoViewPage{T}.
///
/// PageCssId y PageCssClass son los atributos DOM del nodo raíz de la página,
/// distintos de los atributos de cada sección individual (que van en SectionView).
/// </summary>
public class ContentPageView : ContentModel
{
    public ContentPageView(IPublishedContent content) : base(content) { }

    public required string   Title        { get; init; }
    public string?           Subtitle     { get; init; }
    public required SeoMetadata Seo       { get; init; }
    public string?           PageCssId    { get; init; }
    public string?           PageCssClass { get; init; }

    /// <summary>
    /// Native Umbraco Block Grid model — rendered via Html.GetBlockGridHtmlAsync().
    /// This is the primary rendering path (Umbraco 10+ native convention).
    /// </summary>
    public BlockGridModel? BlockGrid { get; init; }

    /// <summary>
    /// Lista ordenada de secciones listas para renderizar (flat).
    /// Cada elemento lleva su sección de dominio tipada y sus atributos DOM.
    /// </summary>
    public IReadOnlyList<SectionView> Sections { get; init; } = [];

    /// <summary>
    /// Árbol de secciones preservando la jerarquía de áreas del Block Grid.
    /// Section > Grid > Column > content blocks.
    /// Usado por PageSections.cshtml para rendering con layout de grilla.
    /// </summary>
    public IReadOnlyList<SectionNode> SectionTree { get; init; } = [];

    /// <summary>
    /// Structured taxonomy tags from the CompTagging composition (pageTags property).
    /// Resolved as PageTagInfo records from linked PageTag document type nodes.
    /// </summary>
    public IReadOnlyList<PageTagInfo> PageTags { get; init; } = [];
}
