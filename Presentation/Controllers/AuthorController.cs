using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Web;
using Umbraco.Cms.Web.Common.Controllers;
using Synergos.CMS.Application.Mapping;

namespace Synergos.CMS.Presentation.Controllers;

/// <summary>
/// Route hijacking for document type alias "author".
/// Renders the author profile + the feed of posts authored by them
/// (resolved by walking the site root for posts whose postAuthor picks
/// this author).
/// </summary>
public sealed class AuthorController : RenderController
{
    private readonly BlogAssembler _assembler;

    public AuthorController(
        ILogger<AuthorController> logger,
        ICompositeViewEngine compositeViewEngine,
        IUmbracoContextAccessor umbracoContextAccessor,
        BlogAssembler assembler)
        : base(logger, compositeViewEngine, umbracoContextAccessor)
    {
        _assembler = assembler;
    }

    [OutputCache(PolicyName = "BlogListing")]
    public override IActionResult Index()
    {
        if (CurrentPage is null) return NotFound();
        return CurrentTemplate(_assembler.AssembleAuthor(CurrentPage));
    }
}
