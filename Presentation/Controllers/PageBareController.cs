using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Web;
using Umbraco.Cms.Web.Common.Controllers;
using Synergos.CMS.Application.Mapping;

namespace Synergos.CMS.Presentation.Controllers;

/// <summary>
/// Route hijacking for document type alias "pageBare".
///
/// Reuses <see cref="PageAssembler"/> because the view model shape is identical
/// to PageBase — only the template differs (no master layout). The PageBare
/// Razor view (<c>/Views/PageBare.cshtml</c>) renders the same
/// <c>ContentPageView</c> under its own &lt;html&gt; root.
/// </summary>
public sealed class PageBareController : RenderController
{
    private readonly PageAssembler _assembler;

    public PageBareController(
        ILogger<PageBareController> logger,
        ICompositeViewEngine        compositeViewEngine,
        IUmbracoContextAccessor     umbracoContextAccessor,
        PageAssembler               assembler)
        : base(logger, compositeViewEngine, umbracoContextAccessor)
    {
        _assembler = assembler;
    }

    public override IActionResult Index()
    {
        if (CurrentPage is null) return NotFound();
        return CurrentTemplate(_assembler.AssemblePage(CurrentPage));
    }
}
