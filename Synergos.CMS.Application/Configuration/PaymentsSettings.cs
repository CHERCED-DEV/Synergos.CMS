namespace Synergos.CMS.Application.Configuration;

/// <summary>
/// POCO tipado bindeado de <c>Synergos:Payments</c> (T3, doc 25). Gobierna la
/// SELECCIÓN de proveedor de pago (gating por config, calcando el patrón
/// <c>BundleRegistrySettings.Mode</c>) + el secreto del webhook entrante + los knobs
/// de simulación del stub durable. Las llaves de Wompi son placeholders para la Ola B
/// (adapter PSP real, gated).
/// </summary>
public sealed class PaymentsSettings
{
    /// <summary>
    /// Proveedor activo: <c>"Stub"</c> (default — stub durable, sin PSP real) o
    /// <c>"Wompi"</c> (Ola B, requiere llaves). Case-insensitive. Sin sección de
    /// config, la demo corre siempre con el stub durable.
    /// </summary>
    public string Provider { get; init; } = "Stub";

    /// <summary>
    /// Secreto compartido para verificar la firma HMAC del webhook entrante (esquema
    /// stub, espejo de <c>WebhookSigner</c>). Vacío = no se exige firma (cómodo para la
    /// demo). Con <see cref="Provider"/> != Stub, el receptor exige firma obligatoria.
    /// </summary>
    public string WebhookSecret { get; init; } = string.Empty;

    /// <summary>
    /// Base absoluta para construir la <c>returnUrl</c> del checkout (a dónde vuelve el
    /// cliente tras el redirect del PSP). Vacío = se deriva del host del request.
    /// </summary>
    public string ReturnUrlBase { get; init; } = string.Empty;

    /// <summary>
    /// Knob de simulación (stub): si una línea del carrito trae este SKU/productRef, la
    /// sesión de pago nace/declina en <c>Failed</c> (permite demostrar un rechazo limpio
    /// sin PSP real). Vacío = nunca declina.
    /// </summary>
    public string DeclineTriggerSku { get; init; } = string.Empty;

    /// <summary>
    /// Knob de simulación (stub): si es true, <c>CreateSessionAsync</c> devuelve
    /// <c>RequiresAction</c> + una <c>RedirectUrl</c> sintética (simula 3DS/redirect a
    /// hosted checkout) en vez de auto-autorizar. Default false = auto-autoriza (demo
    /// síncrona intacta).
    /// </summary>
    public bool SimulateRequiresAction { get; init; }

    // ── Ola B (gated) — llaves de Wompi CO. Vacías en Ola A. ──────────
    /// <summary>Llave pública Wompi (pub_test_/pub_prod_). Ola B.</summary>
    public string WompiPublicKey { get; init; } = string.Empty;

    /// <summary>Llave privada Wompi (prv_test_/prv_prod_). Ola B.</summary>
    public string WompiPrivateKey { get; init; } = string.Empty;

    /// <summary>Secreto de integridad Wompi (test_integrity_/prod_integrity_) — firma de la transacción. Ola B.</summary>
    public string WompiIntegritySecret { get; init; } = string.Empty;

    /// <summary>Secreto de eventos Wompi (test_events_/prod_events_) — firma del webhook. Ola B.</summary>
    public string WompiEventsSecret { get; init; } = string.Empty;
}
