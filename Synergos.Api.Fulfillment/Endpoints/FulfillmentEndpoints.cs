using Synergos.Api.Fulfillment.Contracts;
using Synergos.Api.Fulfillment.Domain;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.Api.Fulfillment.Endpoints;

/// <summary>El ruteo de los envíos.</summary>
public static class FulfillmentEndpoints
{
    public static IEndpointRouteBuilder MapFulfillmentEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/shipments", (CreateShipmentRequest req, HttpRequest http, FulfillmentService svc) =>
        {
            if (!IdempotencyHeader.TryRead(http, FulfillmentRules.CodePrefix, out var key, out var falta)) return falta!;

            var forWhat = Ref.TryCreate(req.ForKind, req.ForId);
            if (forWhat is null) return Invalid("bad_for", "Hacen falta forKind y forId.");

            var to = req.To is null ? null : new Address(
                req.To.Line1 ?? string.Empty, req.To.Line2, req.To.City ?? string.Empty,
                req.To.Region, req.To.PostalCode, (req.To.Country ?? string.Empty).ToUpperInvariant(), req.To.Contact);

            return svc.Create(forWhat, to, req.Carrier, req.TrackingCode, key).Match(
                s => Results.Created($"/v1/shipments/{s.Id}", ShipmentResponse.From(s)),
                bad => bad.ToProblem());
        });

        app.MapGet("/v1/shipments/{id}", (string id, FulfillmentService svc) =>
            svc.Get(id).Map(ShipmentResponse.From).ToHttp());

        app.MapGet("/v1/shipments", (string? forKind, string? forId, int? offset, int? limit, FulfillmentService svc) =>
            svc.ListForSubject(Ref.TryCreate(forKind, forId), Math.Max(0, offset ?? 0), QueryWindow.Limit(limit))
                .Map(p => new PageResponse<ShipmentResponse>(
                    p.Items.Select(ShipmentResponse.From).ToList(), p.Total, p.Offset, p.HasMore))
                .ToHttp());

        app.MapPost("/v1/shipments/{id}/track", (string id, TrackRequest req, FulfillmentService svc) =>
        {
            if (!Enum.TryParse<ShipmentStatus>(req.Status, ignoreCase: true, out var estado))
            {
                return Invalid("bad_status", $"'{req.Status}' no es un estado. Son: {string.Join(", ", Enum.GetNames<ShipmentStatus>())}.");
            }

            return svc.Track(id, estado, req.Note).Map(ShipmentResponse.From).ToHttp();
        });

        return app;
    }

    private static IResult Invalid(string code, string message)
        => Rejection.Invalid($"{FulfillmentRules.CodePrefix}.{code}", message).ToProblem();
}
