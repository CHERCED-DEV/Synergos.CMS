using Synergos.CMS.Interfaces;
using Umbraco.Cms.Core.Web;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Web-layer implementation of <see cref="IContentContextAccessor"/>
/// that reads from Umbraco's <see cref="IUmbracoContextAccessor"/>.
/// </summary>
/// <remarks>
/// Per ADR 0002, Umbraco types do not leak into
/// <c>Synergos.CMS.Application</c>. This adapter lives in Web and
/// exposes only the neutral <see cref="Guid"/> content key defined by
/// the <see cref="IContentContextAccessor"/> contract.
/// </remarks>
public sealed class UmbracoContentContextAccessor : IContentContextAccessor
{
    private readonly IUmbracoContextAccessor _umbracoContextAccessor;

    public UmbracoContentContextAccessor(IUmbracoContextAccessor umbracoContextAccessor) =>
        _umbracoContextAccessor = umbracoContextAccessor;

    public Guid? GetCurrentContentKey()
    {
        if (!_umbracoContextAccessor.TryGetUmbracoContext(out var umbracoContext))
        {
            return null;
        }

        var publishedRequest = umbracoContext.PublishedRequest;
        return publishedRequest?.PublishedContent?.Key;
    }
}
