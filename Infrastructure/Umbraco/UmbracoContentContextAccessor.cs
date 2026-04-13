using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Web;
using Synergos.CMS.Application;

namespace Synergos.CMS.Infrastructure.Umbraco;

/// <summary>
/// Implements <see cref="IContentContextAccessor"/> using Umbraco's ambient
/// <see cref="IUmbracoContextAccessor"/>.  All three methods call TrySatisfy on each
/// invocation — no context is captured at construction time, so Singleton lifetime is safe.
/// </summary>
public sealed class UmbracoContentContextAccessor : IContentContextAccessor
{
    private readonly IUmbracoContextAccessor _ctx;

    public UmbracoContentContextAccessor(IUmbracoContextAccessor ctx) => _ctx = ctx;

    public IPublishedContent? GetById(int id)
    {
        if (!_ctx.TryGetUmbracoContext(out var ctx)) return null;
        return ctx.Content?.GetById(id);
    }

    public IPublishedContent? GetCurrentPage()
    {
        if (!_ctx.TryGetUmbracoContext(out var ctx)) return null;
        return ctx.PublishedRequest?.PublishedContent;
    }

    public IEnumerable<IPublishedContent> GetAtRoot()
    {
        if (!_ctx.TryGetUmbracoContext(out var ctx)) return [];
        return ctx.Content?.GetAtRoot() ?? [];
    }
}
