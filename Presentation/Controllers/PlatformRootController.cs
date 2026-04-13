using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Web;
using Umbraco.Cms.Web.Common.Controllers;
using Umbraco.Extensions;
using Synergos.CMS.Schema.Constants;

namespace Synergos.CMS.Presentation.Controllers;

/// <summary>
/// Route hijacking for document type alias "platformRoot".
/// Redirects to the first visible child (typically a SiteRoot).
/// Falls back to a static placeholder if no children exist.
/// </summary>
public sealed class PlatformRootController : RenderController
{
    private static readonly HashSet<string> NonNavigableAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ContentTypeKeys.Aliases.GlobalSettings,
        ContentTypeKeys.Aliases.SharedContentFolder,
        ContentTypeKeys.Aliases.PageTagsFolder
    };

    public PlatformRootController(
        ILogger<PlatformRootController> logger,
        ICompositeViewEngine compositeViewEngine,
        IUmbracoContextAccessor umbracoContextAccessor)
        : base(logger, compositeViewEngine, umbracoContextAccessor)
    {
    }

    public override IActionResult Index()
    {
        if (CurrentPage is null) return NotFound();

        var children = CurrentPage.Children;
        var target = children
            .FirstOrDefault(child => child.ContentType.Alias.Equals(ContentTypeKeys.Aliases.SiteRoot, StringComparison.OrdinalIgnoreCase))
            ?? children.FirstOrDefault(child => child.IsVisible() && !NonNavigableAliases.Contains(child.ContentType.Alias));

        if (target is not null)
            return Redirect(target.Url());

        return CurrentTemplate(CurrentPage);
    }
}
