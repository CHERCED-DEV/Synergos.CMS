using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Core.Web;
using Synergos.CMS.Domain.Services;
using Synergos.CMS.Presentation.ViewModels.Layout;

namespace Synergos.CMS.Presentation.ViewComponents;

/// <summary>
/// Renders a breadcrumb trail from the site root to the current page.
/// Uses INavigationService.GetBreadcrumbs() for the trail.
///
/// Usage in Razor: @await Component.InvokeAsync("Breadcrumbs")
/// </summary>
public sealed class BreadcrumbsViewComponent : ViewComponent
{
    private readonly INavigationService _navigationService;
    private readonly IUmbracoContextAccessor _umbracoContextAccessor;

    public BreadcrumbsViewComponent(
        INavigationService navigationService,
        IUmbracoContextAccessor umbracoContextAccessor)
    {
        _navigationService = navigationService;
        _umbracoContextAccessor = umbracoContextAccessor;
    }

    public Task<IViewComponentResult> InvokeAsync()
    {
        if (!_umbracoContextAccessor.TryGetUmbracoContext(out var ctx))
            return Task.FromResult<IViewComponentResult>(Content(string.Empty));

        var currentPage = ctx.PublishedRequest?.PublishedContent;
        if (currentPage is null)
            return Task.FromResult<IViewComponentResult>(Content(string.Empty));

        var breadcrumbs = _navigationService.GetBreadcrumbs(currentPage.Id);

        var model = breadcrumbs
            .Select(b => new LayoutNavigationItemViewModel(b.Title, b.Url, b.IsActive, []))
            .ToList();

        return Task.FromResult<IViewComponentResult>(View(model));
    }
}
