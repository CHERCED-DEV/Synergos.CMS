using Synergos.Api.Catalog.Contracts;
using Synergos.Api.Catalog.Domain;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.Api.Catalog.Endpoints;

/// <summary>El ruteo del catálogo.</summary>
public static class CatalogEndpoints
{
    public static IEndpointRouteBuilder MapCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/items", (IndexItemRequest req, HttpRequest http, CatalogService svc) =>
        {
            if (!IdempotencyHeader.TryRead(http, CatalogRules.CodePrefix, out var key, out var falta)) return falta!;

            var subject = Ref.TryCreate(req.SubjectKind, req.SubjectId);
            if (subject is null) return Invalid("bad_subject", "Hacen falta subjectKind y subjectId.");

            return svc.Index(subject, req.Title, req.Summary, req.Tags, req.Facets, key).Match(
                i => Results.Created($"/v1/items/{i.Id}", CatalogItemResponse.From(i)),
                bad => bad.ToProblem());
        });

        app.MapGet("/v1/items/{id}", (string id, CatalogService svc) =>
            svc.Get(id).Map(CatalogItemResponse.From).ToHttp());

        app.MapGet("/v1/items", (string? q, string? tag, string? facet, int? offset, int? limit, CatalogService svc) =>
        {
            // tag y facet llegan repetibles: ?tag=a&tag=b y ?facet=ciudad:medellin. El separador
            // es ':' y se corta en el PRIMERO, porque un valor puede contenerlo.
            var tags = Split(tag);
            var facets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in Split(facet))
            {
                var cut = f.IndexOf(':');
                if (cut <= 0 || cut == f.Length - 1) return Invalid("bad_facet", $"'{f}' no tiene la forma clave:valor.");
                facets[f[..cut]] = f[(cut + 1)..];
            }

            return svc.Search(q, tags.Count > 0 ? tags : null, facets.Count > 0 ? facets : null,
                    Math.Max(0, offset ?? 0), QueryWindow.Limit(limit))
                .Map(p => new PageResponse<CatalogItemResponse>(
                    p.Items.Select(CatalogItemResponse.From).ToList(), p.Total, p.Offset, p.HasMore))
                .ToHttp();
        });

        app.MapPost("/v1/items/{id}/unindex", (string id, CatalogService svc) =>
            svc.Unindex(id).Map(CatalogItemResponse.From).ToHttp());

        return app;
    }

    private static List<string> Split(string? raw)
        => string.IsNullOrWhiteSpace(raw)
            ? new List<string>()
            : raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private static IResult Invalid(string code, string message)
        => Rejection.Invalid($"{CatalogRules.CodePrefix}.{code}", message).ToProblem();
}
