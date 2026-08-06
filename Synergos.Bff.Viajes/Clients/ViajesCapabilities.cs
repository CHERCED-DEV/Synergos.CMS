using Synergos.Bff.Core;
using Synergos.Core;

namespace Synergos.Bff.Viajes.Clients;

/// <summary>Las tres capacidades que este BFF necesita, cada una tras su cliente HTTP nombrado.</summary>
/// <remarks>
/// <para><b>Tres, y ninguna nueva.</b> Igual que Eventos, el cuarto orquestador entró sin
/// obligar a construir una capacidad ni un endpoint. Apartar es <c>Api.Booking</c> tal cual la
/// dejó la cita clínica; cotizar y cobrar, tal cual las dejó la tienda.</para>
///
/// <para><b>Lo que NO está:</b> <c>Api.Orders</c> y <c>Api.Fulfillment</c>. Un viaje no se
/// despacha — el artefacto es un voucher que emite el CMS— y registrar un pedido para algo que
/// no se envía sería papeleo. Mismo razonamiento que en Eventos, por el mismo motivo.</para>
///
/// <para><b>Un cliente nombrado por capacidad</b> y no uno genérico: cada una tiene su URL base,
/// su llave y su timeout, y mezclarlas haría que subir el timeout de Payments se lo subiera
/// también a Booking.</para>
/// </remarks>
public sealed class ViajesCapabilities : CapabilityClients
{
    public const string Pricing = "pricing";
    public const string Booking = "booking";
    public const string Payments = "payments";

    public ViajesCapabilities(IHttpClientFactory clients) : base(clients) { }

    // ── Pricing ─────────────────────────────────────────────────────────────

    /// <summary>Cuánto vale de verdad lo que se está reservando.</summary>
    /// <remarks>
    /// <b>El precio NO viene del llamador</b>, y ésa es la mitad del punto de cotizar acá. Si el
    /// total llegara en la petición, cualquiera reservaría la suite al precio de la estándar
    /// cambiando un número en el navegador. Se cotiza contra la capacidad, y lo que el llamador
    /// mandó —si mandó algo— no se mira.
    /// </remarks>
    public Task<Result<QuoteDto>> QuoteAsync(IReadOnlyList<Ref> productos, CancellationToken ct)
        => Post<QuoteDto>(Pricing, "v1/quotes", new
        {
            lines = productos.Select(p => new { subjectKind = p.Kind, subjectId = p.Id, quantity = 1 }),
        }, null, ct);

    // ── Booking ─────────────────────────────────────────────────────────────

    /// <summary>Del sujeto del producto a su recurso reservable.</summary>
    /// <remarks>
    /// El identificador del recurso lo genera la capacidad, así que <b>no se adivina</b>: se
    /// pregunta por el sujeto. Inventarse una convención fue el error que costó una vuelta en la
    /// HU #25 y no se repite.
    /// </remarks>
    public Task<Result<ResourceDto>> FindResourceAsync(Ref subject, CancellationToken ct)
        => Get<ResourceDto>(Booking,
            $"v1/resources?subjectKind={Uri.EscapeDataString(subject.Kind)}&subjectId={Uri.EscapeDataString(subject.Id)}", ct);

    public Task<Result<HoldDto>> HoldAsync(
        string resourceId, TimeWindow window, Ref traveller, IdempotencyKey key, CancellationToken ct)
        => Post<HoldDto>(Booking, "v1/holds", new
        {
            resourceId,
            start = window.Start,
            end = window.End,
            heldForKind = traveller.Kind,
            heldForId = traveller.Id,
        }, key, ct);

    public Task<Result<HoldDto>> ReleaseHoldAsync(string holdId, CancellationToken ct)
        => Post<HoldDto>(Booking, $"v1/holds/{holdId}/release", null, null, ct);

    /// <summary>Convierte el apartado en reserva. A partir de acá soltarlo ya no sirve.</summary>
    public Task<Result<ReservationDto>> ConfirmHoldAsync(string holdId, IdempotencyKey key, CancellationToken ct)
        => Post<ReservationDto>(Booking, $"v1/holds/{holdId}/confirm", null, key, ct);

    /// <summary>Deshace una reserva ya confirmada.</summary>
    /// <remarks>
    /// <b>Es la otra cara de <see cref="ReleaseHoldAsync"/></b>, no una variante suya: el
    /// apartado dejó de existir al confirmarse y <c>Api.Booking</c> rechaza soltarlo. Cuál de las
    /// dos toca lo decide en qué punto quedó el ítem, y por eso la saga guarda el identificador
    /// de la reserva en el instante en que aparece.
    /// </remarks>
    public Task<Result<ReservationDto>> CancelReservationAsync(string reservationId, CancellationToken ct)
        => Post<ReservationDto>(Booking, $"v1/reservations/{reservationId}/cancel", null, null, ct);

    // ── Payments ────────────────────────────────────────────────────────────

    public Task<Result<PaymentDto>> AuthorizeAsync(
        Ref forWhat, Ref payer, Money amount, IdempotencyKey key, CancellationToken ct)
        => Post<PaymentDto>(Payments, "v1/payments", new
        {
            forKind = forWhat.Kind,
            forId = forWhat.Id,
            payerKind = payer.Kind,
            payerId = payer.Id,
            amount = new { amount = amount.Amount, currency = amount.Currency },
        }, key, ct);

    public Task<Result<PaymentDto>> CaptureAsync(string paymentId, IdempotencyKey key, CancellationToken ct)
        => Post<PaymentDto>(Payments, $"v1/payments/{paymentId}/capture", null, key, ct);

    public Task<Result<PaymentDto>> VoidAsync(string paymentId, CancellationToken ct)
        => Post<PaymentDto>(Payments, $"v1/payments/{paymentId}/void", null, null, ct);

    public Task<Result<PaymentDto>> RefundAsync(
        string paymentId, Money amount, string reason, IdempotencyKey key, CancellationToken ct)
        => Post<PaymentDto>(Payments, $"v1/payments/{paymentId}/refund", new
        {
            amount = new { amount = amount.Amount, currency = amount.Currency },
            reason,
        }, key, ct);

    public Task<Result<PaymentDto>> GetPaymentAsync(string paymentId, CancellationToken ct)
        => Get<PaymentDto>(Payments, $"v1/payments/{paymentId}", ct);
}

// ── Las formas mínimas que este BFF consume de cada capacidad ────────────────
// Solo los campos que usa. Un DTO que copie la respuesta entera obligaría a tocar el
// orquestador cada vez que una capacidad agrega un campo que no le importa.

public sealed record MoneyDto(decimal Amount, string Currency);
public sealed record QuoteDto(MoneyDto Subtotal, MoneyDto Tax, MoneyDto Total);
public sealed record ResourceDto(string Id, string SubjectKind, string SubjectId, int Capacity);
public sealed record HoldDto(string Id, string ResourceId, DateTimeOffset ExpiresAt);
public sealed record ReservationDto(string Id, string Status);
public sealed record PaymentDto(string Id, string Status, MoneyDto Amount, MoneyDto Refundable);
