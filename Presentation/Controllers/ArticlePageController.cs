using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Web;
using Umbraco.Cms.Web.Common.Controllers;
using Synergos.CMS.Application.Mapping;

namespace Synergos.CMS.Presentation.Controllers;

public sealed class ArticlePageController : RenderController
{
    private readonly PageAssembler _assembler;

    public ArticlePageController(
        ILogger<ArticlePageController> logger,
        ICompositeViewEngine compositeViewEngine,
        IUmbracoContextAccessor umbracoContextAccessor,
        PageAssembler assembler)
        : base(logger, compositeViewEngine, umbracoContextAccessor)
    {
        _assembler = assembler;
    }

    public override IActionResult Index()
    {
        if (CurrentPage is null) return NotFound();
        return CurrentTemplate(_assembler.AssembleArticlePage(CurrentPage));
    }
}
