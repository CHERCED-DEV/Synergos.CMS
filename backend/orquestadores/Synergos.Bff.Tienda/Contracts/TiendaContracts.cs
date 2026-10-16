using Synergos.Bff.Core;
using Synergos.Bff.Tienda.Domain;

namespace Synergos.Bff.Tienda.Contracts;

// Lo que cruza el cable, separado de Domain/ (doc 08 §4.1). Acá la separación gana algo muy
// concreto: la saga lleva los identificadores internos de cada capacidad —los apartados, el
// pago— y los intentos de cada compensación. Nada de eso tiene por qué salir a la UI.

/// <summary>Un monto tal como sale.</summary>
public sealed record MoneyDto(decimal Amount, string Currency);

// En una petición, todo anulable lleva `= null` (ADR 0140 F2, decisión 6): el contrato publica la forma
// del cable; lo que el negocio exige lo publican los rechazos con código.

/// <summary>Comprar una canasta.</summary>
public sealed record BuyRequest(string? CartId = null);

/// <summary>Una dirección de entrega, tal como la pide <c>Api.Fulfillment</c>.</summary>
public sealed record AddressRequest(
    string? Line1 = null, string? Line2 = null, string? City = null, string? Region = null,
    string? PostalCode = null, string? Country = null, string? Contact = null);

/// <summary>Confirmar la compra: capturar y despachar.</summary>
public sealed record ConfirmRequest(AddressRequest? To = null, string? Carrier = null);

/// <summary>
/// Lo que quien vendió manda para devolver una parte de lo cobrado (#57).
/// </summary>
/// <remarks>
/// <b>El monto llega calculado.</b> El orquestador cotiza la compra entera y no sabe cuánto vale
/// una línea; quien vende sí, y lo saca de la orden — no del cliente.
/// </remarks>
public sealed record RefundPurchaseRequest(MoneyDto? Amount = null, string? Reason = null);

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
    string? OrderId, string? ShipmentId, int HeldLines, int PendingCompensations, string? LastError,
    MoneyDto? Refunded = null)
{
    public static PurchaseResponse From(PurchaseSaga s) => new(
        s.Id, s.Buyer.Kind, s.Buyer.Id, s.CartId, s.Status.ToString(),
        new MoneyDto(s.Total.Amount, s.Total.Currency),
        s.OrderId, s.ShipmentId, s.Holds.Count, s.Pending().Count, s.LastError,
        // Nulo es «no se devolvió nada», no «cero»: un cero escrito diría que alguien ordenó una
        // devolución de cero, y quien lea la compra no puede distinguir eso de que nadie la tocó.
        s.Refunded is { } d ? new MoneyDto(d.Amount, d.Currency) : null);
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

/// <summary>Una variante de la oferta de un producto, tal como la publica el CMS.</summary>
/// <param name="VariantId">Cuál; sin él, el producto que no tiene variantes. Sin «:».</param>
/// <param name="Price">Cuánto vale una unidad, en la moneda de la oferta.</param>
/// <param name="Stock">Cuántas declara el editor que hay.</param>
public sealed record OfferVariantRequest(string? VariantId = null, decimal? Price = null, int? Stock = null);

/// <summary>Publicar la oferta de un producto: el precio y las existencias de cada variante (004-tienda-compra, R-9).</summary>
/// <param name="ProductId">De qué producto. Sin «:».</param>
/// <param name="Currency">La moneda de los precios.</param>
/// <param name="Variants">Lo que se vende de él. Lo que no viene deja de venderse.</param>
/// <remarks>La manda el CMS al publicar un producto, no el navegador: por eso no lleva marca de la puerta.</remarks>
public sealed record PublishProductOfferRequest(
    string? ProductId = null, string? Currency = null, IReadOnlyList<OfferVariantRequest>? Variants = null);

/// <summary>Cómo quedó publicada una variante.</summary>
/// <param name="VariantId">La variante; nulo para el producto sin variantes.</param>
/// <param name="Price">Su precio, sin impuesto.</param>
/// <param name="Stock">Las existencias declaradas: las de la oferta, o lo comprometido si era más.</param>
public sealed record OfferVariantResponse(string? VariantId, MoneyDto Price, int Stock);

/// <summary>Cómo quedó publicada la oferta de un producto.</summary>
public sealed record ProductOfferResponse(string ProductId, IReadOnlyList<OfferVariantResponse> Variants)
{
    public static ProductOfferResponse From(OfertaPublicada o) => new(
        o.ProductId,
        o.Variants.Select(v => new OfferVariantResponse(v.VariantId, new MoneyDto(v.Price.Amount, v.Price.Currency), v.Stock)).ToList());
}
