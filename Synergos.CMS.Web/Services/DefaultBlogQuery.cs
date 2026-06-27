using Synergos.CMS.Interfaces;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Web;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Default <see cref="IBlogQuery"/>. Recorre los descendientes del
/// siteRoot del request actual buscando nodos <c>postPage</c>, aplica
/// los filtros del <see cref="BlogQueryRequest"/> y proyecta a
/// <see cref="PostSummary"/>.
/// </summary>
/// <remarks>
/// Vive en <c>Synergos.CMS.Web</c> porque depende de
/// <see cref="IUmbracoContextAccessor"/>. Sin caché — el published
/// cache de Umbraco ya mantiene los nodos en memoria. Para sitios con
/// 10k+ posts evaluar Examine o un store dedicado.
/// </remarks>
public sealed class DefaultBlogQuery : IBlogQuery
{
    private const string PostPageAlias = "postPage";
    private const string PostCategoryPageAlias = "postCategoryPage";

    private readonly IUmbracoContextAccessor _umbracoContextAccessor;

    public DefaultBlogQuery(IUmbracoContextAccessor umbracoContextAccessor) =>
        _umbracoContextAccessor = umbracoContextAccessor;

    public IReadOnlyList<PostSummary> GetPosts(BlogQueryRequest request)
    {
        if (!_umbracoContextAccessor.TryGetUmbracoContext(out var umbracoContext))
        {
            return Array.Empty<PostSummary>();
        }

        var siteRoots = umbracoContext.PublishedRequest?.PublishedContent?.Root() is { } currentRoot
            ? new[] { currentRoot }
            : umbracoContext.Content?.GetAtRoot().ToArray() ?? Array.Empty<IPublishedContent>();

        var allPosts = siteRoots
            .SelectMany(root => root.DescendantsOrSelfOfType(PostPageAlias));

        // Category filter
        if (!string.IsNullOrWhiteSpace(request.CategoryAliasOrName))
        {
            var needle = request.CategoryAliasOrName.Trim();
            allPosts = allPosts.Where(p =>
            {
                var category = p.Parent;
                if (category is null || !string.Equals(category.ContentType.Alias, PostCategoryPageAlias, StringComparison.Ordinal))
                {
                    return false;
                }
                var categoryName = category.Value<string>("categoryName") ?? category.Name;
                return string.Equals(category.Name, needle, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(categoryName, needle, StringComparison.OrdinalIgnoreCase);
            });
        }

        // Tags filter (OR)
        var tagFilter = ParseCsv(request.TagsCsv);
        if (tagFilter.Count > 0)
        {
            allPosts = allPosts.Where(p =>
            {
                var postTags = p.Value<IEnumerable<string>>("tags") ?? Array.Empty<string>();
                return postTags.Any(t => tagFilter.Contains(t, StringComparer.OrdinalIgnoreCase));
            });
        }

        // Author filter (por referencia authorRef → authorPage)
        if (request.AuthorKey is Guid authorKey)
        {
            allPosts = allPosts.Where(p => p.Value<IPublishedContent>("authorRef")?.Key == authorKey);
        }

        // Sort: publishDate desc; nodos sin fecha al final por nombre
        var ordered = allPosts
            .Select(p => (Post: p, Date: p.Value<DateTime?>("publishDate")))
            .OrderByDescending(x => x.Date.HasValue)
            .ThenByDescending(x => x.Date ?? DateTime.MinValue)
            .ThenBy(x => x.Post.Name, StringComparer.OrdinalIgnoreCase);

        var maxItems = request.MaxItems > 0 ? request.MaxItems : 6;
        var skip = request.Skip > 0 ? request.Skip : 0;

        return ordered
            .Skip(skip)
            .Take(maxItems)
            .Select(x => Project(x.Post, x.Date))
            .ToArray();
    }

    public IReadOnlyList<PostSummary> GetByTag(string tag, int maxItems)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return Array.Empty<PostSummary>();
        }
        // Reusa el pipeline de GetPosts (single source of truth para el
        // recorrido del árbol + sort por publishDate). El filtro de tags
        // de GetPosts es exacto case-insensitive, OR sobre el CSV; un solo
        // tag ⇒ match exacto de ese tag. (Un tag con coma se trataría como
        // dos términos OR — caso patológico aceptado, los tags no llevan coma.)
        return GetPosts(new BlogQueryRequest(
            MaxItems: maxItems > 0 ? maxItems : 24,
            Skip: 0,
            TagsCsv: tag.Trim()));
    }

    public IReadOnlyList<PostSummary> GetRelated(Guid postKey, int maxItems)
    {
        if (!_umbracoContextAccessor.TryGetUmbracoContext(out var umbracoContext))
        {
            return Array.Empty<PostSummary>();
        }
        var current = umbracoContext.Content?.GetById(postKey);
        if (current is null || !string.Equals(current.ContentType.Alias, PostPageAlias, StringComparison.Ordinal))
        {
            return Array.Empty<PostSummary>();
        }

        var currentTags = new HashSet<string>(
            current.Value<IEnumerable<string>>("tags") ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        var parent = current.Parent;
        var categoryId = parent is { } cat && string.Equals(cat.ContentType.Alias, PostCategoryPageAlias, StringComparison.Ordinal)
            ? cat.Id : 0;

        var root = current.Root();
        var candidates = root is not null
            ? root.DescendantsOrSelfOfType(PostPageAlias).Where(p => p.Id != current.Id)
            : Enumerable.Empty<IPublishedContent>();

        var max = maxItems > 0 ? maxItems : 3;
        return candidates
            .Select(p =>
            {
                var tags = p.Value<IEnumerable<string>>("tags") ?? Array.Empty<string>();
                var shared = tags.Count(t => currentTags.Contains(t));
                var sameCategory = categoryId != 0 && p.Parent?.Id == categoryId ? 1 : 0;
                var date = p.Value<DateTime?>("publishDate");
                return (Post: p, Score: shared * 2 + sameCategory, Date: date);
            })
            .Where(x => x.Score > 0)                          // solo si comparte tag o categoría
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Date ?? DateTime.MinValue)
            .Take(max)
            .Select(x => Project(x.Post, x.Date))
            .ToArray();
    }

    private static PostSummary Project(IPublishedContent post, DateTime? publishDate)
    {
        var heroImage = post.Value<IPublishedContent>("heroImage");
        var category = post.Parent;
        var categoryName = category is { } c && string.Equals(c.ContentType.Alias, PostCategoryPageAlias, StringComparison.Ordinal)
            ? (c.Value<string>("categoryName") ?? c.Name)
            : null;

        var readTime = post.Value<int?>("readTimeMinutes");
        var tags = post.Value<IEnumerable<string>>("tags")?.ToArray() ?? Array.Empty<string>();

        return new PostSummary(
            Url: post.Url(),
            Title: post.Name ?? string.Empty,
            Excerpt: post.Value<string>("excerpt"),
            HeroImageUrl: heroImage?.Url(),
            PublishDate: publishDate,
            ReadTimeMinutes: readTime,
            CategoryName: categoryName,
            Tags: tags);
    }

    private static IReadOnlyCollection<string> ParseCsv(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
        {
            return Array.Empty<string>();
        }
        return csv
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();
    }
}
