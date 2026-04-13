using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Web;
using Umbraco.Cms.Web.Common.Controllers;
using Umbraco.Extensions;
using Synergos.CMS.Schema.Constants;

namespace Synergos.CMS.Presentation.Controllers;

/// <summary>
/// Route hijacking for document type alias "siteRoot".
/// Redirects to the first visible child page when present.
/// </summary>
public sealed class SiteRootController : RenderController
{
    private static readonly HashSet<string> NonNavigableAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ContentTypeKeys.Aliases.SiteSettingsAlias,
        ContentTypeKeys.Aliases.ThemeSettings
    };

    public SiteRootController(
        ILogger<SiteRootController> logger,
        ICompositeViewEngine compositeViewEngine,
        IUmbracoContextAccessor umbracoContextAccessor)
        : base(logger, compositeViewEngine, umbracoContextAccessor)
    {
    }

    public override IActionResult Index()
    {
        if (CurrentPage is null) return NotFound();

        var target = CurrentPage.Children
            .FirstOrDefault(child => child.IsVisible() && !NonNavigableAliases.Contains(child.ContentType.Alias));

        if (target is not null)
            return Redirect(target.Url());

        return CurrentTemplate(CurrentPage);
    }
}
