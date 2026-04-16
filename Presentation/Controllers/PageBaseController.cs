using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Web;
using Umbraco.Cms.Web.Common.Controllers;
using Synergos.CMS.Application.Mapping;

namespace Synergos.CMS.Presentation.Controllers;

/// <summary>
/// Route hijacking for document type alias "pageBase".
/// Keeps rendering logic delegated to the assembler.
/// </summary>
public sealed class PageBaseController : RenderController
{
    private readonly PageAssembler _assembler;

    public PageBaseController(
        ILogger<PageBaseController> logger,
        ICompositeViewEngine compositeViewEngine,
        IUmbracoContextAccessor umbracoContextAccessor,
        PageAssembler assembler)
        : base(logger, compositeViewEngine, umbracoContextAccessor)
    {
        _assembler = assembler;
    }

    [OutputCache(PolicyName = "PageContent")]
    public override IActionResult Index()
    {
        if (CurrentPage is null) return NotFound();
        return CurrentTemplate(_assembler.AssemblePage(CurrentPage));
    }
}
