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
/// candado atómico <see cref="IIdempotencyLedger"/> por (provider,eventId); (6) despacha
/// a <see cref="IShopOrderService.ConfirmAsync"/> (idempotente). La confirmación de la
/// orden es Tienda-específica por ahora (único vertical durable); cuando otro vertical se
/// durabilice se extrae un despacho domain-neutral. 200 corta el retry del PSP.
/// </remarks>
[ApiController]
[Route("api/payments/webhook")]
public sealed class PaymentWebhookController : ControllerBase
{
    private static readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };
    /// <summary>Familia de claves del ledger — preserva la ruta de T3 (App_Data/syn-payment-events/).</summary>
    private const string LedgerScope = "payment-events";

    private static readonly HashSet<string> KnownProviders = new(StringComparer.OrdinalIgnoreCase) { "stub" };

    private readonly IPaymentProvider _payments;
    private readonly IIdempotencyLedger _ledger;
    private readonly IShopOrderService _orders;
    private readonly PaymentsSettings _settings;
    private readonly ILogger<PaymentWebhookController> _logger;

    public PaymentWebhookController(
        IPaymentProvider payments,
        IIdempotencyLedger ledger,
        IShopOrderService orders,
        IOptions<PaymentsSettings> settings,
        ILogger<PaymentWebhookController> logger)
    {
        _payments = payments;
        _ledger = ledger;
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

        // 2) Verificar la firma. Con un PSP real (provider != stub) la firma es
        //    OBLIGATORIA: sin secret configurado es una misconfiguración → 500 (no se
        //    acepta a ciegas). El stub en demo puede correr sin firma.
        var requireSignature = !string.Equals(provider, "stub", StringComparison.OrdinalIgnoreCase);
        var verdict = PaymentWebhookVerifier.Verify(
            _settings.WebhookSecret,
            Request.Headers[WebhookSigner.TimestampHeaderName],
            Request.Headers[WebhookSigner.SignatureHeaderName],
            body,
            requireSignature);
        switch (verdict)
        {
            case PaymentWebhookVerifier.Result.MissingHeaders:
                return BadRequest(new { error = "Faltan los headers de firma." });
            case PaymentWebhookVerifier.Result.InvalidSignature:
            case PaymentWebhookVerifier.Result.Expired:
                return Unauthorized(new { error = "Firma del webhook inválida o vencida." });
            case PaymentWebhookVerifier.Result.MisconfiguredSecret:
                _logger.LogError("Webhook {Provider}: se exige firma pero falta Synergos:Payments:WebhookSecret.", provider);
                return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Verificación de firma no configurada." });
        }

        // 3) Parsear el payload.
        StubWebhookPayload? payload;
        try { payload = JsonSerializer.Deserialize<StubWebhookPayload>(body, _json); }
        catch (JsonException) { return BadRequest(new { error = "Payload del webhook ilegible." }); }
        if (payload is null
            || string.IsNullOrWhiteSpace(payload.EventId)
            || string.IsNullOrWhiteSpace(payload.SessionId)
            || string.IsNullOrWhiteSpace(payload.OrderRef))
        {
            return BadRequest(new { error = "eventId, sessionId y orderRef son requeridos." });
        }

        // 4) Resolver la orden PRIMERO (Tienda). Sin orden → ack sin efecto.
        var order = await _orders.GetOrderAsync(payload.OrderRef, cancellationToken);
        if (order is null)
        {
            _logger.LogInformation("Webhook {Provider}/{EventId}: orden {OrderRef} no encontrada — ack sin efecto.",
                provider, payload.EventId, payload.OrderRef);
            return Ok(new { ignored = "orden no encontrada" });
        }

        // 5) LIGAR la sesión a la orden: la sesión del payload debe ser la de ESTA orden.
        //    Evita confirmar una orden usando el estado autorizado de una sesión ajena.
        if (!string.Equals(payload.SessionId, order.PaymentSessionId, StringComparison.Ordinal))
        {
            _logger.LogWarning("Webhook {Provider}/{EventId}: sessionId {SessionId} no corresponde a la orden {OrderRef}.",
                provider, payload.EventId, payload.SessionId, payload.OrderRef);
            return Ok(new { ignored = "la sesión no corresponde a la orden" });
        }

        // 6) ANTI-TAMPERING: re-consultar el estado REAL de la sesión DE LA ORDEN (no del
        //    payload). Solo una sesión autorizada/capturada dispara la confirmación.
        var status = await _payments.GetStatusAsync(order.PaymentSessionId!, cancellationToken);
        if (status.Status is not (PaymentStatus.Authorized or PaymentStatus.Captured))
        {
            _logger.LogInformation("Webhook {Provider}/{EventId}: sesión {SessionId} en estado {Status} — nada que confirmar.",
                provider, payload.EventId, order.PaymentSessionId, status.Status);
            return Ok(new { ignored = $"estado {status.Status}" });
        }

        // 7) CONFIRMAR y LUEGO marcar (ConfirmAsync es idempotente). Marcar ANTES sería
        //    peligroso: un fallo transitorio tras el marcado dejaría la orden
        //    cobrada-sin-confirmar y el retry del PSP quedaría deduped para siempre.
        try
        {
            var result = await _orders.ConfirmAsync(payload.OrderRef, cancellationToken);
            // Marca DESPUÉS del éxito: corta retries futuros. Un duplicado concurrente que
            // ya confirmó devuelve false aquí (ambos confirmaron idempotente, sin doble cobro).
            var firstTime = await _ledger.TryClaimAsync(LedgerScope, $"{provider}-{payload.EventId}", cancellationToken);
            return Ok(new { processed = firstTime, duplicate = !firstTime, orderNumber = result.OrderNumber, status = result.Status });
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // Rechazo de negocio PERMANENTE (hold vencido, etc.): marcar para cortar el
            // bucle de reintentos del PSP y ack 200. Una IOException transitoria NO cae
            // aquí → propaga como 500 SIN marcar → el PSP reintenta y ConfirmAsync se
            // re-ejecuta idempotente.
            _logger.LogWarning(ex, "Webhook {Provider}/{EventId}: confirmación de {OrderRef} no procedió (rechazo terminal).",
                provider, payload.EventId, payload.OrderRef);
            await _ledger.TryClaimAsync(LedgerScope, $"{provider}-{payload.EventId}", cancellationToken);
            return Ok(new { processed = false, reason = ex.Message });
        }
    }

    /// <summary>Payload del webhook (esquema stub). Un PSP real trae su propia forma (Ola B).</summary>
    public sealed record StubWebhookPayload(string? EventId, string? SessionId, string? OrderRef, string? Status);
}
