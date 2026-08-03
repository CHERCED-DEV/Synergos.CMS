using Synergos.Core;

namespace Synergos.Bff.Salud.Clients;

/// <summary>Las capacidades que este BFF necesita, cada una tras su cliente HTTP nombrado.</summary>
/// <remarks>
/// <b>Un cliente nombrado por capacidad</b> y no uno genérico: cada una tiene su URL base, su
/// llave y su timeout, y mezclarlas haría que subir el timeout de Payments se lo subiera también
/// a Consent.
/// </remarks>
public sealed class SaludCapabilities
{
    public const string Consent = "consent";
    public const string Booking = "booking";
    public const string Pricing = "pricing";
    public const string Payments = "payments";

    private readonly IHttpClientFactory _clients;

    public SaludCapabilities(IHttpClientFactory clients) => _clients = clients;

    // ── Consent ─────────────────────────────────────────────────────────────

    public Task<Result<ConsentDto>> CheckConsentAsync(Ref patient, string purpose, CancellationToken ct)
        => Post<ConsentDto>(Consent, "v1/grants/check",
            new { subjectKind = patient.Kind, subjectId = patient.Id, purpose }, null, ct);

    // ── Booking ─────────────────────────────────────────────────────────────

    public Task<Result<ResourceDto>> FindResourceAsync(string resourceId, CancellationToken ct)
        => Get<ResourceDto>(Booking, $"v1/resources/{resourceId}", ct);

    public Task<Result<HoldDto>> HoldAsync(string resourceId, TimeWindow window, Ref patient, IdempotencyKey key, CancellationToken ct)
        => Post<HoldDto>(Booking, "v1/holds", new
        {
            resourceId,
            start = window.Start,
            end = window.End,
            heldForKind = patient.Kind,
            heldForId = patient.Id,
        }, key, ct);

    public Task<Result<ReservationDto>> ConfirmHoldAsync(string holdId, IdempotencyKey key, CancellationToken ct)
        => Post<ReservationDto>(Booking, $"v1/holds/{holdId}/confirm", null, key, ct);

    public Task<Result<HoldDto>> ReleaseHoldAsync(string holdId, CancellationToken ct)
        => Post<HoldDto>(Booking, $"v1/holds/{holdId}/release", null, null, ct);

    // ── Pricing ─────────────────────────────────────────────────────────────

    public Task<Result<QuoteDto>> QuoteAsync(Ref service, CancellationToken ct)
        => Post<QuoteDto>(Pricing, "v1/quotes", new
        {
            lines = new[] { new { subjectKind = service.Kind, subjectId = service.Id, quantity = 1 } },
        }, null, ct);

    // ── Payments ────────────────────────────────────────────────────────────

    public Task<Result<PaymentDto>> AuthorizeAsync(Ref forWhat, Ref payer, Money amount, IdempotencyKey key, CancellationToken ct)
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

    public Task<Result<PaymentDto>> RefundAsync(string paymentId, Money amount, string reason, IdempotencyKey key, CancellationToken ct)
        => Post<PaymentDto>(Payments, $"v1/payments/{paymentId}/refund", new
        {
            amount = new { amount = amount.Amount, currency = amount.Currency },
            reason,
        }, key, ct);

    public Task<Result<PaymentDto>> GetPaymentAsync(string paymentId, CancellationToken ct)
        => Get<PaymentDto>(Payments, $"v1/payments/{paymentId}", ct);

    // ── Fontanería ──────────────────────────────────────────────────────────

    private Task<Result<T>> Post<T>(string capability, string path, object? body, IdempotencyKey? key, CancellationToken ct)
        => CapabilityHttp.SendAsync<T>(_clients.CreateClient(capability), HttpMethod.Post, path, body, key, capability, ct);

    private Task<Result<T>> Get<T>(string capability, string path, CancellationToken ct)
        => CapabilityHttp.SendAsync<T>(_clients.CreateClient(capability), HttpMethod.Get, path, null, null, capability, ct);
}
