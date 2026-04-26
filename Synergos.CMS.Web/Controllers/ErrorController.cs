using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Web;
using Umbraco.Extensions;

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
///    statusCode dentro del transversalsRepository del siteRoot
///    correspondiente al hostname.
/// 4. Si encuentra → renderiza Views/Error.cshtml con el modelo.
/// 5. Si no encuentra → renderiza Views/Error.cshtml con un fallback
///    inline (título genérico + mensaje + botón home + buscador).
///
/// Respeta el modelo Lego: cero schema editorial nuevo además del ya
/// commiteado en Ola 76.1. Cero seam nuevo — usa
/// IUmbracoContextAccessor existente.
/// </remarks>
[ApiController]
[Route("error")]
public sealed class ErrorController : ControllerBase
{
    private const string TransversalErrorPageAlias = "transversalErrorPage";
    private const string SiteRootAlias = "siteRoot";

    private readonly IUmbracoContextAccessor _umbracoContextAccessor;

    public ErrorController(IUmbracoContextAccessor umbracoContextAccessor) =>
        _umbracoContextAccessor = umbracoContextAccessor;

    [HttpGet("{statusCode:int}")]
    [AllowAnonymous]
    public IActionResult Show(int statusCode)
    {
        // Preserve la semántica HTTP — sin esto la re-execute devuelve 200.
        Response.StatusCode = statusCode;

        var page = ResolveErrorPage(statusCode);
        var viewModel = new ErrorPageViewModel(
            StatusCode: statusCode,
            Title: page?.Value<string>("errorTitle") ?? FallbackTitleFor(statusCode),
            BodyHtml: page?.Value<Microsoft.AspNetCore.Html.IHtmlContent>("errorBody"),
            ShowSearchBox: page?.Value<bool>("showSearchBox") ?? statusCode == 404,
            ShowHomeLink: page?.Value<bool>("showHomeLink") ?? true,
            HomeUrl: ResolveHomeUrl());

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

    private IPublishedContent? ResolveErrorPage(int statusCode)
    {
        if (!_umbracoContextAccessor.TryGetUmbracoContext(out var ctx) || ctx.Content is null)
        {
            return null;
        }

        var statusCodeStr = statusCode.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return ctx.Content.GetAtRoot()
            .SelectMany(root => root.DescendantsOrSelfOfType(TransversalErrorPageAlias))
            .FirstOrDefault(p => string.Equals(
                p.Value<string>("statusCode"),
                statusCodeStr,
                StringComparison.Ordinal));
    }

    private string ResolveHomeUrl()
    {
        if (!_umbracoContextAccessor.TryGetUmbracoContext(out var ctx) || ctx.Content is null)
        {
            return "/";
        }

        var siteRoot = ctx.Content.GetAtRoot()
            .SelectMany(r => r.DescendantsOrSelfOfType(SiteRootAlias))
            .FirstOrDefault();
        return siteRoot?.Url() ?? "/";
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
    bool ShowSearchBox,
    bool ShowHomeLink,
    string HomeUrl);
