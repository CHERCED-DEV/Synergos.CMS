using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Web.Controllers;

/// <summary>
/// Receptor de webhooks de pago ENTRANTES. Es cómo un PSP confirma un cobro de
/// forma ASÍNCRONA: server-to-server, sin cookie de member, así que <b>la firma
/// es la autorización</b> y no <see cref="IMemberAccessGate"/>.
/// </summary>
/// <remarks>
/// <para>Flujo endurecido, en este orden: (1) valida el proveedor; (2) lee el
/// body RAW a bytes ANTES de deserializar, porque la firma es sobre los bytes
/// exactos; (3) normaliza el payload del PSP; (4) verifica la firma;
/// (5) <b>anti-tampering</b>: nunca confía el estado del payload, lo re-consulta
/// al proveedor; (6) despacha al vertical dueño; (7) deduplica por
/// <c>(proveedor, evento)</c> DESPUÉS de procesar. Un 200 corta el reintento.</para>
///
/// <para><b>Lo que cambió en la fase 4 del ADR 0116:</b> antes despachaba
/// siempre a <c>IShopOrderService</c>. Ahora recorre los
/// <see cref="IPaymentEventSink"/> registrados y cada uno dice si la referencia
/// es suya. Con PSE —que confirma por evento y no en el retorno— los otros
/// siete verticales estaban sordos: cobraban y nunca se enteraban.</para>
///
/// <para><b>Sobre eventos viejos que llegan tarde.</b> La investigación de PSPs
/// advierte que pueden llegar fuera de orden y pisar un estado más nuevo. Acá no
/// hace falta maquinaria de versiones: el paso (5) re-consulta el estado REAL al
/// proveedor, así que un evento rancio termina actuando sobre la verdad actual y
/// no sobre la que traía. Es la misma propiedad que ya daba el anti-tampering,
/// aprovechada para otra cosa.</para>
/// </remarks>
[ApiController]
[Route("api/payments/webhook")]
public sealed class PaymentWebhookController : ControllerBase
{
    /// <summary>Familia de claves del ledger (→ App_Data/syn-payment-events/).</summary>
    private const string LedgerScope = "payment-events";

    private static readonly HashSet<string> KnownProviders =
        new(StringComparer.OrdinalIgnoreCase) { "stub", "wompi" };

    private readonly IPaymentProvider _payments;
    private readonly IIdempotencyLedger _ledger;
    private readonly IEnumerable<IPaymentEventSink> _sinks;
    private readonly PaymentsSettings _settings;
    private readonly ILogger<PaymentWebhookController> _logger;

    public PaymentWebhookController(
        IPaymentProvider payments,
        IIdempotencyLedger ledger,
        IEnumerable<IPaymentEventSink> sinks,
        IOptions<PaymentsSettings> settings,
        ILogger<PaymentWebhookController> logger)
    {
        _payments = payments;
        _ledger = ledger;
        _sinks = sinks;
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

        // 1) Body RAW a bytes ANTES de deserializar: la firma es sobre los bytes.
        byte[] body;
        using (var ms = new MemoryStream())
        {
            await Request.Body.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
            body = ms.ToArray();
        }

        // 2) Normalizar el payload del PSP a una forma común.
        var payload = PaymentWebhookPayloadReader.Read(provider, body);
        if (payload is null)
        {
            return BadRequest(new { error = "Payload del webhook ilegible o incompleto." });
        }

        // 3) Verificar autenticidad. Cada esquema firma a su manera.
        if (VerifySignature(provider, body, payload) is { } rejection)
        {
            return rejection;
        }

        // 4) ¿De quién es? Se pregunta ANTES de consultar al PSP: si no es
        //    nuestra, consultar no aporta nada y le regala a cualquiera un
        //    amplificador contra el proveedor a costa nuestra.
        IPaymentEventSink? owner = null;
        foreach (var sink in _sinks)
        {
            if (await sink.OwnsAsync(payload.OrderReference, cancellationToken).ConfigureAwait(false))
            {
                owner = sink;
                break;
            }
        }

        if (owner is null)
        {
            // Se ack igual: reintentar no va a cambiar nada, y un no-200 haría
            // que el PSP insista durante 24 horas por una referencia ajena.
            _logger.LogInformation(
                "Webhook {Provider}/{EventId}: ningún vertical reclamó la referencia {OrderRef}.",
                provider, payload.EventId, payload.OrderReference);
            return Ok(new { ignored = "ningún vertical reclamó la referencia" });
        }

        // 5) ANTI-TAMPERING: el estado sale del PROVEEDOR, nunca del payload.
        var verified = await _payments
            .GetStatusAsync(payload.SessionId, cancellationToken)
            .ConfigureAwait(false);

        var paymentEvent = new PaymentEvent(
            ProviderKey: provider,
            EventId: payload.EventId,
            OrderReference: payload.OrderReference,
            SessionId: payload.SessionId,
            // Un adapter que devuelva null viola el contrato, pero acá eso no
            // puede tumbar el receptor: un 500 haría que el PSP reintente
            // durante 24 horas contra un endpoint que ya sabemos que revienta.
            // Sin estado conocido se degrada a Pending, que no confirma nada.
            VerifiedStatus: verified?.Status ?? PaymentStatus.Pending,
            OccurredUtc: payload.OccurredUtc);

        // 6) Atender. Un sink que revienta NO puede tragarse el evento: propaga
        //    como 500 SIN marcar, así el PSP reintenta. Silenciarlo perdería el
        //    pago, que es el peor final posible acá.
        var outcome = await owner.HandleAsync(paymentEvent, cancellationToken).ConfigureAwait(false);

        if (!outcome.Handled)
        {
            // Lo reclamó y después se desdijo — la orden desapareció entre una
            // llamada y otra. Raro, pero no es motivo para insistir.
            return Ok(new { ignored = "el vertical soltó la referencia" });
        }

        // 7) Marca DESPUÉS del éxito: marcar antes dejaría un pago
        //    cobrado-sin-procesar con su reintento deduplicado para siempre.
        var firstTime = await _ledger
            .TryClaimAsync(LedgerScope, $"{provider}-{payload.EventId}", cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation(
            "Webhook {Provider}/{EventId} → {Vertical}: {Detail}",
            provider, payload.EventId, owner.Vertical, outcome.Detail ?? "procesado");

        return Ok(new
        {
            processed = firstTime,
            duplicate = !firstTime,
            vertical = owner.Vertical,
            detail = outcome.Detail,
        });
    }

    /// <summary>
    /// Verifica la firma según el esquema del proveedor. Devuelve el rechazo, o
    /// <c>null</c> si pasa.
    /// </summary>
    private IActionResult? VerifySignature(string provider, byte[] body, PaymentWebhookPayload payload)
    {
        if (string.Equals(provider, "wompi", StringComparison.OrdinalIgnoreCase))
        {
            // Wompi firma con SHA256 sobre las propiedades que él mismo declara.
            // Sin secreto configurado es una misconfiguración, no una excusa
            // para aceptar a ciegas.
            if (string.IsNullOrWhiteSpace(_settings.WompiEventsSecret))
            {
                _logger.LogError("Webhook wompi: falta Synergos:Payments:WompiEventsSecret.");
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new { error = "Verificación de firma no configurada." });
            }
            if (!PaymentWebhookPayloadReader.WompiChecksumValid(payload, _settings.WompiEventsSecret))
            {
                return Unauthorized(new { error = "Checksum del evento inválido." });
            }
            return null;
        }

        // Esquema propio (stub): HMAC sobre los bytes con ventana anti-replay.
        // El stub en demo puede correr sin firma; un PSP real nunca.
        var requireSignature = !string.Equals(provider, "stub", StringComparison.OrdinalIgnoreCase);
        var verdict = PaymentWebhookVerifier.Verify(
            _settings.WebhookSecret,
            Request.Headers[WebhookSigner.TimestampHeaderName],
            Request.Headers[WebhookSigner.SignatureHeaderName],
            body,
            requireSignature);

        return verdict switch
        {
            PaymentWebhookVerifier.Result.MissingHeaders =>
                BadRequest(new { error = "Faltan los headers de firma." }),
            PaymentWebhookVerifier.Result.InvalidSignature or PaymentWebhookVerifier.Result.Expired =>
                Unauthorized(new { error = "Firma del webhook inválida o vencida." }),
            PaymentWebhookVerifier.Result.MisconfiguredSecret =>
                StatusCode(StatusCodes.Status500InternalServerError,
                    new { error = "Verificación de firma no configurada." }),
            _ => null,
        };
    }
}
