using Microsoft.AspNetCore.Http.HttpResults;
using Synergos.Api.Fulfillment.Contracts;
using Synergos.Api.Fulfillment.Domain;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.Api.Fulfillment.Endpoints;

/// <summary>El ruteo de los envíos.</summary>
/// <remarks>
/// Cada endpoint declara su respuesta con el TIPO DE RETORNO, su nombre con <c>WithName</c> y lo que lee
/// de la petición —la llave— con su metadato (núcleo N2, el molde F2 de la ADR 0140): de ahí sale el
/// contrato publicado (<c>docs/contracts/openapi/Synergos.Api.Fulfillment.json</c>). Las rutas, los estados y los cuerpos
/// son los de antes.
/// </remarks>
public static class FulfillmentEndpoints
{
    public static IEndpointRouteBuilder MapFulfillmentEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/shipments", Results<Created<ShipmentResponse>, ProblemHttpResult> (CreateShipmentRequest req, HttpRequest http, FulfillmentService svc) =>
        {
            if (!IdempotencyHeader.TryRead(http, FulfillmentRules.CodePrefix, out var key, out var falta)) return falta!;

            var forWhat = Ref.TryCreate(req.ForKind, req.ForId);
            if (forWhat is null) return Invalid("bad_for", "Hacen falta forKind y forId.");

            var to = req.To is null ? null : new Address(
                req.To.Line1 ?? string.Empty, req.To.Line2, req.To.City ?? string.Empty,
                req.To.Region, req.To.PostalCode, (req.To.Country ?? string.Empty).ToUpperInvariant(), req.To.Contact);

            return svc.Create(forWhat, to, req.Carrier, req.TrackingCode, key).Map(ShipmentResponse.From).ToCreated(s => $"/v1/shipments/{s.Id}");
        }).WithName("CreateShipment").ConLlaveDeIdempotencia();

        app.MapGet("/v1/shipments/{id}", (string id, FulfillmentService svc) =>
            svc.Get(id).Map(ShipmentResponse.From).ToHttp()).WithName("GetShipment");

        app.MapGet("/v1/shipments", (string? forKind, string? forId, int? offset, int? limit, FulfillmentService svc) =>
            svc.ListForSubject(Ref.TryCreate(forKind, forId), Math.Max(0, offset ?? 0), QueryWindow.Limit(limit))
                .Map(p => new PageResponse<ShipmentResponse>(
                    p.Items.Select(ShipmentResponse.From).ToList(), p.Total, p.Offset, p.HasMore))
                .ToHttp()).WithName("ListShipments");

        app.MapPost("/v1/shipments/{id}/track", Results<Ok<ShipmentResponse>, ProblemHttpResult> (string id, TrackRequest req, FulfillmentService svc) =>
        {
            if (!Enum.TryParse<ShipmentStatus>(req.Status, ignoreCase: true, out var estado))
            {
                return Invalid("bad_status", $"'{req.Status}' no es un estado. Son: {string.Join(", ", Enum.GetNames<ShipmentStatus>())}.");
            }

            return svc.Track(id, estado, req.Note).Map(ShipmentResponse.From).ToHttp();
        }).WithName("TrackShipment");

        return app;
    }

    private static ProblemHttpResult Invalid(string code, string message)
        => Rejection.Invalid($"{FulfillmentRules.CodePrefix}.{code}", message).ToProblem();
}
