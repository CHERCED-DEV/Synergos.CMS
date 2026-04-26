using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Controllers;

/// <summary>
/// Endpoint público de búsqueda. Wraps <see cref="ISearchQuery"/> y
/// devuelve JSON consumible desde:
/// <list type="bullet">
///   <item>El SearchPage Razor template (Ola 62) directamente via DI.</item>
///   <item>Frontend JS (autocomplete, instant-search) via fetch.</item>
///   <item>Synergos.UI cuando integre el bloque elementSynSearchBox.</item>
/// </list>
/// </summary>
/// <remarks>
/// Sin auth — la búsqueda es pública por default (solo indexa contenido
/// publicado, ya filtrado por <see cref="Application.Configuration.SearchSettings.ExcludedDocTypeAliases"/>).
/// Para sitios con contenido member-only, agregar gate en el controller
/// futuro.
/// </remarks>
[ApiController]
[Route("api/search")]
public sealed class SearchController : ControllerBase
{
    private readonly ISearchQuery _searchQuery;
    private readonly IAnalyticsTracker _analytics;

    public SearchController(ISearchQuery searchQuery, IAnalyticsTracker analytics)
    {
        _searchQuery = searchQuery;
        _analytics = analytics;
    }

    /// <summary>
    /// GET /api/search?q=foo&amp;maxItems=20&amp;skip=0&amp;docType=postPage
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    public ActionResult<SearchResponse> Search(
        [FromQuery(Name = "q")] string? query,
        [FromQuery] int maxItems = 20,
        [FromQuery] int skip = 0,
        [FromQuery(Name = "docType")] string? docTypeFilter = null)
    {
        var request = new SearchRequest(
            Query: query ?? string.Empty,
            MaxItems: maxItems,
            Skip: skip,
            DocTypeAliasFilter: string.IsNullOrWhiteSpace(docTypeFilter)
                ? null
                : docTypeFilter);

        var response = _searchQuery.Search(request);

        _analytics.Track("search.executed", new Dictionary<string, object?>
        {
            ["query"] = request.Query,
            ["resultCount"] = response.TotalEstimated,
            ["docTypeFilter"] = request.DocTypeAliasFilter,
            ["elapsedMs"] = response.ElapsedMilliseconds,
        });

        if (response.TotalEstimated == 0 && !string.IsNullOrWhiteSpace(request.Query))
        {
            _analytics.Track("search.no-results", new Dictionary<string, object?>
            {
                ["query"] = request.Query,
            });
        }

        return Ok(response);
    }
}
