using Synergos.Bff.Core;
using Synergos.Bff.Tienda.Domain;

namespace Synergos.Bff.Tienda.Contracts;

// Lo que cruza el cable, separado de Domain/ (doc 08 §4.1). Acá la separación gana algo muy
// concreto: la saga lleva los identificadores internos de cada capacidad —los apartados, el
// pago— y los intentos de cada compensación. Nada de eso tiene por qué salir a la UI.

/// <summary>Un monto tal como sale.</summary>
public sealed record MoneyDto(decimal Amount, string Currency);

/// <summary>Comprar una canasta.</summary>
public sealed record BuyRequest(string? CartId);

/// <summary>Una dirección de entrega, tal como la pide <c>Api.Fulfillment</c>.</summary>
public sealed record AddressRequest(
    string? Line1, string? Line2, string? City, string? Region,
    string? PostalCode, string? Country, string? Contact);

/// <summary>Confirmar la compra: capturar y despachar.</summary>
public sealed record ConfirmRequest(AddressRequest? To, string? Carrier);

/// <summary>Cómo sale una compra.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="BuyerKind">Tipo del comprador.</param>
/// <param name="BuyerId">Identificador del comprador.</param>
/// <param name="CartId">La canasta de la que salió.</param>
/// <param name="Status">En qué punto está.</param>
/// <param name="Total">Cuánto se cobra.</param>
/// <param name="OrderId">El pedido, cuando ya existe.</param>
/// <param name="ShipmentId">El envío, si se despachó.</param>
/// <param name="HeldLines">Cuántas líneas quedaron apartadas.</param>
/// <param name="PendingCompensations">Cuánto queda por deshacer.</param>
/// <param name="LastError">Qué falló.</param>
public sealed record PurchaseResponse(
    string Id, string BuyerKind, string BuyerId, string CartId, string Status, MoneyDto Total,
    string? OrderId, string? ShipmentId, int HeldLines, int PendingCompensations, string? LastError)
{
    public static PurchaseResponse From(PurchaseSaga s) => new(
        s.Id, s.Buyer.Kind, s.Buyer.Id, s.CartId, s.Status.ToString(),
        new MoneyDto(s.Total.Amount, s.Total.Currency),
        s.OrderId, s.ShipmentId, s.Holds.Count, s.Pending().Count, s.LastError);
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
