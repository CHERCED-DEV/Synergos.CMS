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
/// Maps elementCompArticleList → ArticleListViewModel.
/// Resolves curated content from MultiContentPicker.
/// </summary>
public sealed class ArticleListMapper(
    ContentHeadingReader heading,
    DomClassReader       cls,
    DomVariantReader     variant) : ISectionMapper
{
    public string SupportedAlias => "elementCompArticleList";

    public ISection? Map(IPublishedElement e)
    {
        var showExcerpt = e.Value<bool>("showExcerpt");
        var showImage   = e.Value<bool>("showImage");
        var listLayout  = e.Value<string>("listLayout");

        var picks = e.Value<IEnumerable<IPublishedContent>>("articles");
        var articles = picks?.Select(ToSummary).ToList() ?? [];

        return new ComponentSection(new ArticleListViewModel
        {
            Heading     = heading.Read(e),
            Articles    = articles,
            ShowExcerpt = showExcerpt,
            ShowImage   = showImage,
            ListLayout  = listLayout,
            DomClass    = cls.Read(e),
            DomVariant  = variant.Read(e)
        });
    }

    private static BlogPostSummary ToSummary(IPublishedContent content)
    {
        // Works for both BlogPost and regular pages
        var isBlogPost = content.ContentType.Alias == ContentTypeKeys.Aliases.BlogPost;

        var title = isBlogPost
            ? content.Value<string>("postTitle") ?? content.Name
            : content.Value<string>("pageTitle") ?? content.Name;

        var excerpt = isBlogPost
            ? content.Value<string>("postExcerpt")
            : content.Value<string>("pageSubtitle");

        var author = isBlogPost
            ? content.Value<IPublishedContent>("postAuthor")
            : null;

        var parentNode = content.Parent;
        var category = isBlogPost && parentNode?.ContentType.Alias == ContentTypeKeys.Aliases.Category
            ? (parentNode.Value<string>("categoryName") ?? parentNode.Name)
            : null;

        return new BlogPostSummary(
            Title:            title,
            Excerpt:          excerpt,
            FeaturedImageUrl: content.Value<IPublishedContent>("featuredImage")?.Url(),
            Url:              content.Url(),
            PublishDate:      content.Value<DateTime>("contentDate"),
            AuthorName:       author?.Value<string>("authorName") ?? author?.Name,
            ReadingTime:      content.Value<int>("readingTime"),
            CategoryName:     category);
    }
}
