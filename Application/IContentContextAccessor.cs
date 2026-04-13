using Umbraco.Cms.Core.Models.PublishedContent;

namespace Synergos.CMS.Application;

/// <summary>
/// Abstracts access to the Umbraco published-content cache so that Application
/// services do not depend on <c>IUmbracoContextAccessor</c> (an Infrastructure concern).
///
/// Registered as Singleton — the implementation wraps an ambient-context accessor
/// whose TryGet is evaluated at call time, not at construction time.
/// </summary>
public interface IContentContextAccessor
{
    /// <summary>Returns the published content node with the given id, or null.</summary>
    IPublishedContent? GetById(int id);

    /// <summary>Returns the page being rendered by the current request, or null.</summary>
    IPublishedContent? GetCurrentPage();

    /// <summary>Returns all published root-level content nodes.</summary>
    IEnumerable<IPublishedContent> GetAtRoot();
}
