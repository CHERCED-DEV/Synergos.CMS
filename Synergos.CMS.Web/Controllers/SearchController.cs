using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
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
    private readonly ISearchAnalyticsStore _analyticsStore;
    private readonly IMemberAccessGate _gate;
    private readonly IOptions<SearchSettings> _options;

    public SearchController(
        ISearchQuery searchQuery,
        IAnalyticsTracker analytics,
        ISearchAnalyticsStore analyticsStore,
        IMemberAccessGate gate,
        IOptions<SearchSettings> options)
    {
        _searchQuery = searchQuery;
        _analytics = analytics;
        _analyticsStore = analyticsStore;
        _gate = gate;
        _options = options;
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

        // Ola 86 — record en store para queries posteriores
        // (top queries, no-result queries) via /api/search/analytics
        if (!string.IsNullOrWhiteSpace(request.Query))
        {
            _analyticsStore.Record(request.Query, response.TotalEstimated, response.ElapsedMilliseconds);
        }

        return Ok(response);
    }

    /// <summary>
    /// GET /api/search/analytics?from=2026-04-01&amp;to=2026-04-30&amp;limit=20
    /// — top queries + no-result queries en la ventana indicada.
    /// </summary>
    [HttpGet("analytics")]
    [AllowAnonymous]
    public ActionResult<SearchAnalyticsResponse> Analytics(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int limit = 20)
    {
        // Ola 88 — Gate por roles configurados en SearchSettings.
        // Vacío/null = endpoint abierto (mantener para dev/staging).
        var rolesCsv = _options.Value.AnalyticsAdminRolesCsv;
        if (!string.IsNullOrWhiteSpace(rolesCsv) && !_gate.HasAnyRole(rolesCsv))
        {
            return Forbid();
        }

        var fromUtc = (from ?? DateTime.UtcNow.AddDays(-30)).ToUniversalTime();
        var toUtc = (to ?? DateTime.UtcNow).ToUniversalTime();
        var clampedLimit = Math.Clamp(limit, 1, 100);

        var topQueries = _analyticsStore.GetTopQueries(fromUtc, toUtc, clampedLimit);
        var topNoResults = _analyticsStore.GetTopNoResultQueries(fromUtc, toUtc, clampedLimit);

        return Ok(new SearchAnalyticsResponse(
            FromUtc: fromUtc,
            ToUtc: toUtc,
            TopQueries: topQueries,
            TopNoResultQueries: topNoResults));
    }
}

/// <summary>
/// Respuesta del endpoint <c>/api/search/analytics</c>.
/// </summary>
public sealed record SearchAnalyticsResponse(
    DateTime FromUtc,
    DateTime ToUtc,
    IReadOnlyList<SearchQueryStat> TopQueries,
    IReadOnlyList<SearchQueryStat> TopNoResultQueries);
