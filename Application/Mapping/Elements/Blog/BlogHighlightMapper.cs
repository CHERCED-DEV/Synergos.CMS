using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Extensions;
using Synergos.CMS.Application.Elements;
using Synergos.CMS.Application.Mapping.Compositions.Content;
using Synergos.CMS.Application.Mapping.Compositions.Dom;
using Synergos.CMS.Application.Output;
using Synergos.CMS.Domain.Sections;
using Synergos.CMS.Schema.Constants;

namespace Synergos.CMS.Application.Mapping.Elements;

/// <summary>
/// Maps elementCompBlogHighlight → BlogHighlightViewModel.
/// Resolves blog posts from the picked BlogHome node.
/// </summary>
public sealed class BlogHighlightMapper(
    ContentHeadingReader heading,
    DomClassReader       cls,
    DomVariantReader     variant) : ISectionMapper
{
    public string SupportedAlias => "elementCompBlogHighlight";

    public ISection? Map(IPublishedElement e)
    {
        var maxPosts    = e.Value<int>("maxPosts");
        if (maxPosts <= 0) maxPosts = 3;
        var showExcerpt = e.Value<bool>("showExcerpt");
        var showImage   = e.Value<bool>("showImage");
        var listLayout  = e.Value<string>("listLayout");

        // Resolve blog source and fetch posts
        var blogHome = e.Value<IPublishedContent>("blogSource");
        var posts = ResolvePosts(blogHome, maxPosts);

        return new ComponentSection(new BlogHighlightViewModel
        {
            Heading     = heading.Read(e),
            Posts       = posts,
            ShowExcerpt = showExcerpt,
            ShowImage   = showImage,
            ListLayout  = listLayout,
            DomClass    = cls.Read(e),
            DomVariant  = variant.Read(e)
        });
    }

    private static IReadOnlyList<BlogPostSummary> ResolvePosts(IPublishedContent? blogHome, int max)
    {
        if (blogHome is null) return [];

        // 2-level traversal: BlogHome → Category* → BlogPost*
        return blogHome.Children?
            .Where(c => c.ContentType.Alias == ContentTypeKeys.Aliases.Category && c.IsVisible())
            .SelectMany(cat => cat.Children?
                .Where(p => p.ContentType.Alias == ContentTypeKeys.Aliases.BlogPost && p.IsVisible())
                ?? Enumerable.Empty<IPublishedContent>())
            .OrderByDescending(c => c.Value<DateTime>("contentDate"))
            .ThenByDescending(c => c.CreateDate)
            .Take(max)
            .Select(ToSummary)
            .ToList() ?? [];
    }

    private static BlogPostSummary ToSummary(IPublishedContent post)
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
            CategoryName:     category);
    }
}
