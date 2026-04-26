using System.Diagnostics;
using Examine;
using Examine.Search;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Web;
using Umbraco.Extensions;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Implementación por defecto de <see cref="ISearchQuery"/> sobre el
/// <c>ExternalIndex</c> de Examine que Umbraco mantiene auto-actualizado
/// vía notification handlers internos al publish/unpublish/delete.
/// </summary>
/// <remarks>
/// <para>
/// Sin custom indexer — el ExternalIndex out-of-the-box indexa todos
/// los IPublishedContent con todas sus propiedades. La query se hace
/// con <c>GroupedOr</c> sobre los <see cref="SearchSettings.SearchableFields"/>.
/// </para>
/// <para>
/// Hidratación: el ID devuelto por Examine se usa para resolver el
/// IPublishedContent desde el published cache. Esto da URL consistente
/// con cultura activa, respeta hostname binding y siteRoot.
/// </para>
/// </remarks>
public sealed class ExamineSearchProvider : ISearchQuery
{
    private readonly IExamineManager _examineManager;
    private readonly IUmbracoContextAccessor _umbracoContextAccessor;
    private readonly IOptions<SearchSettings> _options;
    private readonly ILogger<ExamineSearchProvider> _logger;

    public ExamineSearchProvider(
        IExamineManager examineManager,
        IUmbracoContextAccessor umbracoContextAccessor,
        IOptions<SearchSettings> options,
        ILogger<ExamineSearchProvider> logger)
    {
        _examineManager = examineManager;
        _umbracoContextAccessor = umbracoContextAccessor;
        _options = options;
        _logger = logger;
    }

    public SearchResponse Search(SearchRequest request)
    {
        var settings = _options.Value;
        var query = (request.Query ?? string.Empty).Trim();
        if (query.Length == 0)
        {
            return new SearchResponse(query, Array.Empty<SearchHit>(), 0, 0);
        }

        if (!_examineManager.TryGetIndex(Constants.UmbracoIndexes.ExternalIndexName, out var index))
        {
            _logger.LogWarning("Examine ExternalIndex not available — search returns empty.");
            return new SearchResponse(query, Array.Empty<SearchHit>(), 0, 0);
        }

        if (!_umbracoContextAccessor.TryGetUmbracoContext(out var ctx) || ctx.Content is null)
        {
            return new SearchResponse(query, Array.Empty<SearchHit>(), 0, 0);
        }

        var stopwatch = Stopwatch.StartNew();
        var terms = SplitTerms(query);
        if (terms.Length == 0)
        {
            return new SearchResponse(query, Array.Empty<SearchHit>(), 0, 0);
        }

        var maxItems = Math.Clamp(request.MaxItems, 1, settings.MaxHitsHardCap);
        var skip = Math.Max(0, request.Skip);
        var fetchSize = maxItems + skip;

        var examineQuery = index.Searcher
            .CreateQuery("content")
            .GroupedOr(settings.SearchableFields, terms);

        var results = examineQuery.Execute(QueryOptions.SkipTake(0, fetchSize));
        var totalEstimated = (int)results.TotalItemCount;

        var hits = new List<SearchHit>();
        foreach (var result in results.Skip(skip))
        {
            if (hits.Count >= maxItems) { break; }

            if (!int.TryParse(result.Id, out var nodeId)) { continue; }
            var content = ctx.Content.GetById(nodeId);
            if (content is null) { continue; }

            var alias = content.ContentType.Alias;
            if (settings.ExcludedDocTypeAliases.Contains(alias, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }
            if (!string.IsNullOrWhiteSpace(request.DocTypeAliasFilter) &&
                !string.Equals(alias, request.DocTypeAliasFilter, StringComparison.Ordinal))
            {
                continue;
            }

            hits.Add(BuildHit(content, result.Score, settings.ExcerptMaxLength));
        }

        stopwatch.Stop();
        return new SearchResponse(
            Query: query,
            Hits: hits,
            TotalEstimated: totalEstimated,
            ElapsedMilliseconds: stopwatch.ElapsedMilliseconds);
    }

    private static SearchHit BuildHit(IPublishedContent content, float score, int excerptMaxLength)
    {
        var title = content.Value<string>("seoTitle") ?? content.Name ?? "—";
        var excerptRaw = content.Value<string>("excerpt")
            ?? content.Value<string>("seoDescription")
            ?? content.Value<string>("summaryText");

        string? excerpt = null;
        if (!string.IsNullOrWhiteSpace(excerptRaw))
        {
            excerpt = excerptRaw.Length > excerptMaxLength
                ? excerptRaw[..excerptMaxLength].TrimEnd() + "…"
                : excerptRaw;
        }

        return new SearchHit(
            Url: content.Url(),
            Title: title,
            Excerpt: excerpt,
            DocTypeAlias: content.ContentType.Alias,
            DocTypeName: content.ContentType.Alias,
            Score: score);
    }

    private static string[] SplitTerms(string query) =>
        query.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries
            | StringSplitOptions.TrimEntries);
}
