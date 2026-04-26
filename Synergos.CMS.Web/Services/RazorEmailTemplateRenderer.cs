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
    private readonly IServiceProvider _serviceProvider;

    public RazorEmailTemplateRenderer(
        IRazorViewEngine viewEngine,
        ITempDataProvider tempDataProvider,
        IServiceProvider serviceProvider)
    {
        _viewEngine = viewEngine;
        _tempDataProvider = tempDataProvider;
        _serviceProvider = serviceProvider;
    }

    public async Task<string> RenderAsync<TModel>(
        string viewName,
        TModel model,
        CancellationToken cancellationToken)
    {
        var viewPath = $"{ViewsPrefix}{viewName}{ViewExtension}";

        var httpContext = new DefaultHttpContext { RequestServices = _serviceProvider };
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
