using Umbraco.Cms.Core.Models.PublishedContent;
using Synergos.CMS.Schema.Constants;

namespace Synergos.CMS.Application.Blog;

/// <summary>
/// Servicio de blog — consulta el árbol de contenido publicado para posts.
/// Singleton: stateless, seguro para inyectar en Singletons y Transients.
///
/// Tree model: BlogHome → Category* → BlogPost*
/// Posts are children of categories, which are children of blogHome.
/// </summary>
public sealed class BlogService
{
    private static readonly string PostAlias     = ContentTypeKeys.Aliases.BlogPost;
    private static readonly string CategoryAlias = ContentTypeKeys.Aliases.Category;

    public BlogPagedResult GetPosts(IPublishedContent blogHome, int page, int pageSize)
    {
        var posts = GetAllPosts(blogHome);
        return Paginate(posts, page, pageSize);
    }

    public BlogPagedResult GetPostsByCategory(IPublishedContent blogHome, string categorySlug, int page, int pageSize)
    {
        var category = blogHome.Children?
            .FirstOrDefault(c => string.Equals(c.ContentType.Alias, CategoryAlias, StringComparison.OrdinalIgnoreCase)
                              && c.IsPublished()
                              && string.Equals(c.UrlSegment, categorySlug, StringComparison.OrdinalIgnoreCase));

        if (category is null) return new BlogPagedResult([], page, 0, 0);

        var posts = GetPostsFromCategory(category);
        return Paginate(posts, page, pageSize);
    }

    public BlogPagedResult GetPostsByTag(IPublishedContent blogHome, string tag, int page, int pageSize)
    {
        var posts = GetAllPosts(blogHome)
            .Where(p => p.Value<IEnumerable<string>>("tags")
                         ?.Any(t => string.Equals(t, tag, StringComparison.OrdinalIgnoreCase)) == true)
            .ToList();
        return Paginate(posts, page, pageSize);
    }

    public IReadOnlyList<IPublishedContent> GetRelatedPosts(IPublishedContent post, int max = 3)
    {
        // post.Parent = Category, post.Parent.Parent = BlogHome
        var category = post.Parent;
        var blogHome = category?.Parent;
        if (blogHome is null) return [];

        var postTags = post.Value<IEnumerable<string>>("tags")
                           ?.ToHashSet(StringComparer.OrdinalIgnoreCase)
                       ?? [];
        var postCategoryId = category?.Id;

        return GetAllPosts(blogHome)
            .Where(p => p.Id != post.Id)
            .Select(p =>
            {
                // Category match = 2 pts, each shared tag = 1 pt
                var score = (p.Parent?.Id == postCategoryId ? 2 : 0)
                          + (postTags.Count > 0
                              ? p.Value<IEnumerable<string>>("tags")
                                 ?.Count(t => postTags.Contains(t)) ?? 0
                              : 0);
                return (Post: p, Score: score);
            })
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Post.Value<DateTime>("publishDate"))
            .Take(max)
            .Select(x => x.Post)
            .ToList();
    }

    public IPublishedContent? GetFeaturedPost(IPublishedContent blogHome)
        => blogHome.Value<IPublishedContent>("featuredPost");

    // ── Private helpers ───────────────────────────────────────────────────

    /// <summary>
    /// Collects all published BlogPost nodes across every Category under blogHome.
    /// BlogHome → Category* → BlogPost*  (two-level traversal).
    /// </summary>
    private static List<IPublishedContent> GetAllPosts(IPublishedContent blogHome)
    {
        var categories = blogHome.Children?
            .Where(c => string.Equals(c.ContentType.Alias, CategoryAlias, StringComparison.OrdinalIgnoreCase)
                     && c.IsPublished())
            ?? Enumerable.Empty<IPublishedContent>();

        return categories
            .SelectMany(GetPostsFromCategory)
            .OrderByDescending(p => p.Value<DateTime>("publishDate"))
            .ToList();
    }

    /// <summary>
    /// Gets published BlogPost children of a single category node.
    /// </summary>
    private static List<IPublishedContent> GetPostsFromCategory(IPublishedContent category)
        => category.Children?
            .Where(c => string.Equals(c.ContentType.Alias, PostAlias, StringComparison.OrdinalIgnoreCase)
                     && c.IsPublished())
            .OrderByDescending(p => p.Value<DateTime>("publishDate"))
            .ToList() ?? [];

    private static BlogPagedResult Paginate(List<IPublishedContent> posts, int page, int pageSize)
    {
        page     = Math.Max(1, page);
        pageSize = Math.Max(1, pageSize);

        var totalItems = posts.Count;
        var totalPages = (int)Math.Ceiling((double)totalItems / pageSize);
        var items      = posts.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return new BlogPagedResult(items, page, totalPages, totalItems);
    }
}
