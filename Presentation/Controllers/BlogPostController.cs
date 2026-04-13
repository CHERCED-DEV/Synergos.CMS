using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Web;
using Umbraco.Cms.Web.Common.Controllers;
using Synergos.CMS.Application.Mapping;

namespace Synergos.CMS.Presentation.Controllers;

/// <summary>
/// Route hijacking for document type alias "blogPost".
/// Assembles the full blog post view with author, taxonomy, and related posts.
/// </summary>
public sealed class BlogPostController : RenderController
{
    private readonly BlogAssembler _assembler;

    public BlogPostController(
        ILogger<BlogPostController> logger,
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
        return CurrentTemplate(_assembler.AssembleBlogPost(CurrentPage));
    }
}
