using Microsoft.AspNetCore.Mvc;
using Synergos.CMS.Domain.Services;

namespace Synergos.CMS.Presentation.ViewComponents;

/// <summary>
/// Emits CSS custom properties from ThemeSettings as an inline &lt;style&gt; block.
/// Injected into &lt;head&gt; via _Layout.cshtml.
///
/// Resolution: site root → child "themeSettings" (via IThemeService).
/// If no ThemeSettings node exists, emits nothing (design system defaults apply).
/// </summary>
public sealed class ThemeViewComponent : ViewComponent
{
    private readonly ISiteResolver _siteResolver;
    private readonly IThemeService _themeService;

    public ThemeViewComponent(ISiteResolver siteResolver, IThemeService themeService)
    {
        _siteResolver = siteResolver;
        _themeService = themeService;
    }

    public Task<IViewComponentResult> InvokeAsync()
    {
        var site  = _siteResolver.Resolve();
        var theme = _themeService.GetThemeConfig(site.RootNodeId);

        if (theme is null)
            return Task.FromResult<IViewComponentResult>(Content(string.Empty));

        return Task.FromResult<IViewComponentResult>(View(theme));
    }
}
