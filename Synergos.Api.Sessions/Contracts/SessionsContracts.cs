using Synergos.Api.Sessions.Domain;

namespace Synergos.Api.Sessions.Contracts;

// ─────────────────────────────────────────────────────────────────────────────
// Lo que cruza el cable, separado de Domain/ (doc 08 §4.1). Acá la separación se
// gana algo concreto y no teórico: el CMS ya consume esta forma en producción
// (HttpSearchAnalyticsStore, ADR 0130), así que renombrar un campo de QueryStat
// por dentro no puede poder romperlo.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Lo que un origen reporta al registrar una búsqueda.</summary>
public sealed record RecordSearchEventRequest(string? Query, int ResultCount, long ElapsedMs);

/// <summary>Lo que se responde a una ingesta.</summary>
/// <param name="Stored">Si se persistió. Un <c>false</c> no es un error del llamador.</param>
public sealed record IngestAcceptedResponse(bool Stored);

/// <summary>Un query agregado, tal como sale.</summary>
public sealed record QueryStatResponse(string Query, int Count, int LastResultCount, DateTime LastSeenUtc)
{
    public static QueryStatResponse From(QueryStat s) => new(s.Query, s.Count, s.LastResultCount, s.LastSeenUtc);
}
