using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Web.Controllers;

/// <summary>
/// Receptor de webhooks de pago ENTRANTES (T3, doc 25) — el PRIMER webhook entrante del
/// repo (los demás son salientes: Discord/Email). Es cómo un PSP confirma el cobro de
/// forma ASÍNCRONA (server-to-server, sin cookie de member → la FIRMA es la
/// autorización, no <see cref="IMemberAccessGate"/>). Domain-neutral en la ruta
/// (<c>{provider}</c>); en Ola A solo el esquema <c>stub</c> está vivo (Wompi = Ola B).
/// </summary>
/// <remarks>
/// Flujo endurecido: (1) valida el provider; (2) lee el body RAW a bytes ANTES de
/// deserializar (el HMAC es sobre bytes exactos); (3) verifica la firma
/// (<see cref="PaymentWebhookVerifier"/>); (4) ANTI-TAMPERING: nunca confía el estado del
/// payload, re-consulta <see cref="IPaymentProvider.GetStatusAsync"/>; (5) IDEMPOTENCIA:
/// candado atómico <see cref="IPaymentEventStore"/> por (provider,eventId); (6) despacha
/// a <see cref="IShopOrderService.ConfirmAsync"/> (idempotente). La confirmación de la
/// orden es Tienda-específica por ahora (único vertical durable); cuando otro vertical se
/// durabilice se extrae un despacho domain-neutral. 200 corta el retry del PSP.
/// </remarks>
[ApiController]
[Route("api/payments/webhook")]
public sealed class PaymentWebhookController : ControllerBase
{
    private static readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };
    private static readonly HashSet<string> KnownProviders = new(StringComparer.OrdinalIgnoreCase) { "stub" };

    private readonly IPaymentProvider _payments;
    private readonly IPaymentEventStore _events;
    private readonly IShopOrderService _orders;
    private readonly PaymentsSettings _settings;
    private readonly ILogger<PaymentWebhookController> _logger;

    public PaymentWebhookController(
        IPaymentProvider payments,
        IPaymentEventStore events,
        IShopOrderService orders,
        IOptions<PaymentsSettings> settings,
        ILogger<PaymentWebhookController> logger)
    {
        _payments = payments;
        _events = events;
        _orders = orders;
        _settings = settings.Value;
        _logger = logger;
    }

    // POST /api/payments/webhook/{provider}
    [HttpPost("{provider}")]
    [AllowAnonymous]
    public async Task<IActionResult> Receive(string provider, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(provider) || !KnownProviders.Contains(provider))
        {
            return NotFound(new { error = $"Proveedor de pago '{provider}' desconocido." });
        }

        // 1) Body RAW a bytes ANTES de deserializar (el HMAC es sobre los bytes exactos).
        byte[] body;
        using (var ms = new MemoryStream())
        {
            await Request.Body.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
            body = ms.ToArray();
        }

        // 2) Verificar la firma (esquema stub = espejo de WebhookSigner).
        var verdict = PaymentWebhookVerifier.Verify(
            _settings.WebhookSecret,
            Request.Headers[WebhookSigner.TimestampHeaderName],
            Request.Headers[WebhookSigner.SignatureHeaderName],
            body);
        switch (verdict)
        {
            case PaymentWebhookVerifier.Result.MissingHeaders:
                return BadRequest(new { error = "Faltan los headers de firma." });
            case PaymentWebhookVerifier.Result.InvalidSignature:
            case PaymentWebhookVerifier.Result.Expired:
                return Unauthorized(new { error = "Firma del webhook inválida o vencida." });
        }

        // 3) Parsear el payload.
        StubWebhookPayload? payload;
        try { payload = JsonSerializer.Deserialize<StubWebhookPayload>(body, _json); }
        catch (JsonException) { return BadRequest(new { error = "Payload del webhook ilegible." }); }
        if (payload is null || string.IsNullOrWhiteSpace(payload.EventId) || string.IsNullOrWhiteSpace(payload.SessionId))
        {
            return BadRequest(new { error = "eventId y sessionId son requeridos." });
        }

        // 4) ANTI-TAMPERING: re-consultar el estado REAL de la sesión (no confiar el
        //    payload). Solo una sesión autorizada/capturada dispara la confirmación.
        var status = await _payments.GetStatusAsync(payload.SessionId, cancellationToken);
        if (status.Status is not (PaymentStatus.Authorized or PaymentStatus.Captured))
        {
            _logger.LogInformation("Webhook {Provider}/{EventId}: sesión {SessionId} en estado {Status} — nada que confirmar.",
                provider, payload.EventId, payload.SessionId, status.Status);
            return Ok(new { ignored = $"estado {status.Status}" });
        }

        // 5) Resolver la orden (Tienda). Sin orderRef o sin orden → ack sin efecto.
        if (string.IsNullOrWhiteSpace(payload.OrderRef) || await _orders.GetOrderAsync(payload.OrderRef, cancellationToken) is null)
        {
            _logger.LogInformation("Webhook {Provider}/{EventId}: orden {OrderRef} no encontrada — ack sin efecto.",
                provider, payload.EventId, payload.OrderRef);
            return Ok(new { ignored = "orden no encontrada" });
        }

        // 6) IDEMPOTENCIA: candado atómico. Duplicado → 200 sin re-ejecutar.
        if (!await _events.TryMarkProcessedAsync(provider, payload.EventId, cancellationToken))
        {
            return Ok(new { duplicate = true });
        }

        // 7) Confirmar la orden (idempotente: si ya está Paid, short-circuit).
        try
        {
            var result = await _orders.ConfirmAsync(payload.OrderRef, cancellationToken);
            return Ok(new { processed = true, orderNumber = result.OrderNumber, status = result.Status });
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // La orden existía pero la confirmación no procede (hold vencido, etc.). Ack
            // 200 para no reintentar en bucle; el estado real vive en la orden persistida.
            _logger.LogWarning(ex, "Webhook {Provider}/{EventId}: confirmación de {OrderRef} no procedió.",
                provider, payload.EventId, payload.OrderRef);
            return Ok(new { processed = false, reason = ex.Message });
        }
    }

    /// <summary>Payload del webhook (esquema stub). Un PSP real trae su propia forma (Ola B).</summary>
    public sealed record StubWebhookPayload(string? EventId, string? SessionId, string? OrderRef, string? Status);
}
