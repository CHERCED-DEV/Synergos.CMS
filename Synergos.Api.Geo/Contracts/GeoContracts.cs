using Synergos.Api.Geo.Domain;

namespace Synergos.Api.Geo.Contracts;

// Lo que cruza el cable, separado de Domain/ (doc 08 §4.1).

/// <summary>Fijar la ubicación de algo.</summary>
public sealed record SetPlaceRequest(
    string? SubjectKind, string? SubjectId, double? Latitude, double? Longitude,
    string? Label, IReadOnlyDictionary<string, string>? Facets);

/// <summary>Cómo sale un sitio.</summary>
public sealed record PlaceResponse(
    string Id, string SubjectKind, string SubjectId, double Latitude, double Longitude,
    string? Label, IReadOnlyDictionary<string, string> Facets, DateTimeOffset UpdatedAtUtc)
{
    public static PlaceResponse From(Place p) => new(
        p.Id, p.Subject.Kind, p.Subject.Id, p.Latitude, p.Longitude, p.Label, p.Facets, p.UpdatedAtUtc);
}

/// <summary>Un sitio con su distancia.</summary>
public sealed record NearbyResponse(PlaceResponse Place, double DistanceKm)
{
    public static NearbyResponse From(NearbyPlace n) => new(PlaceResponse.From(n.Place), Math.Round(n.DistanceKm, 3));
}

/// <summary>Una porción de una lista, con su total.</summary>
public sealed record PageResponse<T>(IReadOnlyList<T> Items, int Total, int Offset, bool HasMore);
