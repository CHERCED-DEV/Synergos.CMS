using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Umbraco.Cms.Core.Models.Blocks;
using Umbraco.Cms.Core.Models.PublishedContent;
using Synergos.CMS.Web.Filters;
using Synergos.CMS.Web.Services;
using Umbraco.Cms.Core.Web;

namespace Synergos.CMS.Web.Controllers;

/// <summary>
/// Renderiza páginas de error custom (transversalErrorPage) cuando
/// ASP.NET dispara un status code de error vía
/// <c>UseStatusCodePagesWithReExecute("/error/{0}")</c>.
/// </summary>
/// <remarks>
/// Pipeline:
/// 1. Lee el status code del path /error/{statusCode}.
/// 2. Setea Response.StatusCode al original (sin esto, el navegador
///    recibe 200 OK por la re-execute — semántica HTTP rota).
/// 3. Busca un transversalErrorPage publicado con el matching
///    statusCode dentro del siteRoot correspondiente al hostname
///    (<see cref="SitioDeLaPeticion"/>). Esto lo prometía y no lo hacía:
///    tomaba la primera del árbol entero, así que un sitio con dominio
///    propio pintaba la página de error del primer sitio (#188). Sin
///    dominio que case, sigue siendo la primera del árbol.
/// 4. Si encuentra → renderiza Views/Error.cshtml con el modelo.
/// 5. Si no encuentra → renderiza Views/Error.cshtml con un fallback
///    inline (título genérico + mensaje + botón home + buscador, si hay
///    una searchPage publicada a la que mandarlo).
///
/// Respeta el modelo Lego: cero schema editorial nuevo además del ya
/// commiteado en Ola 76.1. El sitio y la cultura los da la misma pieza que
/// a <c>/blog/tag/*</c>; y se pinta en la cultura del sitio
/// (<see cref="CulturaDelSitioAttribute"/>): salía en <c>en-US</c>.
/// </remarks>
[ApiController]
[Route("error")]
[CulturaDelSitio]
public sealed class ErrorController : ControllerBase
{
    private const string TransversalErrorPageAlias = "transversalErrorPage";
    private const string SiteRootAlias = "siteRoot";
    private const string SearchPageAlias = "searchPage";

    private readonly IUmbracoContextAccessor _umbracoContextAccessor;
    private readonly SitioDeLaPeticion _sitio;

    public ErrorController(IUmbracoContextAccessor umbracoContextAccessor, SitioDeLaPeticion sitio)
    {
        _umbracoContextAccessor = umbracoContextAccessor;
        _sitio = sitio;
    }

    [HttpGet("{statusCode:int}")]
    [AllowAnonymous]
    public IActionResult Show(int statusCode)
    {
        // Preserve la semántica HTTP — sin esto la re-execute devuelve 200.
        Response.StatusCode = statusCode;

        var raices = RaicesDelSitio();
        var page = ResolveErrorPage(raices, statusCode);
        var searchUrl = ResolveSearchUrl(raices);
        var viewModel = new ErrorPageViewModel(
            StatusCode: statusCode,
            Title: page?.Value<string>("errorTitle") ?? FallbackTitleFor(statusCode),
            BodyHtml: page?.Value<Microsoft.AspNetCore.Html.IHtmlContent>("errorBody"),
            BodyBlocks: page?.Value<BlockGridModel>("errorBlocks"),
            ShowSearchBox: searchUrl is not null && (page?.Value<bool>("showSearchBox") ?? statusCode == 404),
            ShowHomeLink: page?.Value<bool>("showHomeLink") ?? true,
            HomeUrl: ResolveHomeUrl(raices),
            SearchUrl: searchUrl);

        return new ViewResult
        {
            ViewName = "Error",
            ViewData = new ViewDataDictionary<ErrorPageViewModel>(
                metadataProvider: new Microsoft.AspNetCore.Mvc.ModelBinding.EmptyModelMetadataProvider(),
                modelState: new Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary())
            {
                Model = viewModel,
            },
        };
    }

    /// <summary>
    /// Dónde se busca: el siteRoot del hostname si hay dominio que case, o el árbol entero.
    /// </summary>
    private IReadOnlyList<IPublishedContent> RaicesDelSitio()
    {
        if (_sitio.Resolver().SiteRoot is { } sitio)
        {
            return new[] { sitio };
        }

        return _umbracoContextAccessor.TryGetUmbracoContext(out var ctx) && ctx.Content is not null
            ? ctx.Content.GetAtRoot().ToList()
            : Array.Empty<IPublishedContent>();
    }

    private static IPublishedContent? ResolveErrorPage(IReadOnlyList<IPublishedContent> raices, int statusCode)
    {
        var statusCodeStr = statusCode.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return raices
            .SelectMany(root => root.DescendantsOrSelfOfType(TransversalErrorPageAlias))
            .FirstOrDefault(p => string.Equals(
                p.Value<string>("statusCode"),
                statusCodeStr,
                StringComparison.Ordinal));
    }

    private static string ResolveHomeUrl(IReadOnlyList<IPublishedContent> raices)
    {
        var siteRoot = raices
            .SelectMany(r => r.DescendantsOrSelfOfType(SiteRootAlias))
            .FirstOrDefault();
        return siteRoot?.Url() ?? "/";
    }

    /// <summary>
    /// La página de búsqueda que el editor publicó (<c>searchPage</c>); <c>null</c> si no hay.
    /// </summary>
    /// <remarks>
    /// El buscador de la página de error mandaba a <c>/search</c> escrito a mano, y esa ruta no
    /// existe: ni hay controlador ahí ni contenido de tipo <c>searchPage</c> en la base, así que
    /// quien buscaba desde un 404 caía en otro 404 (Synergos.CMS#187, la misma clase que el
    /// carrito). Sin página de búsqueda el buscador no se pinta.
    /// </remarks>
    private static string? ResolveSearchUrl(IReadOnlyList<IPublishedContent> raices)
    {
        return raices
            .SelectMany(r => r.DescendantsOrSelfOfType(SearchPageAlias))
            .FirstOrDefault()?.Url();
    }

    private static string FallbackTitleFor(int statusCode) => statusCode switch
    {
        404 => "Página no encontrada",
        500 => "Algo salió mal",
        503 => "Servicio temporalmente no disponible",
        _ => $"Error {statusCode}",
    };
}

/// <summary>
/// View model que pasa <c>ErrorController.Show</c> a
/// <c>Views/Error.cshtml</c>.
/// </summary>
public sealed record ErrorPageViewModel(
    int StatusCode,
    string Title,
    Microsoft.AspNetCore.Html.IHtmlContent? BodyHtml,
    BlockGridModel? BodyBlocks,
    bool ShowSearchBox,
    bool ShowHomeLink,
    string HomeUrl,
    string? SearchUrl = null);
