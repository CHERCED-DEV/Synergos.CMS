using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Controllers;

/// <summary>
/// Tag page del blog (Ola 230, ADR 0100). Lista los posts del sitio
/// actual que llevan un tag dado, reutilizando el post-card canónico de
/// la categoría (clases <c>.syn-post-category*</c>). RenderController
/// MVC puro — sin DocType ni schema (preferimos ruta virtual a crear un
/// ContentType, ADR 0100). Consume <see cref="IBlogQuery.GetByTag"/>,
/// single source of truth con PostCategoryPage / BlogRss.
/// </summary>
/// <remarks>
/// Ruta <c>/blog/tag/{tag}</c>. El tag llega URL-encoded desde los pills
/// clicables de PostPage; ASP.NET lo decodifica. Layout = "_Layout" para
/// heredar el chrome del sitio. Sin caché (la query es O(N posts) sobre
/// el published cache en memoria).
/// </remarks>
[AllowAnonymous]
public sealed class BlogTagController : Controller
{
    private readonly IBlogQuery _blogQuery;
    private readonly IOptions<BlogSettings> _blogOptions;

    public BlogTagController(IBlogQuery blogQuery, IOptions<BlogSettings> blogOptions)
    {
        _blogQuery = blogQuery;
        _blogOptions = blogOptions;
    }

    [HttpGet("/blog/tag/{tag}")]
    public IActionResult Index(string tag)
    {
        var normalized = (tag ?? string.Empty).Trim();
        var max = Math.Max(1, _blogOptions.Value.CategoryPageSize) * 8; // cap generoso
        var posts = string.IsNullOrWhiteSpace(normalized)
            ? Array.Empty<PostSummary>()
            : _blogQuery.GetByTag(normalized, max);

        return View("BlogTag", new BlogTagViewModel(normalized, posts));
    }
}

/// <summary>Modelo de la tag page: el tag pedido + los posts que lo llevan.</summary>
public sealed record BlogTagViewModel(
    string Tag,
    IReadOnlyList<PostSummary> Posts);
