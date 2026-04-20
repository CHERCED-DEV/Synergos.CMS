using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Synergos.CMS.Application.Dto.Responses;
using Umbraco.Cms.Core.Web;
using Umbraco.Cms.Web.Common.Controllers;
using Umbraco.Extensions;

namespace Synergos.CMS.Web.Controllers;

/// <summary>
/// Render controller for the <c>pageBare</c> Document Type (Ola 8).
/// Projects <c>CurrentPage</c> into a <see cref="PageBareResponse"/>
/// and returns the current template.
/// </summary>
public sealed class PageBareController : RenderController
{
    public PageBareController(
        ILogger<PageBareController> logger,
        ICompositeViewEngine compositeViewEngine,
        IUmbracoContextAccessor umbracoContextAccessor)
        : base(logger, compositeViewEngine, umbracoContextAccessor)
    {
    }

    public override IActionResult Index()
    {
        var response = PageBareResponse.Project(
            alias => CurrentPage?.Value<string>(alias));

        return CurrentTemplate(response);
    }
}
