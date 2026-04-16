using Synergos.CMS.Application.Components;
using Synergos.CMS.Application.Output;
using Synergos.CMS.Domain.Compositions.Content;

namespace Synergos.CMS.Application.Elements;

/// <summary>
/// Displays a curated list of content pages or blog posts.
/// Articles are resolved from MultiContentPicker at map time.
/// </summary>
public sealed class ArticleListViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/ArticleList";
    public override string BlockClass => "sg-element--comp-article-list";
    public override string Alias      => "elementCompArticleList";

    public ContentHeadingModel? Heading { get; init; }

    /// <summary>Pre-resolved list of article summaries.</summary>
    public IReadOnlyList<BlogPostSummary> Articles { get; init; } = [];

    public bool    ShowExcerpt { get; init; }
    public bool    ShowImage   { get; init; }
    public string? ListLayout  { get; init; }
}
