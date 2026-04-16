using Microsoft.AspNetCore.Html;
using Umbraco.Cms.Core.Models.Blocks;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Extensions;
using Synergos.CMS.Application.Mapping.Compositions;
using Synergos.CMS.Application.Output;
using Synergos.CMS.Application.Rendering;
using Synergos.CMS.Domain.Shared;
using Synergos.CMS.Schema.Constants;

namespace Synergos.CMS.Application.Mapping;

/// <summary>
/// Assembles blog-specific page views from Umbraco content nodes.
///
/// Tree model: BlogHome → Category* → BlogPost*
///
/// Three responsibilities:
///   1. AssembleBlogHome     — blog index with resolved post listing + pagination
///   2. AssembleBlogPost     — single post with author profile, taxonomy, related posts
///   3. AssembleBlogCategory — category listing with its child posts, sidebar with siblings
///
/// Category is resolved from the tree hierarchy (parent node), not from a picker.
/// Author is resolved by following ContentPicker references.
/// </summary>
public sealed class BlogAssembler
{
    private readonly ElementResolver _resolver;

    public BlogAssembler(ElementResolver resolver) => _resolver = resolver;

    // ══════════════════════════════════════════════════════════════════════════
    // Blog Home
    // ══════════════════════════════════════════════════════════════════════════

    public BlogHomePageView AssembleBlogHome(IPublishedContent page, int currentPage = 1)
    {
        var blockGrid    = page.Value<BlockGridModel>("pageSections");
        var postsPerPage = page.Value<int>("postsPerPage");
        if (postsPerPage <= 0) postsPerPage = 10;

        // blogCtaBlock: ContentPicker → ReusableBlock → pageSections Block Grid
        var ctaNode         = page.Value<IPublishedContent>("blogCtaBlock");
        var blogCtaBlockGrid = ctaNode?.Value<BlockGridModel>("pageSections");

        // Resolve Category children of BlogHome, then collect all BlogPost grandchildren
        var categoryNodes = page.Children?
            .Where(c => c.ContentType.Alias == ContentTypeKeys.Aliases.Category && c.IsVisible())
            .ToList() ?? [];

        var allPosts = categoryNodes
            .SelectMany(cat => cat.Children?
                .Where(c => c.ContentType.Alias == ContentTypeKeys.Aliases.BlogPost && c.IsVisible())
                ?? Enumerable.Empty<IPublishedContent>())
            .OrderByDescending(c => c.Value<DateTime>("contentDate"))
            .ThenByDescending(c => c.CreateDate)
            .ToList();

        var totalPosts = allPosts.Count;
        var totalPages = (int)Math.Ceiling((double)totalPosts / postsPerPage);
        currentPage = Math.Clamp(currentPage, 1, Math.Max(1, totalPages));

        var pagedPosts = allPosts
            .Skip((currentPage - 1) * postsPerPage)
            .Take(postsPerPage)
            .Select(ToPostSummary)
            .ToList();

        // Featured post
        var featuredNode = page.Value<IPublishedContent>("featuredPost");
        var featuredPost = featuredNode is not null ? ToPostSummary(featuredNode) : null;

        // Categories — from Category child nodes directly
        var categories = categoryNodes
            .Select(c => new BlogCategoryInfo(
                Name:    c.Value<string>("categoryName") ?? c.Name,
                Slug:    c.Value<string>("categorySlug"),
                Url:     c.Url(),
                IconUrl: c.Value<IPublishedContent>("categoryIcon")?.Url()))
            .ToList();

        return new BlogHomePageView(page)
        {
            Title          = page.Value<string>("pageTitle") ?? page.Name,
            Subtitle       = page.Value<string>("pageSubtitle"),
            Seo            = ReadSeo(page, ResolveSiteSettings(page)),
            PageCssId      = page.Value<string>("cssId"),
            PageCssClass   = page.Value<string>("cssClass"),
            BlockGrid      = blockGrid,
            Sections       = [],
            SectionTree    = [],
            PageTags       = PageAssembler.ReadPageTags(page),
            PostsPerPage     = postsPerPage,
            ShowCategories   = page.Value<bool>("showCategories"),
            ShowTags         = page.Value<bool>("showTags"),
            ListLayout       = page.Value<string>("listLayout"),
            FeaturedPost     = featuredPost,
            Posts            = pagedPosts,
            Categories       = categories,
            BlogCtaBlockGrid = blogCtaBlockGrid,
            CurrentPage      = currentPage,
            TotalPages       = totalPages,
            TotalPosts       = totalPosts
        };
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Blog Category
    // ══════════════════════════════════════════════════════════════════════════

    public BlogCategoryPageView AssembleBlogCategory(IPublishedContent page, int currentPage = 1)
    {
        var blogHome = page.Parent;

        var postsPerPage = blogHome is not null ? blogHome.Value<int>("postsPerPage") : 0;
        if (postsPerPage <= 0) postsPerPage = 10;

        // Posts are direct children of this category node
        var allMatchingPosts = page.Children?
            .Where(c => c.ContentType.Alias == ContentTypeKeys.Aliases.BlogPost && c.IsVisible())
            .OrderByDescending(c => c.Value<DateTime>("contentDate"))
            .ThenByDescending(c => c.CreateDate)
            .ToList() ?? [];

        var totalPosts  = allMatchingPosts.Count;
        var totalPages  = (int)Math.Ceiling((double)totalPosts / postsPerPage);
        currentPage     = Math.Clamp(currentPage, 1, Math.Max(1, totalPages));

        var pagedPosts = allMatchingPosts
            .Skip((currentPage - 1) * postsPerPage)
            .Take(postsPerPage)
            .Select(ToPostSummary)
            .ToList();

        // Sibling category nodes (other categories under the same blogHome)
        var otherCategories = blogHome?.Children?
            .Where(c => c.ContentType.Alias == ContentTypeKeys.Aliases.Category
                     && c.IsVisible()
                     && c.Key != page.Key)
            .Select(c => new BlogCategoryInfo(
                Name:    c.Value<string>("categoryName") ?? c.Name,
                Slug:    c.Value<string>("categorySlug"),
                Url:     c.Url(),
                IconUrl: c.Value<IPublishedContent>("categoryIcon")?.Url()))
            .ToList() ?? [];

        return new BlogCategoryPageView(page)
        {
            Title               = page.Value<string>("categoryName") ?? page.Name,
            Subtitle            = page.Value<string>("categoryDescription"),
            Seo                 = ReadSeo(page, ResolveSiteSettings(page)),
            PageCssId           = page.Value<string>("cssId"),
            PageCssClass        = page.Value<string>("cssClass"),
            BlockGrid           = null,
            Sections            = [],
            SectionTree         = [],
            PageTags            = PageAssembler.ReadPageTags(page),
            CategoryName        = page.Value<string>("categoryName") ?? page.Name,
            CategoryDescription = page.Value<string>("categoryDescription"),
            CategoryImageUrl    = page.Value<IPublishedContent>("categoryImage")?.Url(),
            CategoryColor       = page.Value<string>("categoryColor"),
            CategoryIconUrl     = page.Value<IPublishedContent>("categoryIcon")?.Url(),
            Posts               = pagedPosts,
            OtherCategories     = otherCategories,
            CurrentPage         = currentPage,
            TotalPages          = totalPages,
            TotalPosts          = totalPosts,
            PostsPerPage        = postsPerPage
        };
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Blog Post
    // ══════════════════════════════════════════════════════════════════════════

    public BlogPostPageView AssembleBlogPost(IPublishedContent page)
    {
        var blockGrid = page.Value<BlockGridModel>("pageSections");

        // Read display flags from BlogHome (post.Parent = Category, .Parent = BlogHome)
        var blogHome       = page.Parent?.Parent;
        var showAuthorBio  = blogHome is null || blogHome.Value<bool?>("showAuthorBio")  != false;
        var showRelatedPosts = blogHome is null || blogHome.Value<bool?>("showRelatedPosts") != false;

        return new BlogPostPageView(page)
        {
            Title         = page.Value<string>("postTitle") ?? page.Name,
            Subtitle      = page.Value<string>("postSubtitle"),
            Seo           = ReadSeo(page, ResolveSiteSettings(page)),
            PageCssId     = page.Value<string>("cssId"),
            PageCssClass  = page.Value<string>("cssClass"),
            BlockGrid     = blockGrid,
            Sections      = [],
            SectionTree   = [],
            PostTitle     = page.Value<string>("postTitle") ?? page.Name,
            PostSubtitle  = page.Value<string>("postSubtitle"),
            Body          = page.Value<IHtmlContent>("postBody")?.ToString() ?? string.Empty,
            Excerpt       = page.Value<string>("postExcerpt"),
            FeaturedImage = ReadImage(page, "featuredImage"),
            ReadingTime   = page.Value<int>("readingTime"),
            PostType      = BlogPostTypeExtensions.FromAlias(page.Value<string>("postType")),
            Author        = ResolveAuthor(page),
            PublishDate   = page.Value<DateTime>("contentDate"),
            Categories        = ResolveCategoryFromParent(page),
            Tags              = page.Value<IEnumerable<string>>("postTags")?.ToList() ?? [],
            RelatedPosts      = ResolveRelatedPosts(page),
            ShowAuthorBio     = showAuthorBio,
            ShowRelatedPosts  = showRelatedPosts,
            PageTags          = PageAssembler.ReadPageTags(page),
            AllBlogCategories = ResolveAllCategoriesFromBlogHome(page)
        };
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Author profile page
    // ══════════════════════════════════════════════════════════════════════════

    public AuthorPageView AssembleAuthor(IPublishedContent author)
    {
        var profile = ReadAuthorProfile(author);

        // Find every post that picked this author. We walk all sibling/cousin
        // BlogHomes under the same site root — authors live in shared content
        // and may be referenced from multiple blog trees.
        var siteRoot = author.AncestorsOrSelf()
            .FirstOrDefault(n => n.ContentType.Alias == ContentTypeKeys.Aliases.SiteRoot);

        var posts = siteRoot?
            .DescendantsOrSelf()
            .Where(n => n.ContentType.Alias == ContentTypeKeys.Aliases.BlogPost && n.IsVisible())
            .Where(p => p.Value<IPublishedContent>("postAuthor")?.Key == author.Key)
            .OrderByDescending(p => p.Value<DateTime>("contentDate"))
            .ThenByDescending(p => p.CreateDate)
            .Select(ToPostSummary)
            .ToList() ?? [];

        return new AuthorPageView(author)
        {
            Title        = profile.Name,
            Subtitle     = profile.Role,
            Seo          = ReadSeo(author, ResolveSiteSettings(author)),
            PageCssId    = author.Value<string>("cssId"),
            PageCssClass = author.Value<string>("cssClass"),
            BlockGrid    = null,
            Sections     = [],
            SectionTree  = [],
            PageTags     = PageAssembler.ReadPageTags(author),
            Profile      = profile,
            Posts        = posts
        };
    }

    private static BlogAuthorInfo ReadAuthorProfile(IPublishedContent author) => new(
        Name:       author.Value<string>("authorName") ?? author.Name,
        Role:       author.Value<string>("authorRole"),
        Bio:        author.Value<string>("authorBio"),
        PhotoUrl:   author.Value<IPublishedContent>("authorPhoto")?.Url(),
        Email:      author.Value<string>("authorEmail"),
        LinkedInUrl: author.Value<string>("authorLinkedin"),
        TwitterUrl:  author.Value<string>("authorTwitter"));

    // ══════════════════════════════════════════════════════════════════════════
    // Resolvers
    // ══════════════════════════════════════════════════════════════════════════

    private static BlogAuthorInfo? ResolveAuthor(IPublishedContent post)
    {
        var author = post.Value<IPublishedContent>("postAuthor");
        if (author is null) return null;

        return new BlogAuthorInfo(
            Name:       author.Value<string>("authorName") ?? author.Name,
            Role:       author.Value<string>("authorRole"),
            Bio:        author.Value<string>("authorBio"),
            PhotoUrl:   author.Value<IPublishedContent>("authorPhoto")?.Url(),
            Email:      author.Value<string>("authorEmail"),
            LinkedInUrl: author.Value<string>("authorLinkedin"),
            TwitterUrl:  author.Value<string>("authorTwitter"));
    }

    private static IReadOnlyList<BlogCategoryInfo> ResolveCategoryFromParent(IPublishedContent post)
    {
        var parent = post.Parent;
        if (parent is null || parent.ContentType.Alias != ContentTypeKeys.Aliases.Category)
            return [];

        return [new BlogCategoryInfo(
            Name:    parent.Value<string>("categoryName") ?? parent.Name,
            Slug:    parent.Value<string>("categorySlug"),
            Url:     parent.Url(),
            IconUrl: parent.Value<IPublishedContent>("categoryIcon")?.Url())];
    }

    private static IReadOnlyList<BlogCategoryInfo> ResolveAllCategoriesFromBlogHome(IPublishedContent post)
    {
        // post.Parent = Category, post.Parent.Parent = BlogHome
        var blogHome = post.Parent?.Parent;
        if (blogHome is null) return [];

        return blogHome.Children?
            .Where(c => c.ContentType.Alias == ContentTypeKeys.Aliases.Category && c.IsVisible())
            .Select(c => new BlogCategoryInfo(
                Name:    c.Value<string>("categoryName") ?? c.Name,
                Slug:    c.Value<string>("categorySlug"),
                Url:     c.Url(),
                IconUrl: c.Value<IPublishedContent>("categoryIcon")?.Url()))
            .ToList() ?? [];
    }

    private static IReadOnlyList<BlogPostSummary> ResolveRelatedPosts(IPublishedContent post)
    {
        var picks = post.Value<IEnumerable<IPublishedContent>>("relatedPosts");
        if (picks is null) return [];

        return picks.Select(ToPostSummary).ToList();
    }

    private static BlogPostSummary ToPostSummary(IPublishedContent post)
    {
        var author   = post.Value<IPublishedContent>("postAuthor");
        var parent   = post.Parent;
        var category = parent?.ContentType.Alias == ContentTypeKeys.Aliases.Category
                     ? (parent.Value<string>("categoryName") ?? parent.Name)
                     : null;

        return new BlogPostSummary(
            Title:            post.Value<string>("postTitle") ?? post.Name,
            Excerpt:          post.Value<string>("postExcerpt"),
            FeaturedImageUrl: post.Value<IPublishedContent>("featuredImage")?.Url(),
            Url:              post.Url(),
            PublishDate:      post.Value<DateTime>("contentDate"),
            AuthorName:       author?.Value<string>("authorName") ?? author?.Name,
            ReadingTime:      post.Value<int>("readingTime"),
            CategoryName:     category,
            PostType:         BlogPostTypeExtensions.FromAlias(post.Value<string>("postType")));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Shared helpers (same pattern as PageAssembler)
    // ══════════════════════════════════════════════════════════════════════════

    private static IPublishedContent? ResolveSiteSettings(IPublishedContent page)
    {
        var siteRoot = page.AncestorsOrSelf()
            .FirstOrDefault(n => n.ContentType.Alias == ContentTypeKeys.Aliases.SiteRoot);
        return siteRoot?.FirstChild(ContentTypeKeys.Aliases.SiteSettingsAlias);
    }

    /// <summary>
    /// Reads page-level SEO values with site-level defaults as fallback.
    /// All page aliases come from compSeo (see <see cref="PropertyAliases.Seo"/>).
    /// </summary>
    private static SeoMetadata ReadSeo(IPublishedContent page, IPublishedContent? siteSettings = null)
        => new(
            Title:        page.Value<string>(PropertyAliases.Seo.SeoTitle),
            Description:  page.Value<string>(PropertyAliases.Seo.SeoDescription)
                          ?? siteSettings?.Value<string>("seoDefaultDescription"),
            Image:        ReadImage(page, PropertyAliases.Seo.OgImage)
                          ?? (siteSettings is not null ? ReadImage(siteSettings, "seoDefaultOgImage") : null),
            NoIndex:      IsNoIndex(page.Value<string>(PropertyAliases.Seo.Robots)),
            CanonicalUrl: page.Value<string>(PropertyAliases.Seo.Canonical),
            TitleSuffix:  siteSettings?.Value<string>("seoTitleSuffix"));

    /// <summary>
    /// True when the robots dropdown directive includes "noindex".
    /// </summary>
    private static bool IsNoIndex(string? robots)
        => !string.IsNullOrWhiteSpace(robots)
           && robots.Contains("noindex", StringComparison.OrdinalIgnoreCase);

    private static Image? ReadImage(IPublishedContent page, string alias)
    {
        var media = page.Value<IPublishedContent>(alias);
        if (media is null) return null;
        var w = media.Value<int>("umbracoWidth");
        var h = media.Value<int>("umbracoHeight");
        return new Image(media.Url(), media.Name, w > 0 ? w : null, h > 0 ? h : null);
    }
}
