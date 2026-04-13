using Umbraco.Cms.Core.Models.PublishedContent;

namespace Synergos.CMS.Application.Output;

/// <summary>
/// View model for a blog category listing page.
///
/// Shows all posts tagged with this category (paginated),
/// plus sidebar "Otros temas" with sibling categories.
/// </summary>
public sealed class BlogCategoryPageView(IPublishedContent content) : ContentPageView(content)
{
    // ── Category metadata ────────────────────────────────────────────────
    public required string CategoryName        { get; init; }
    public string?         CategoryDescription { get; init; }
    public string?         CategoryImageUrl    { get; init; }
    public string?         CategoryColor       { get; init; }
    public string?         CategoryIconUrl     { get; init; }

    // ── Posts ─────────────────────────────────────────────────────────────
    public IReadOnlyList<BlogPostSummary> Posts { get; init; } = [];

    // ── Sidebar ──────────────────────────────────────────────────────────
    public IReadOnlyList<BlogCategoryInfo> OtherCategories { get; init; } = [];

    // ── Pagination ───────────────────────────────────────────────────────
    public int CurrentPage  { get; init; } = 1;
    public int TotalPages   { get; init; } = 1;
    public int TotalPosts   { get; init; }
    public int PostsPerPage { get; init; } = 10;
}
