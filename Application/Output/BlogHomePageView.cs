using Umbraco.Cms.Core.Models.Blocks;
using Umbraco.Cms.Core.Models.PublishedContent;

namespace Synergos.CMS.Application.Output;

/// <summary>
/// View model for the blog index page.
///
/// Contains the page shell (title, SEO, Block Grid for hero/intro content)
/// plus blog-specific configuration and the resolved post listing.
/// </summary>
public sealed class BlogHomePageView(IPublishedContent content) : ContentPageView(content)
{
    // ── Blog configuration ───────────────────────────────────────────────
    public int     PostsPerPage   { get; init; } = 10;
    public bool    ShowCategories { get; init; }
    public bool    ShowTags       { get; init; }
    public string? ListLayout     { get; init; }

    // ── Resolved content ─────────────────────────────────────────────────
    public BlogPostSummary?                FeaturedPost    { get; init; }
    public IReadOnlyList<BlogPostSummary>  Posts           { get; init; } = [];
    public IReadOnlyList<BlogCategoryInfo> Categories      { get; init; } = [];
    /// <summary>Block Grid from the ReusableBlock picked in blogCtaBlock — rendered below the post listing.</summary>
    public BlockGridModel?                 BlogCtaBlockGrid { get; init; }

    // ── Pagination ───────────────────────────────────────────────────────
    public int CurrentPage { get; init; } = 1;
    public int TotalPages  { get; init; } = 1;
    public int TotalPosts  { get; init; }
}
