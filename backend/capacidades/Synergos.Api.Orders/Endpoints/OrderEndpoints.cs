using Microsoft.AspNetCore.Http.HttpResults;
using Synergos.Api.Orders.Contracts;
using Synergos.Api.Orders.Domain;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.Api.Orders.Endpoints;

/// <summary>El ruteo de pedidos.</summary>
/// <remarks>
/// Cada endpoint declara su respuesta con el TIPO DE RETORNO, su nombre con <c>WithName</c> y lo que lee
/// de la petición —la llave— con su metadato (núcleo N2, el molde F2 de la ADR 0140): de ahí sale el
/// contrato publicado (<c>docs/contracts/openapi/Synergos.Api.Orders.json</c>). Las rutas, los estados y los cuerpos
/// son los de antes.
/// </remarks>
public static class OrderEndpoints
{
    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/orders", Results<Created<OrderResponse>, ProblemHttpResult> (PlaceOrderRequest req, HttpRequest http, OrderService svc) =>
        {
            if (!IdempotencyHeader.TryRead(http, OrderRules.CodePrefix, out var key, out var falta)) return falta!;

            var buyer = Ref.TryCreate(req.BuyerKind, req.BuyerId);
            if (buyer is null) return Invalid("bad_buyer", "Hacen falta buyerKind y buyerId.");

            var lines = new List<OrderLine>();
            foreach (var l in req.Lines ?? Array.Empty<OrderLineRequest>())
            {
                var subject = Ref.TryCreate(l.SubjectKind, l.SubjectId);
                if (subject is null) return Invalid("bad_subject", "Cada línea necesita subjectKind y subjectId.");
                if (!TryMoney(l.UnitPrice, out var unit, out var badUnit)) return badUnit!;
                lines.Add(new OrderLine(subject, l.Quantity ?? 0, unit));
            }
            if (lines.Count == 0) return Invalid("empty_order", "Un pedido sin líneas no pide nada.");
            if (!TryMoney(req.Total, out var total, out var badTotal)) return badTotal!;

            return svc.Place(buyer, lines, total, key).Map(OrderResponse.From).ToCreated(o => $"/v1/orders/{o.Id}");
        }).WithName("PlaceOrder").ConLlaveDeIdempotencia();

        app.MapGet("/v1/orders/{id}", (string id, OrderService svc) =>
            svc.Get(id).Map(OrderResponse.From).ToHttp()).WithName("GetOrder");

        app.MapGet("/v1/orders", (string? buyerKind, string? buyerId, int? offset, int? limit, OrderService svc) =>
            svc.ListForBuyer(Ref.TryCreate(buyerKind, buyerId), Math.Max(0, offset ?? 0), QueryWindow.Limit(limit))
                .Map(p => new PageResponse<OrderResponse>(
                    p.Items.Select(OrderResponse.From).ToList(), p.Total, p.Offset, p.HasMore))
                .ToHttp()).WithName("ListOrders");

        app.MapPost("/v1/orders/{id}/fulfill", (string id, OrderService svc) =>
            svc.Fulfill(id).Map(OrderResponse.From).ToHttp()).WithName("FulfillOrder");

        app.MapPost("/v1/orders/{id}/cancel", (string id, OrderService svc) =>
            svc.Cancel(id).Map(OrderResponse.From).ToHttp()).WithName("CancelOrder");

        return app;
    }

    private static bool TryMoney(MoneyDto? dto, out Money money, out ProblemHttpResult? bad)
    {
        money = default;
        bad = null;
        if (dto is null || string.IsNullOrWhiteSpace(dto.Currency))
        {
            bad = Invalid("bad_money", "Hace falta un monto con su moneda: {amount, currency}.");
            return false;
        }
        try
        {
            money = Money.Of(dto.Amount, dto.Currency);
            return true;
        }
        catch (ArgumentException)
        {
            bad = Invalid("bad_currency", $"'{dto.Currency}' no es un código ISO 4217.");
            return false;
        }
    }

    private static ProblemHttpResult Invalid(string code, string message)
        => Rejection.Invalid($"{OrderRules.CodePrefix}.{code}", message).ToProblem();
}
