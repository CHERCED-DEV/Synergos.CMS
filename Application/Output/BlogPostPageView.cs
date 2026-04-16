using Umbraco.Cms.Core.Models.PublishedContent;
using Synergos.CMS.Domain.Shared;

namespace Synergos.CMS.Application.Output;

/// <summary>
/// View model for a single blog post.
///
/// Extends ContentPageView with structured article properties:
/// title/subtitle/body from Blog schema, author profile from Author doc type,
/// taxonomy from Category + Tags, related posts, and reading time.
///
/// The author is resolved as a full profile (name, photo, bio, social links)
/// rather than a plain string, unlike ArticlePageView.
/// </summary>
public sealed class BlogPostPageView(IPublishedContent content) : ContentPageView(content)
{
    // ── Article content ──────────────────────────────────────────────────
    public required string PostTitle    { get; init; }
    public string?         PostSubtitle { get; init; }
    public required string Body         { get; init; }
    public string?         Excerpt      { get; init; }
    public Image?          FeaturedImage { get; init; }
    public int             ReadingTime   { get; init; }
    /// <summary>
    /// Editorial classification of the post — drives badge rendering and
    /// list filtering. Resolved from the <c>postType</c> dropdown via
    /// <see cref="BlogPostTypeExtensions.FromAlias"/> so unknown legacy
    /// values safely collapse to <see cref="BlogPostType.Article"/>.
    /// </summary>
    public BlogPostType    PostType      { get; init; } = BlogPostType.Article;

    // ── Author profile ───────────────────────────────────────────────────
    public BlogAuthorInfo? Author { get; init; }

    // ── Date ─────────────────────────────────────────────────────────────
    public DateTime PublishDate { get; init; }

    // ── Taxonomy ─────────────────────────────────────────────────────────
    public IReadOnlyList<BlogCategoryInfo> Categories { get; init; } = [];
    public IReadOnlyList<string>           Tags       { get; init; } = [];

    // ── Related ──────────────────────────────────────────────────────────
    public IReadOnlyList<BlogPostSummary> RelatedPosts { get; init; } = [];

    // ── BlogHome display flags ────────────────────────────────────────────
    /// <summary>From BlogHome.showAuthorBio — whether to render the author bio block.</summary>
    public bool ShowAuthorBio     { get; init; } = true;
    /// <summary>From BlogHome.showRelatedPosts — whether to render the related posts section.</summary>
    public bool ShowRelatedPosts  { get; init; } = true;

    // ── Sidebar ──────────────────────────────────────────────────────────
    /// <summary>All distinct categories in the blog — for "Quizás también te interese" sidebar.</summary>
    public IReadOnlyList<BlogCategoryInfo> AllBlogCategories { get; init; } = [];
}

/// <summary>Resolved author profile from Author document type.</summary>
public sealed record BlogAuthorInfo(
    string  Name,
    string? Role,
    string? Bio,
    string? PhotoUrl,
    string? Email,
    string? LinkedInUrl,
    string? TwitterUrl);

/// <summary>Resolved category from Category document type.</summary>
public sealed record BlogCategoryInfo(
    string  Name,
    string? Slug,
    string  Url,
    string? IconUrl = null);

/// <summary>Lightweight summary of a blog post for listings and related posts.</summary>
public sealed record BlogPostSummary(
    string       Title,
    string?      Excerpt,
    string?      FeaturedImageUrl,
    string       Url,
    DateTime     PublishDate,
    string?      AuthorName,
    int          ReadingTime,
    string?      CategoryName,
    BlogPostType PostType = BlogPostType.Article);
