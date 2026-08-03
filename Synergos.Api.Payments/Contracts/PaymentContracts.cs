using Synergos.Api.Payments.Domain;

namespace Synergos.Api.Payments.Contracts;

// Lo que cruza el cable, separado de Domain/ (doc 08 §4.1). Acá la separación gana algo
// concreto: Payment lleva ProviderReference —el identificador de la pasarela— y esa es
// justamente la clase de dato que no tiene por qué salir a un cliente cualquiera.

/// <summary>Un monto tal como llega o sale. Nunca un decimal suelto.</summary>
public sealed record MoneyDto(decimal Amount, string Currency);

/// <summary>Autorizar un cobro.</summary>
public sealed record AuthorizeRequest(
    string? ForKind, string? ForId, string? PayerKind, string? PayerId, MoneyDto? Amount);

/// <summary>Devolver, total o parcialmente.</summary>
public sealed record RefundRequest(MoneyDto? Amount, string? Reason);

/// <summary>Cómo sale una devolución.</summary>
public sealed record RefundResponse(string Id, MoneyDto Amount, string? Reason, DateTimeOffset AtUtc);

/// <summary>Cómo sale un cobro. <b>Sin la referencia del proveedor.</b></summary>
public sealed record PaymentResponse(
    string Id, string ForKind, string ForId, string PayerKind, string PayerId,
    MoneyDto Amount, string Status, string Provider,
    MoneyDto Refunded, MoneyDto Refundable,
    IReadOnlyList<RefundResponse> Refunds,
    DateTimeOffset AuthorizedAtUtc, DateTimeOffset? CapturedAtUtc)
{
    public static PaymentResponse From(Payment p) => new(
        p.Id, p.For.Kind, p.For.Id, p.Payer.Kind, p.Payer.Id,
        new MoneyDto(p.Amount.Amount, p.Amount.Currency), p.Status.ToString(), p.Provider,
        new MoneyDto(p.Refunded.Amount, p.Refunded.Currency),
        new MoneyDto(p.Refundable.Amount, p.Refundable.Currency),
        p.Refunds.Select(r => new RefundResponse(r.Id, new MoneyDto(r.Amount.Amount, r.Amount.Currency), r.Reason, r.AtUtc)).ToList(),
        p.AuthorizedAtUtc, p.CapturedAtUtc);
}

/// <summary>Una porción de una lista, con su total.</summary>
public sealed record PageResponse<T>(IReadOnlyList<T> Items, int Total, int Offset, bool HasMore);
