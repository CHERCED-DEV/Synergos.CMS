using Synergos.Bff.Core;
using Synergos.Core;

namespace Synergos.Bff.Eventos.Clients;

/// <summary>Las tres capacidades que este BFF necesita, cada una tras su cliente HTTP nombrado.</summary>
/// <remarks>
/// <para><b>Tres, contra las seis de Tienda y las cuatro de Salud.</b> Y es el dato interesante:
/// el tercer orquestador no necesitó ninguna capacidad nueva ni ningún endpoint nuevo. Apartar
/// aforo es <c>Api.Inventory</c> tal cual la dejó la tienda; cobrar es <c>Api.Payments</c> tal
/// cual. Que un dominio distinto entre sin tocar nada es la prueba de que las capacidades son
/// agnósticas de verdad y no «agnósticas hasta el segundo caso».</para>
///
/// <para><b>Lo que NO está:</b> <c>Api.Orders</c> y <c>Api.Fulfillment</c>. Una entrada no se
/// despacha, y registrar un pedido para algo que no se envía sería papeleo. El artefacto —el
/// e-ticket con su QR— lo emite el CMS, que es donde vive el firmante.</para>
/// </remarks>
public sealed class EventosCapabilities : CapabilityClients
{
    public const string Pricing = "pricing";
    public const string Inventory = "inventory";
    public const string Payments = "payments";

    public EventosCapabilities(IHttpClientFactory clients) : base(clients) { }

    // ── Pricing ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Cuánto vale de verdad lo que se está comprando.
    /// </summary>
    /// <remarks>
    /// <b>El precio NO viene del llamador</b>, y ésa es la mitad del punto de cotizar acá. Si el
    /// total llegara en la petición, cualquiera podría comprar la localidad VIP al precio de la
    /// general cambiando un número en el navegador. Se cotiza contra la capacidad, y lo que el
    /// llamador mandó —si mandó algo— no se mira.
    /// </remarks>
    public Task<Result<QuoteDto>> QuoteAsync(IReadOnlyList<(Ref Subject, int Quantity)> lineas, CancellationToken ct)
        => Post<QuoteDto>(Pricing, "v1/quotes", new
        {
            lines = lineas.Select(l => new { subjectKind = l.Subject.Kind, subjectId = l.Subject.Id, quantity = l.Quantity }),
        }, null, ct);

    // ── Inventory ───────────────────────────────────────────────────────────

    /// <summary>Del sujeto del pozo a su ítem de aforo.</summary>
    /// <remarks>
    /// El identificador del ítem lo genera la capacidad, así que <b>no se adivina</b>: se
    /// pregunta por el sujeto. Inventarse una convención fue el error que costó una vuelta en la
    /// HU #25 y no se repite.
    /// </remarks>
    public Task<Result<StockItemDto>> FindAforoAsync(Ref subject, CancellationToken ct)
        => Get<StockItemDto>(Inventory,
            $"v1/items?subjectKind={Uri.EscapeDataString(subject.Kind)}&subjectId={Uri.EscapeDataString(subject.Id)}", ct);

    public Task<Result<StockHoldDto>> HoldAforoAsync(
        string itemId, int quantity, Ref forWhat, IdempotencyKey key, CancellationToken ct)
        => Post<StockHoldDto>(Inventory, $"v1/items/{itemId}/holds", new
        {
            quantity,
            forKind = forWhat.Kind,
            forId = forWhat.Id,
        }, key, ct);

    public Task<Result<StockHoldDto>> ReleaseAforoAsync(string holdId, CancellationToken ct)
        => Post<StockHoldDto>(Inventory, $"v1/holds/{holdId}/release", null, null, ct);

    /// <summary>Consume el apartado: la butaca sale del aforo de verdad.</summary>
    public Task<Result<StockHoldDto>> ConsumeAforoAsync(string holdId, CancellationToken ct)
        => Post<StockHoldDto>(Inventory, $"v1/holds/{holdId}/consume", null, null, ct);

    /// <summary>
    /// Devuelve aforo sumando sobre lo que haya, sin leer primero.
    /// </summary>
    /// <remarks>
    /// La llave no es decoración: un relativo reintentado suma dos veces y el motor reintenta
    /// hasta ocho. Va determinista desde la saga para que los ocho intentos sean el mismo ajuste
    /// (defecto #30).
    /// </remarks>
    public Task<Result<StockItemDto>> RestockAforoAsync(
        string itemId, int delta, IdempotencyKey key, CancellationToken ct)
        => Post<StockItemDto>(Inventory, $"v1/items/{itemId}/adjust", new { delta }, key, ct);

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
public sealed record StockItemDto(string Id, string SubjectKind, string SubjectId, int OnHand, int Available);
public sealed record StockHoldDto(string Id, int Quantity, DateTimeOffset ExpiresAtUtc, bool Released);
public sealed record PaymentDto(string Id, string Status, MoneyDto Amount, MoneyDto Refundable);
