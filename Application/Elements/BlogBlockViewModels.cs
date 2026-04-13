using Synergos.CMS.Application.Components;
using Synergos.CMS.Application.Output;
using Synergos.CMS.Domain.Compositions.Content;

namespace Synergos.CMS.Application.Elements;

/// <summary>
/// Displays a featured/recent posts teaser from a BlogHome source.
/// The blog source is resolved at map time; posts are pre-fetched.
/// </summary>
public sealed class BlogHighlightViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/BlogHighlight";
    public override string BlockClass => "sg-element--comp-blog-highlight";
    public override string Alias      => "elementCompBlogHighlight";

    public ContentHeadingModel? Heading { get; init; }

    /// <summary>Pre-resolved list of blog post summaries.</summary>
    public IReadOnlyList<BlogPostSummary> Posts { get; init; } = [];

    public bool    ShowExcerpt { get; init; }
    public bool    ShowImage   { get; init; }
    public string? ListLayout  { get; init; }
}

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
