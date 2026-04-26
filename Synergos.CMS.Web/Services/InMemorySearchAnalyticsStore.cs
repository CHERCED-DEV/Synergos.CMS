using System.Collections.Concurrent;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Implementación in-memory de <see cref="ISearchAnalyticsStore"/>.
/// ConcurrentDictionary&lt;normalizedQuery, AggregateRecord&gt;.
/// </summary>
/// <remarks>
/// In-memory significa: el state se pierde con restart del proceso y
/// NO se comparte entre instancias bajo load balancer. Para sites de
/// tráfico bajo/medio es suficiente; para producción multi-instancia
/// con retención larga, swap por adapter sobre TimescaleDB/Influx.
///
/// Cap de memoria: el dictionary puede crecer linealmente con el número
/// de queries únicos. Para mitigar, agregar TTL eviction si justifica.
/// Hoy KISS — el operador reinicia el proceso periódicamente.
/// </remarks>
public sealed class InMemorySearchAnalyticsStore : ISearchAnalyticsStore
{
    private sealed class AggregateRecord
    {
        public int Count;
        public int LastResultCount;
        public DateTime LastSeenUtc;
        public long TotalElapsedMs;
    }

    private readonly ConcurrentDictionary<string, AggregateRecord> _records =
        new(StringComparer.OrdinalIgnoreCase);

    public void Record(string query, int resultCount, long elapsedMilliseconds)
    {
        var normalized = NormalizeQuery(query);
        if (string.IsNullOrEmpty(normalized))
        {
            return;
        }

        _records.AddOrUpdate(
            key: normalized,
            addValueFactory: _ => new AggregateRecord
            {
                Count = 1,
                LastResultCount = resultCount,
                LastSeenUtc = DateTime.UtcNow,
                TotalElapsedMs = elapsedMilliseconds,
            },
            updateValueFactory: (_, existing) =>
            {
                existing.Count++;
                existing.LastResultCount = resultCount;
                existing.LastSeenUtc = DateTime.UtcNow;
                existing.TotalElapsedMs += elapsedMilliseconds;
                return existing;
            });
    }

    public IReadOnlyList<SearchQueryStat> GetTopQueries(DateTime fromUtc, DateTime toUtc, int limit)
    {
        return _records
            .Where(kvp => kvp.Value.LastSeenUtc >= fromUtc && kvp.Value.LastSeenUtc <= toUtc)
            .OrderByDescending(kvp => kvp.Value.Count)
            .Take(Math.Max(1, limit))
            .Select(kvp => new SearchQueryStat(
                Query: kvp.Key,
                Count: kvp.Value.Count,
                LastResultCount: kvp.Value.LastResultCount,
                LastSeenUtc: kvp.Value.LastSeenUtc))
            .ToList();
    }

    public IReadOnlyList<SearchQueryStat> GetTopNoResultQueries(DateTime fromUtc, DateTime toUtc, int limit)
    {
        return _records
            .Where(kvp => kvp.Value.LastSeenUtc >= fromUtc &&
                          kvp.Value.LastSeenUtc <= toUtc &&
                          kvp.Value.LastResultCount == 0)
            .OrderByDescending(kvp => kvp.Value.Count)
            .Take(Math.Max(1, limit))
            .Select(kvp => new SearchQueryStat(
                Query: kvp.Key,
                Count: kvp.Value.Count,
                LastResultCount: 0,
                LastSeenUtc: kvp.Value.LastSeenUtc))
            .ToList();
    }

    private static string NormalizeQuery(string raw) =>
        string.IsNullOrWhiteSpace(raw) ? string.Empty : raw.Trim().ToLowerInvariant();
}
