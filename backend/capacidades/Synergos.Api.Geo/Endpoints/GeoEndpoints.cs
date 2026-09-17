using Synergos.Api.Geo.Contracts;
using Synergos.Api.Geo.Domain;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.Api.Geo.Endpoints;

/// <summary>El ruteo de las ubicaciones.</summary>
public static class GeoEndpoints
{
    public static IEndpointRouteBuilder MapGeoEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/places", (SetPlaceRequest req, HttpRequest http, GeoService svc) =>
        {
            if (!IdempotencyHeader.TryRead(http, GeoRules.CodePrefix, out var key, out var falta)) return falta!;

            var subject = Ref.TryCreate(req.SubjectKind, req.SubjectId);
            if (subject is null) return Invalid("bad_subject", "Hacen falta subjectKind y subjectId.");

            return svc.SetPlace(subject, req.Latitude, req.Longitude, req.Label, req.Facets, key).Match(
                p => Results.Created($"/v1/places/{p.Id}", PlaceResponse.From(p)),
                bad => bad.ToProblem());
        });

        app.MapGet("/v1/places/{id}", (string id, GeoService svc) =>
            svc.Get(id).Map(PlaceResponse.From).ToHttp());

        app.MapGet("/v1/places", (double? lat, double? lon, double? radiusKm, string? facet,
            int? offset, int? limit, GeoService svc) =>
        {
            var facets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in (facet ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var cut = f.IndexOf(':');
                if (cut <= 0 || cut == f.Length - 1) return Invalid("bad_facet", $"'{f}' no tiene la forma clave:valor.");
                facets[f[..cut]] = f[(cut + 1)..];
            }

            return svc.Nearby(lat, lon, radiusKm, facets.Count > 0 ? facets : null,
                    Math.Max(0, offset ?? 0), QueryWindow.Limit(limit))
                .Map(p => new PageResponse<NearbyResponse>(
                    p.Items.Select(NearbyResponse.From).ToList(), p.Total, p.Offset, p.HasMore))
                .ToHttp();
        });

        return app;
    }

    private static IResult Invalid(string code, string message)
        => Rejection.Invalid($"{GeoRules.CodePrefix}.{code}", message).ToProblem();
}
