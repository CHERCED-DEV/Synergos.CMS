using Umbraco.Cms.Core.Models.PublishedContent;

namespace Synergos.CMS.Application.Output;

/// <summary>
/// View model for an Author profile page. Renders the author's profile
/// (name, role, bio, photo, social) and a feed of their posts resolved
/// from the Blog tree.
/// </summary>
public sealed class AuthorPageView(IPublishedContent content) : ContentPageView(content)
{
    /// <summary>Resolved profile metadata.</summary>
    public required BlogAuthorInfo Profile { get; init; }

    /// <summary>Posts authored by this author across all blogs/categories.</summary>
    public IReadOnlyList<BlogPostSummary> Posts { get; init; } = [];
}
