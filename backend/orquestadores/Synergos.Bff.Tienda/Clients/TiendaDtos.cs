namespace Synergos.Bff.Tienda.Clients;

// ── Las formas mínimas que este BFF consume de cada capacidad ────────────────
// Solo los campos que usa. Un DTO que copie la respuesta entera obligaría a tocar el
// orquestador cada vez que una capacidad agrega un campo que no le importa.
//
// La fontanería —traducir HTTP a Result<T> preservando el código del rechazo— vive en
// Synergos.Bff.Core.CapabilityHttp: es igual en los ocho orquestadores.

public sealed record MoneyDto(decimal Amount, string Currency);

public sealed record CartLineDto(string SubjectKind, string SubjectId, int Quantity);
public sealed record CartDto(string Id, string OwnerKind, string OwnerId,
    IReadOnlyList<CartLineDto> Lines, bool CheckedOut, bool Open);

/// <summary>Una línea ya cotizada, con su precio unitario.</summary>
public sealed record QuoteLineDto(string SubjectKind, string SubjectId, int Quantity, MoneyDto UnitPrice);

/// <summary>
/// Una cotización — <b>con sus líneas</b>.
/// </summary>
/// <remarks>
/// <para><b>Las líneas NO son «un campo que no le importa»</b> (#122). El comentario de arriba
/// tiene razón en general, y acá no aplicaba: <c>Api.Pricing</c> es el único que sabe a cuánto
/// va cada renglón, y <c>Api.Orders</c> lo congela por línea porque «un pedido es un acuerdo
/// sobre un monto». Sin <c>Lines</c> en este DTO, <c>System.Text.Json</c> descartaba en silencio
/// unos precios que YA habían llegado y el pedido se guardaba con <c>UnitPrice: 0</c> en todas
/// sus líneas: el total quedaba bien, así que nada fallaba y la factura decía que el cliente se
/// llevó la mercancía regalada.</para>
///
/// <para><b>Va anulable a propósito.</b> Si un día la respuesta no las trae, lo que corresponde
/// es que el flujo lo diga —no que rellene ceros—, y para poder decirlo tiene que poder
/// distinguir «no vinieron» de «vinieron vacías». Es la forma de G-7 dentro del árbol de
/// servicios, donde G-7 no mira; ver <c>CLAUDE.md</c> §7.</para>
/// </remarks>
public sealed record QuoteDto(
    IReadOnlyList<QuoteLineDto>? Lines, MoneyDto Subtotal, MoneyDto Tax, MoneyDto Total);

public sealed record StockItemDto(string Id, string SubjectKind, string SubjectId, int OnHand, int Available);
public sealed record StockHoldDto(string Id, int Quantity, DateTimeOffset ExpiresAtUtc, bool Released);

public sealed record OrderDto(string Id, string Status, MoneyDto Total);

// `Refunded` se añadió con #57: la capacidad siempre lo mandó —es el ACUMULADO devuelto— y acá
// no se leía porque hasta ahora sólo se compensaba, y para eso basta con lo que queda devolvible.
// Va opcional para no romper a quien lo construye posicionalmente: si no viene, quien lo use cae
// a lo que pidió.
public sealed record PaymentDto(
    string Id, string Status, MoneyDto Amount, MoneyDto Refundable, MoneyDto? Refunded = null);

public sealed record ShipmentDto(string Id, string Status);
