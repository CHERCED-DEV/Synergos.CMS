using Synergos.Bff.Core;
using Synergos.Bff.Eventos.Domain;

namespace Synergos.Bff.Eventos.Contracts;

// Lo que cruza el cable, separado de Domain/ (doc 08 §4.1). Acá la separación gana algo muy
// concreto: la saga lleva los identificadores internos de cada capacidad —los apartados de aforo,
// el pago— y los intentos de cada compensación. Nada de eso tiene por qué salir a la UI.
//
// En lo que llega en el CUERPO de una petición, todo anulable lleva `= null` (ADR 0140, F2). No
// cambia cómo se liga —si falta el campo, System.Text.Json ya ponía null—, cambia lo que se
// publica: ASP.NET marca `required` todo parámetro posicional sin valor por defecto aunque sea
// anulable, y el contrato exigía campos que el dominio no exige. El esquema publica la forma del
// cable; lo que el negocio exige lo publican los rechazos con su código. Lo vigila el suelo del
// contrato (SueloDelContratoTests).

/// <summary>Un monto tal como sale.</summary>
public sealed record MoneyDto(decimal Amount, string Currency);

/// <summary>Una línea pedida: cuántas, de qué localidad, y qué butaca si la hay.</summary>
/// <remarks>
/// <para><b>Sin precio, a propósito.</b> Si el total llegara del llamador, cualquiera compraría la
/// localidad VIP al precio de la general. Se cotiza contra <c>Api.Pricing</c>.</para>
///
/// <para><b><c>Quantity</c> va primero</b> porque es el único que no es anulable, y en C# un
/// parámetro sin valor por defecto no puede ir detrás de uno que lo tiene. <c>Seat</c> es nulo en
/// cupo general, y el contrato lo exigía. El orden no cambia el cable: se liga por nombre, y nadie
/// construye este record por posición.</para>
/// </remarks>
public sealed record TicketLineRequest(int Quantity, string? Tier = null, string? Seat = null);

/// <summary>Comprar entradas de un evento: lo que manda el NAVEGADOR, y nada más.</summary>
/// <remarks>
/// <para><b>Ni quién compra ni la comisión van acá</b> (ADR 0140 F3). Los pone la puerta del CMS en
/// cabeceras —<c>X-Synergos-Sujeto</c> desde la sesión, <c>X-Synergos-Negocio</c> desde la
/// configuración del sitio (ADR 0137)— y el cuerpo pasa tal cual del navegador. Con los dos en el
/// cuerpo, el contrato público le dejaba al navegador nombrar a otro comprador o fijarse un 0 % de
/// comisión. Un <c>buyerId</c> o un <c>serviceFeePercent</c> que lleguen en el cuerpo se ignoran.</para>
///
/// <para><b>El precio tampoco viaja</b>: se cotiza contra <c>Api.Pricing</c>.</para>
/// </remarks>
public sealed record BuyTicketsRequest(string? EventId = null, IReadOnlyList<TicketLineRequest>? Lines = null);

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

/// <summary>Una localidad de la oferta de un evento, tal como la publica el CMS.</summary>
/// <param name="Code">El código de la localidad: el mismo con que la nombra una línea de la compra.</param>
/// <param name="Price">Cuánto vale una entrada, en la moneda de la oferta.</param>
/// <param name="MaxPerOrder">Cuántas admite una compra, sumando sus líneas. Sin él, sin tope.</param>
/// <param name="Capacity">El aforo de cupo general. Con butacas no se mira: cada butaca es un pozo de 1.</param>
/// <param name="Seats">Las butacas nominadas, si la localidad las tiene.</param>
/// <param name="SaleOpensUtc">Desde cuándo se vende, incluido. Sin él, desde ya.</param>
/// <param name="SaleClosesUtc">Hasta cuándo, excluido. Sin él, hasta que empiece el evento.</param>
public sealed record OfferTierRequest(
    string? Code = null, decimal? Price = null, int? MaxPerOrder = null, int? Capacity = null,
    IReadOnlyList<string>? Seats = null, DateTimeOffset? SaleOpensUtc = null, DateTimeOffset? SaleClosesUtc = null);

/// <summary>Publicar la oferta de un evento: el precio de cada localidad y sus pozos de aforo (ADR 0140 F3).</summary>
/// <param name="EventId">De qué evento.</param>
/// <param name="Currency">La moneda de los precios.</param>
/// <param name="StartsAtUtc">Cuándo empieza: desde ahí no se vende ninguna localidad.</param>
/// <param name="Tiers">Sus localidades.</param>
/// <remarks>
/// La manda el CMS al publicar un evento, no el navegador: por eso no lleva marca de la puerta.
/// </remarks>
public sealed record PublishEventOfferRequest(
    string? EventId = null, string? Currency = null, DateTimeOffset? StartsAtUtc = null,
    IReadOnlyList<OfferTierRequest>? Tiers = null);

/// <summary>Cómo quedó publicada una localidad.</summary>
/// <param name="Code">La localidad.</param>
/// <param name="Price">Su precio, sin impuesto.</param>
/// <param name="ValidFrom">Desde cuándo se cotiza.</param>
/// <param name="ValidTo">Hasta cuándo: lo primero entre el cierre de la venta y el inicio del evento.</param>
/// <param name="MaxPerOrder">Cuántas admite una compra.</param>
/// <param name="Capacity">El aforo declarado: el de cupo general, o cuántas butacas.</param>
/// <param name="Pools">Cuántos pozos de aforo tiene.</param>
public sealed record OfferTierResponse(
    string Code, MoneyDto Price, DateTimeOffset? ValidFrom, DateTimeOffset? ValidTo, int? MaxPerOrder, int Capacity, int Pools);

/// <summary>Cómo quedó publicada la oferta de un evento.</summary>
public sealed record EventOfferResponse(string EventId, IReadOnlyList<OfferTierResponse> Tiers)
{
    public static EventOfferResponse From(OfertaPublicada o) => new(
        o.EventId,
        o.Tiers.Select(t => new OfferTierResponse(
            t.Code, new MoneyDto(t.Price.Amount, t.Price.Currency), t.ValidFrom, t.ValidTo, t.MaxPerOrder, t.Capacity, t.Pools)).ToList());
}

/// <summary>Una porción de una lista, con su total.</summary>
public sealed record PageResponse<T>(IReadOnlyList<T> Items, int Total, int Offset, bool HasMore);
