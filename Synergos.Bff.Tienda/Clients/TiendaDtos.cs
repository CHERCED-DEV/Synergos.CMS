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

public sealed record QuoteDto(MoneyDto Subtotal, MoneyDto Tax, MoneyDto Total);

public sealed record StockItemDto(string Id, string SubjectKind, string SubjectId, int OnHand, int Available);
public sealed record StockHoldDto(string Id, int Quantity, DateTimeOffset ExpiresAtUtc, bool Released);

public sealed record OrderDto(string Id, string Status, MoneyDto Total);

public sealed record PaymentDto(string Id, string Status, MoneyDto Amount, MoneyDto Refundable);

public sealed record ShipmentDto(string Id, string Status);
