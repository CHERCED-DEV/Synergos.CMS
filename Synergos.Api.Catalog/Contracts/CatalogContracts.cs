using Synergos.Api.Catalog.Domain;

namespace Synergos.Api.Catalog.Contracts;

// Lo que cruza el cable, separado de Domain/ (doc 08 §4.1).

/// <summary>Indexar (o reindexar) algo.</summary>
public sealed record IndexItemRequest(
    string? SubjectKind, string? SubjectId, string? Title, string? Summary,
    IReadOnlyList<string>? Tags, IReadOnlyDictionary<string, string>? Facets);

/// <summary>Cómo sale un ítem.</summary>
public sealed record CatalogItemResponse(
    string Id, string SubjectKind, string SubjectId, string Title, string? Summary,
    IReadOnlyList<string> Tags, IReadOnlyDictionary<string, string> Facets, DateTimeOffset IndexedAtUtc)
{
    public static CatalogItemResponse From(CatalogItem i) => new(
        i.Id, i.Subject.Kind, i.Subject.Id, i.Title, i.Summary, i.Tags, i.Facets, i.IndexedAtUtc);
}

/// <summary>Una porción de una lista, con su total.</summary>
public sealed record PageResponse<T>(IReadOnlyList<T> Items, int Total, int Offset, bool HasMore);
