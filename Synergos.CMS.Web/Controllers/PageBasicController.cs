using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Synergos.CMS.Application.Dto.Responses;
using Umbraco.Cms.Core.Web;
using Umbraco.Cms.Web.Common.Controllers;

namespace Synergos.CMS.Web.Controllers;

/// <summary>
/// Render controller for the <c>pageBasic</c> Document Type
/// (ADR 0014). Projects <c>CurrentPage</c> into a
/// <see cref="PageBasicResponse"/> and returns the current template.
/// </summary>
/// <remarks>
/// Umbraco resolves this controller by convention: any render
/// request for a content item whose Document Type alias is
/// <c>pageBasic</c> invokes <c>PageBasicController.Index()</c>.
/// No explicit route attribute is required.
///
/// Mapping is inline (one-liner into <see cref="PageBasicResponse.Project"/>)
/// — per ADR 0014 and ADR 0009's 3-question filter, no second
/// consumer justifies extracting an <c>IPageMapper</c> seam.
/// </remarks>
public sealed class PageBasicController : RenderController
{
    public PageBasicController(
        ILogger<PageBasicController> logger,
        ICompositeViewEngine compositeViewEngine,
        IUmbracoContextAccessor umbracoContextAccessor)
        : base(logger, compositeViewEngine, umbracoContextAccessor)
    {
    }

    public override IActionResult Index()
    {
        var response = PageBasicResponse.Project(
            alias => CurrentPage?.Value<string>(alias));

        return CurrentTemplate(response);
    }
}
