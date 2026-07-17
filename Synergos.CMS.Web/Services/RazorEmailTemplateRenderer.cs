using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Default <see cref="IEmailTemplateRenderer"/> usando
/// <see cref="IRazorViewEngine"/> de ASP.NET Core. Compila y ejecuta
/// las vistas <c>Views/Emails/*.cshtml</c> sin requerir HTTP context
/// del request actual (importante para emails enviados desde
/// background jobs).
/// </summary>
/// <remarks>
/// Construye un <see cref="HttpContext"/> sintético cuando no hay uno
/// activo (ej. dentro de un <see cref="IHostedService"/>). El template
/// puede asumir Url helper inválido — para URLs absolutas en emails,
/// el caller las construye y las pasa via el model.
/// </remarks>
public sealed class RazorEmailTemplateRenderer : IEmailTemplateRenderer
{
    private const string ViewsPrefix = "~/Views/Emails/";
    private const string ViewExtension = ".cshtml";

    private readonly IRazorViewEngine _viewEngine;
    private readonly ITempDataProvider _tempDataProvider;
    private readonly IServiceScopeFactory _scopeFactory;

    public RazorEmailTemplateRenderer(
        IRazorViewEngine viewEngine,
        ITempDataProvider tempDataProvider,
        IServiceScopeFactory scopeFactory)
    {
        _viewEngine = viewEngine;
        _tempDataProvider = tempDataProvider;
        _scopeFactory = scopeFactory;
    }

    public async Task<string> RenderAsync<TModel>(
        string viewName,
        TModel model,
        CancellationToken cancellationToken)
    {
        var viewPath = $"{ViewsPrefix}{viewName}{ViewExtension}";

        // Este renderer es Singleton, así que un IServiceProvider inyectado sería el ROOT.
        // Razor necesita servicios SCOPED (IViewBufferScope) y resolverlos del root lanza
        // "Cannot resolve scoped service from root provider" en cuanto la validación de
        // scopes está activa (Development la activa por defecto) — dejando el render de
        // TODOS los emails roto, y en silencio porque los callers lo envuelven en catch.
        // Con un scope propio funciona igual desde un request que desde un hosted service.
        using var scope = _scopeFactory.CreateScope();
        var httpContext = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        var actionContext = new ActionContext(
            httpContext,
            new RouteData(),
            new ActionDescriptor());

        var viewResult = _viewEngine.GetView(executingFilePath: null, viewPath, isMainPage: true);
        if (!viewResult.Success)
        {
            viewResult = _viewEngine.FindView(actionContext, viewName, isMainPage: true);
        }
        if (!viewResult.Success)
        {
            throw new InvalidOperationException(
                $"Email template not found: {viewPath}. " +
                $"Ensure the file exists under Views/Emails/.");
        }

        await using var writer = new StringWriter();
        var viewData = new ViewDataDictionary<TModel>(
            new EmptyModelMetadataProvider(),
            new ModelStateDictionary())
        {
            Model = model,
        };
        var tempData = new TempDataDictionary(httpContext, _tempDataProvider);

        var viewContext = new ViewContext(
            actionContext,
            viewResult.View,
            viewData,
            tempData,
            writer,
            new HtmlHelperOptions());

        await viewResult.View.RenderAsync(viewContext);
        return writer.ToString();
    }
}
