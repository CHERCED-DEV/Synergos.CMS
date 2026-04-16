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
/// Route hijacking for document type alias "blogHome".
/// Supports pagination via ?page=N query parameter.
/// </summary>
public sealed class BlogHomeController : RenderController
{
    private readonly BlogAssembler _assembler;
    private readonly IFeatureFlags _flags;

    public BlogHomeController(
        ILogger<BlogHomeController> logger,
        ICompositeViewEngine compositeViewEngine,
        IUmbracoContextAccessor umbracoContextAccessor,
        BlogAssembler assembler,
        IFeatureFlags flags)
        : base(logger, compositeViewEngine, umbracoContextAccessor)
    {
        _assembler = assembler;
        _flags     = flags;
    }

    [OutputCache(PolicyName = "BlogListing")]
    public override IActionResult Index()
    {
        if (CurrentPage is null) return NotFound();
        if (!_flags.IsEnabled("EnableBlog", defaultIfMissing: true)) return NotFound();

        var page = 1;
        if (HttpContext.Request.Query.TryGetValue("page", out var pageStr)
            && int.TryParse(pageStr, out var parsed))
        {
            page = parsed;
        }

        return CurrentTemplate(_assembler.AssembleBlogHome(CurrentPage, page));
    }
}
