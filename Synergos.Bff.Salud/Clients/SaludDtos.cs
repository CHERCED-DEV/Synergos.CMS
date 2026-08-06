namespace Synergos.Bff.Salud.Clients;

// ── Las formas mínimas que este BFF consume de cada capacidad ────────────────
// Solo los campos que usa. Un DTO que copie la respuesta entera obligaría a tocar el
// orquestador cada vez que una capacidad agrega un campo que no le importa.
//
// La fontanería —traducir HTTP a Result<T> preservando el código del rechazo— vive en
// Synergos.Bff.Core.CapabilityHttp: es igual en los ocho orquestadores.

public sealed record ConsentDto(string Id, bool Active);
public sealed record HoldDto(string Id, string ResourceId, DateTimeOffset ExpiresAt);
public sealed record ReservationDto(string Id, string Status);
public sealed record MoneyDto(decimal Amount, string Currency);
public sealed record QuoteDto(MoneyDto Total);
public sealed record PaymentDto(string Id, string Status, MoneyDto Amount, MoneyDto Refundable);
public sealed record ResourceDto(string Id);
