using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Web;
using Umbraco.Cms.Web.Common.Controllers;
using Synergos.CMS.Application.Mapping;

namespace Synergos.CMS.Presentation.Controllers;

/// <summary>
/// Route hijacking for document type alias "category".
/// Assembles the blog category listing with filtered + paginated posts.
/// </summary>
public sealed class CategoryController : RenderController
{
    private readonly BlogAssembler _assembler;

    public CategoryController(
        ILogger<CategoryController> logger,
        ICompositeViewEngine compositeViewEngine,
        IUmbracoContextAccessor umbracoContextAccessor,
        BlogAssembler assembler)
        : base(logger, compositeViewEngine, umbracoContextAccessor)
    {
        _assembler = assembler;
    }

    public override IActionResult Index()
    {
        if (CurrentPage is null) return NotFound();
        var currentPage = int.TryParse(Request.Query["page"], out var p) && p > 0 ? p : 1;
        return CurrentTemplate(_assembler.AssembleBlogCategory(CurrentPage, currentPage));
    }
}
