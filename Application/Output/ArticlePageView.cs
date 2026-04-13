using Umbraco.Cms.Core.Models.PublishedContent;
using Synergos.CMS.Domain.Shared;

namespace Synergos.CMS.Application.Output;

public sealed class ArticlePageView(IPublishedContent content) : ContentPageView(content)
{
    public required string           Author        { get; init; }
    public required DateTime         PublishDate   { get; init; }

    /// <summary>
    /// Cuerpo del artículo como HTML pre-renderizado.
    /// La vista aplica Html.Raw() para evitar doble-escape.
    /// </summary>
    public required string           Body          { get; init; }
    public IReadOnlyList<string>     Tags          { get; init; } = [];
    public Image?                    FeaturedImage { get; init; }
}
