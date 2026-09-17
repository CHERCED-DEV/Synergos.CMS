using Synergos.Bff.Core;
using Synergos.Bff.Eventos.Domain;

namespace Synergos.Bff.Eventos.Contracts;

// Lo que cruza el cable, separado de Domain/ (doc 08 §4.1). Acá la separación gana algo muy
// concreto: la saga lleva los identificadores internos de cada capacidad —los apartados de aforo,
// el pago— y los intentos de cada compensación. Nada de eso tiene por qué salir a la UI.

/// <summary>Un monto tal como sale.</summary>
public sealed record MoneyDto(decimal Amount, string Currency);

/// <summary>Una línea pedida: qué localidad, qué butaca si la hay, y cuántas.</summary>
/// <remarks>
/// <b>Sin precio, a propósito.</b> Si el total llegara del llamador, cualquiera compraría la
/// localidad VIP al precio de la general. Se cotiza contra <c>Api.Pricing</c>.
/// </remarks>
public sealed record TicketLineRequest(string? Tier, string? Seat, int Quantity);

/// <summary>Comprar entradas de un evento.</summary>
public sealed record BuyTicketsRequest(
    string? EventId, string? BuyerKind, string? BuyerId, IReadOnlyList<TicketLineRequest>? Lines);

/// <summary>Una butaca o cupo apartado, tal como sale.</summary>
/// <param name="Tier">La localidad.</param>
/// <param name="Seat">La butaca, o <c>null</c> en cupo general.</param>
/// <param name="Quantity">Cuántas entradas.</param>
/// <remarks>
/// <b>Sin el identificador del apartado ni el del pozo.</b> Los dos son internos de
/// <c>Api.Inventory</c> y la UI no puede hacer nada con ellos — sacarlos solo invita a que alguien
/// los cablee río arriba, que es el error que costó una vuelta en la HU #25.
/// </remarks>
public sealed record HeldSeatResponse(string Tier, string? Seat, int Quantity);

/// <summary>Cómo sale una compra de entradas.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="BuyerKind">Tipo del comprador.</param>
/// <param name="BuyerId">Identificador del comprador.</param>
/// <param name="EventId">De qué evento.</param>
/// <param name="Status">En qué punto está.</param>
/// <param name="Total">Cuánto se cobra.</param>
/// <param name="Held">Qué quedó apartado — es lo que el CMS necesita para emitir los e-tickets.</param>
/// <param name="PendingCompensations">Cuánto queda por deshacer.</param>
/// <param name="LastError">Qué falló.</param>
public sealed record TicketPurchaseResponse(
    string Id, string BuyerKind, string BuyerId, string EventId, string Status, MoneyDto Total,
    IReadOnlyList<HeldSeatResponse> Held, int PendingCompensations, string? LastError)
{
    public static TicketPurchaseResponse From(TicketingSaga s) => new(
        s.Id, s.Buyer.Kind, s.Buyer.Id, s.EventId, s.Status.ToString(),
        new MoneyDto(s.Total.Amount, s.Total.Currency),
        s.Holds.Select(h => new HeldSeatResponse(h.Tier, h.Seat, h.Quantity)).ToList(),
        s.Pending().Count, s.LastError);
}

/// <summary>Una compensación pendiente, para quien vigila.</summary>
/// <param name="PurchaseId">La compra a la que pertenece.</param>
/// <param name="Kind">Qué hay que deshacer.</param>
/// <param name="Reason">Por qué.</param>
/// <param name="Attempts">Cuántas veces se intentó.</param>
/// <param name="NextAttemptUtc">Cuándo toca el próximo intento.</param>
/// <param name="LastError">Qué dijo el último fallo.</param>
/// <param name="Stuck">Se rindió: el barrido ya no la toca hasta que una persona pida reintento.</param>
/// <param name="AlertedAtUtc">Cuándo se avisó a la guardia. <b>Nulo con <c>Stuck</c> en cierto
/// significa que se rindió y nadie fue avisado</b> — la fila más urgente de esta lista.</param>
public sealed record PendingCompensationResponse(
    string PurchaseId, string Kind, string Reason, int Attempts,
    DateTimeOffset? NextAttemptUtc, string? LastError, bool Stuck, DateTimeOffset? AlertedAtUtc);

/// <summary>Una porción de una lista, con su total.</summary>
public sealed record PageResponse<T>(IReadOnlyList<T> Items, int Total, int Offset, bool HasMore);
