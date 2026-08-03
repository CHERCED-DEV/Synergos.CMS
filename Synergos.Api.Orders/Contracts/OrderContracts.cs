using Synergos.Api.Orders.Domain;

namespace Synergos.Api.Orders.Contracts;

// Lo que cruza el cable, separado de Domain/ (doc 08 §4.1).

/// <summary>Un monto tal como llega o sale. Nunca un decimal suelto.</summary>
public sealed record MoneyDto(decimal Amount, string Currency);

/// <summary>Una línea del pedido.</summary>
public sealed record OrderLineRequest(string? SubjectKind, string? SubjectId, int? Quantity, MoneyDto? UnitPrice);

/// <summary>Registrar un pedido.</summary>
public sealed record PlaceOrderRequest(
    string? BuyerKind, string? BuyerId, IReadOnlyList<OrderLineRequest>? Lines, MoneyDto? Total);

/// <summary>Cómo sale una línea.</summary>
public sealed record OrderLineResponse(string SubjectKind, string SubjectId, int Quantity, MoneyDto UnitPrice, MoneyDto Subtotal);

/// <summary>Cómo sale un pedido.</summary>
public sealed record OrderResponse(
    string Id, string BuyerKind, string BuyerId, IReadOnlyList<OrderLineResponse> Lines,
    MoneyDto Total, string Status, DateTimeOffset PlacedAtUtc, DateTimeOffset? ClosedAtUtc)
{
    public static OrderResponse From(Order o) => new(
        o.Id, o.Buyer.Kind, o.Buyer.Id,
        o.Lines.Select(l => new OrderLineResponse(
            l.Subject.Kind, l.Subject.Id, l.Quantity,
            new MoneyDto(l.UnitPrice.Amount, l.UnitPrice.Currency),
            new MoneyDto(l.Subtotal.Amount, l.Subtotal.Currency))).ToList(),
        new MoneyDto(o.Total.Amount, o.Total.Currency), o.Status.ToString(), o.PlacedAtUtc, o.ClosedAtUtc);
}

/// <summary>Una porción de una lista, con su total.</summary>
public sealed record PageResponse<T>(IReadOnlyList<T> Items, int Total, int Offset, bool HasMore);
