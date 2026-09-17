using Synergos.Api.Payments.Domain;

namespace Synergos.Api.Payments.Contracts;

// Lo que cruza el cable, separado de Domain/ (doc 08 §4.1). Acá la separación gana algo
// concreto: Payment lleva ProviderReference —el identificador de la pasarela— y esa es
// justamente la clase de dato que no tiene por qué salir a un cliente cualquiera.

/// <summary>Un monto tal como llega o sale. Nunca un decimal suelto.</summary>
public sealed record MoneyDto(decimal Amount, string Currency);

/// <summary>Autorizar un cobro.</summary>
/// <param name="Assertion">
/// Con qué dice el llamador que se afirmó la identidad de quien paga. <b>Se DECLARA, no se
/// decide</b>: lo único que se acepta sin prueba es el suelo (<c>CmsSession</c>), y afirmar algo
/// más fuerte sin presentar el token se rechaza. Quien decide es esta capacidad.
/// </param>
public sealed record AuthorizeRequest(
    string? ForKind, string? ForId, string? PayerKind, string? PayerId, MoneyDto? Amount,
    string? Assertion = null);

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
    DateTimeOffset AuthorizedAtUtc, DateTimeOffset? CapturedAtUtc,
    string? ActionUrl = null,
    // Se DEVUELVE, y no es adorno: una escritura cuyo unico camino de lectura no existe esta
    // enterrada, no guardada (#116). Si nadie puede leer con que se afirmo quien pago, el campo
    // no sirve para lo unico que existe — contestar esa pregunta el dia que alguien la haga.
    // Nulo viaja como nulo: «no consta» y «CmsSession» no son lo mismo.
    string? PaidWith = null)
{
    public static PaymentResponse From(Payment p) => new(
        p.Id, p.For.Kind, p.For.Id, p.Payer.Kind, p.Payer.Id,
        new MoneyDto(p.Amount.Amount, p.Amount.Currency), p.Status.ToString(), p.Provider,
        new MoneyDto(p.Refunded.Amount, p.Refunded.Currency),
        new MoneyDto(p.Refundable.Amount, p.Refundable.Currency),
        p.Refunds.Select(r => new RefundResponse(r.Id, new MoneyDto(r.Amount.Amount, r.Amount.Currency), r.Reason, r.AtUtc)).ToList(),
        p.AuthorizedAtUtc, p.CapturedAtUtc, p.ActionUrl, p.PaidWith?.ToString());
}

/// <summary>
/// Lo que se le contesta a la pasarela cuando su evento no cambia ningún cobro.
/// </summary>
/// <param name="Matched">Si el evento encontró a qué cobro se refería.</param>
/// <param name="Why">Por qué no, dicho donde se puede ver y no solo en un log.</param>
public sealed record WebhookAck(bool Matched, string Why);

/// <summary>Una porción de una lista, con su total.</summary>
public sealed record PageResponse<T>(IReadOnlyList<T> Items, int Total, int Offset, bool HasMore);
