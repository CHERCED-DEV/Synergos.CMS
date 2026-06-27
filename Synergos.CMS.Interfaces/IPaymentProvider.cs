namespace Synergos.CMS.Interfaces;

/// <summary>
/// Estado de una sesión de pago, agnóstico del proveedor (PSP). Mapea a
/// los estados canónicos de Stripe/Wompi/PayU sin acoplar a ninguno.
/// </summary>
public enum PaymentStatus
{
    /// <summary>Sesión creada, aún sin acción del cliente.</summary>
    Pending,
    /// <summary>Requiere acción del cliente (redirect/3DS) — ver <see cref="PaymentSession.RedirectUrl"/>.</summary>
    RequiresAction,
    /// <summary>Autorizada (fondos reservados), pendiente de captura.</summary>
    Authorized,
    /// <summary>Capturada (cobro efectivo).</summary>
    Captured,
    /// <summary>Falló (rechazo/error).</summary>
    Failed,
    /// <summary>Cancelada antes de capturar.</summary>
    Cancelled,
    /// <summary>Reembolsada (total o parcial).</summary>
    Refunded,
}

/// <summary>Línea de un pago (para desglose/recibo del PSP).</summary>
public sealed record PaymentLineItem(string Sku, string Description, decimal UnitPrice, int Quantity);

/// <summary>
/// Solicitud para abrir una sesión de pago. Genérica para cualquier nicho
/// (hoteles/aerolíneas/eventos/tienda…): el motor arma esto desde el
/// carrito/reserva y el PSP lo cobra.
/// </summary>
public sealed record PaymentSessionRequest(
    string OrderReference,
    decimal Amount,
    string Currency,
    IReadOnlyList<PaymentLineItem> Items,
    string? CustomerEmail = null,
    string? ReturnUrl = null,
    IReadOnlyDictionary<string, string>? Metadata = null);

/// <summary>
/// Sesión de pago abierta. <see cref="RedirectUrl"/> (flujos redirect/3DS)
/// y <see cref="ClientSecret"/> (flujos inline tipo Stripe Elements) son
/// mutuamente opcionales según el adapter.
/// </summary>
public sealed record PaymentSession(
    string SessionId,
    PaymentStatus Status,
    string? RedirectUrl = null,
    string? ClientSecret = null,
    string? ProviderKey = null);

/// <summary>Resultado de consultar/capturar/reembolsar una sesión.</summary>
public sealed record PaymentOutcome(
    string SessionId,
    PaymentStatus Status,
    decimal AmountCaptured = 0m,
    string? FailureReason = null);

/// <summary>
/// Pasarela de pago (PSP), agnóstica del proveedor. Es la pieza del MOTOR
/// que faltaba: hoy <see cref="ICheckoutRecorder"/> solo REGISTRA la orden,
/// no procesa el cobro. El flujo del motor es:
/// carrito/reserva → <see cref="CreateSessionAsync"/> → (redirect/3DS si aplica)
/// → <see cref="CaptureAsync"/> → en éxito <see cref="ICheckoutRecorder.Record"/>.
/// </summary>
/// <remarks>
/// Seam stub-first (igual que <see cref="IBundleRegistryClient"/>): el default
/// <c>StubPaymentProvider</c> (Application, lógica pura) simula aprobación para
/// que la demo corra end-to-end; los adapters reales (Stripe / Wompi / PayU CO)
/// se enchufan después sin tocar el motor. ADR 0002 (Application sin Umbraco).
/// </remarks>
public interface IPaymentProvider
{
    /// <summary>Clave del proveedor activo: "stub", "stripe", "wompi", "payu".</summary>
    string ProviderKey { get; }

    /// <summary>Abre una sesión de pago para cobrar <see cref="PaymentSessionRequest.Amount"/>.</summary>
    Task<PaymentSession> CreateSessionAsync(PaymentSessionRequest request, CancellationToken cancellationToken = default);

    /// <summary>Consulta el estado actual de una sesión (post-redirect/webhook).</summary>
    Task<PaymentOutcome> GetStatusAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>Captura (cobra) una sesión autorizada. Idempotente.</summary>
    Task<PaymentOutcome> CaptureAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>Reembolsa una sesión capturada (total si <paramref name="amount"/> es null).</summary>
    Task<PaymentOutcome> RefundAsync(string sessionId, decimal? amount = null, CancellationToken cancellationToken = default);
}
