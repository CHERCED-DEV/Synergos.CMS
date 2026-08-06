using Synergos.Api.Orders.Contracts;
using Synergos.Api.Orders.Domain;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.Api.Orders.Endpoints;

/// <summary>El ruteo de pedidos.</summary>
public static class OrderEndpoints
{
    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/orders", (PlaceOrderRequest req, HttpRequest http, OrderService svc) =>
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

            return svc.Place(buyer, lines, total, key).Match(
                o => Results.Created($"/v1/orders/{o.Id}", OrderResponse.From(o)),
                bad => bad.ToProblem());
        });

        app.MapGet("/v1/orders/{id}", (string id, OrderService svc) =>
            svc.Get(id).Map(OrderResponse.From).ToHttp());

        app.MapGet("/v1/orders", (string? buyerKind, string? buyerId, int? offset, int? limit, OrderService svc) =>
            svc.ListForBuyer(Ref.TryCreate(buyerKind, buyerId), Math.Max(0, offset ?? 0), QueryWindow.Limit(limit))
                .Map(p => new PageResponse<OrderResponse>(
                    p.Items.Select(OrderResponse.From).ToList(), p.Total, p.Offset, p.HasMore))
                .ToHttp());

        app.MapPost("/v1/orders/{id}/fulfill", (string id, OrderService svc) =>
            svc.Fulfill(id).Map(OrderResponse.From).ToHttp());

        app.MapPost("/v1/orders/{id}/cancel", (string id, OrderService svc) =>
            svc.Cancel(id).Map(OrderResponse.From).ToHttp());

        return app;
    }

    private static bool TryMoney(MoneyDto? dto, out Money money, out IResult? bad)
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

    private static IResult Invalid(string code, string message)
        => Rejection.Invalid($"{OrderRules.CodePrefix}.{code}", message).ToProblem();
}
