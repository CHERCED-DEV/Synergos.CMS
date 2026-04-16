using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Web;
using Umbraco.Cms.Web.Common.Controllers;
using Synergos.CMS.Application.Mapping;
using Synergos.CMS.Domain.Services;

namespace Synergos.CMS.Presentation.Controllers;

/// <summary>
/// Route hijacking for document type alias "blogPost".
/// Assembles the full blog post view with author, taxonomy, and related posts.
/// </summary>
public sealed class BlogPostController : RenderController
{
    private readonly BlogAssembler _assembler;
    private readonly IFeatureFlags _flags;

    public BlogPostController(
        ILogger<BlogPostController> logger,
        ICompositeViewEngine compositeViewEngine,
        IUmbracoContextAccessor umbracoContextAccessor,
        BlogAssembler assembler,
        IFeatureFlags flags)
        : base(logger, compositeViewEngine, umbracoContextAccessor)
    {
        _assembler = assembler;
        _flags     = flags;
    }

    [OutputCache(PolicyName = "BlogPost")]
    public override IActionResult Index()
    {
        if (CurrentPage is null) return NotFound();
        if (!_flags.IsEnabled("EnableBlog", defaultIfMissing: true)) return NotFound();
        return CurrentTemplate(_assembler.AssembleBlogPost(CurrentPage));
    }
}
