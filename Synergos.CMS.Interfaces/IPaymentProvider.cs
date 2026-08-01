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

    /// <summary>
    /// El TITULAR abrió una disputa ante su emisor. No es un reembolso: no lo
    /// inicia el comercio, llega semanas o meses después de
    /// <see cref="Captured"/>, y todavía puede resolverse a favor del comercio.
    /// </summary>
    Disputed,

    /// <summary>
    /// La disputa se resolvió CONTRA el comercio y el emisor retiró los fondos.
    /// Se distingue de <see cref="Refunded"/> a propósito: contablemente son
    /// hechos distintos —uno lo decide el comercio, el otro se lo imponen— y
    /// fundirlos hace que la conciliación mienta.
    /// </summary>
    ChargedBack,
}

/// <summary>
/// Qué tiene que hacer el cliente para que el pago avance. Hay TRES formas y
/// son incompatibles entre sí; modelarlas como una sola URL —que es lo que
/// hacía el par <c>RedirectUrl</c>/<c>ClientSecret</c>— deja fuera a dos.
/// </summary>
public enum PaymentActionKind
{
    /// <summary>Nada que hacer: el pago siguió sin intervención.</summary>
    None,

    /// <summary>
    /// Sacar al cliente del sitio hacia el PSP o el banco. Es el flujo de
    /// <b>PSE</b>, donde el resultado NO vuelve en el retorno: llega después
    /// por webhook.
    /// </summary>
    Redirect,

    /// <summary>
    /// Pintar un reto dentro de la propia página, en un iframe. Es el flujo de
    /// <b>3DS2 con challenge</b> (HTML servido por el emisor) y el de los
    /// formularios embebidos tipo Elements (<see cref="PaymentAction.ClientSecret"/>).
    /// Se resuelve consultando el estado cada <see cref="PaymentAction.PollInterval"/>.
    /// </summary>
    InlineChallenge,

    /// <summary>
    /// No hay nada que pintar: el cliente aprueba en OTRO dispositivo. Es el
    /// flujo de <b>Nequi</b> (notificación push en el celular). La UI solo puede
    /// explicar y esperar — de ahí <see cref="PaymentAction.UserHint"/>.
    /// </summary>
    AwaitApproval,
}

/// <summary>
/// La acción pendiente del cliente. Los campos son excluyentes según
/// <see cref="Kind"/>; el emisor llena solo los que su modo usa.
/// </summary>
/// <param name="Kind">Cuál de las tres formas es.</param>
/// <param name="RedirectUrl">Destino. Solo <see cref="PaymentActionKind.Redirect"/>.</param>
/// <param name="InlineHtml">HTML del reto, para inyectar en un iframe.
///   Solo <see cref="PaymentActionKind.InlineChallenge"/>.</param>
/// <param name="ClientSecret">Secreto de sesión para SDKs embebidos.
///   Solo <see cref="PaymentActionKind.InlineChallenge"/>.</param>
/// <param name="PollInterval">Cada cuánto reconsultar el estado. Modos
///   <see cref="PaymentActionKind.InlineChallenge"/> y
///   <see cref="PaymentActionKind.AwaitApproval"/>.</param>
/// <param name="UserHint">Qué decirle al cliente que haga, en su idioma.
///   Sobre todo <see cref="PaymentActionKind.AwaitApproval"/>.</param>
public sealed record PaymentAction(
    PaymentActionKind Kind,
    Uri? RedirectUrl = null,
    string? InlineHtml = null,
    string? ClientSecret = null,
    TimeSpan? PollInterval = null,
    string? UserHint = null)
{
    /// <summary>Redirect completo — PSE, checkout hospedado.</summary>
    public static PaymentAction Redirect(Uri url) =>
        new(PaymentActionKind.Redirect, RedirectUrl: url);

    /// <summary>Reto embebido — 3DS2 con HTML del emisor.</summary>
    public static PaymentAction Challenge(string html, TimeSpan? poll = null) =>
        new(PaymentActionKind.InlineChallenge, InlineHtml: html,
            PollInterval: poll ?? TimeSpan.FromSeconds(3));

    /// <summary>Formulario embebido con secreto de sesión (tipo Elements).</summary>
    public static PaymentAction Embedded(string clientSecret, TimeSpan? poll = null) =>
        new(PaymentActionKind.InlineChallenge, ClientSecret: clientSecret,
            PollInterval: poll ?? TimeSpan.FromSeconds(3));

    /// <summary>Aprobación en otro dispositivo — Nequi.</summary>
    public static PaymentAction AwaitApproval(string userHint, TimeSpan? poll = null) =>
        new(PaymentActionKind.AwaitApproval, UserHint: userHint,
            PollInterval: poll ?? TimeSpan.FromSeconds(5));
}

/// <summary>Línea de un pago (para desglose/recibo del PSP).</summary>
public sealed record PaymentLineItem(string Sku, string Description, decimal UnitPrice, int Quantity);

/// <summary>
/// Solicitud para abrir una sesión de pago. Genérica para cualquier nicho
/// (hoteles/aerolíneas/eventos/tienda…): el motor arma esto desde el
/// carrito/reserva y el PSP lo cobra.
/// </summary>
/// <param name="Vertical">Qué negocio cobra: <c>"shop"</c>, <c>"events"</c>,
///   <c>"travel"</c>, <c>"academy"</c>, <c>"gov"</c>… Lo usa el router para
///   elegir proveedor, y el despacho de webhooks para saber a quién avisar.
///   Opcional para no romper a quien no enruta.</param>
/// <param name="CountryCode">ISO 3166-1 alfa-2 (<c>"CO"</c>). Criterio de
///   enrutamiento junto con la moneda.</param>
/// <param name="PreferredMethod">Riel pedido por el cliente: <c>"card"</c>,
///   <c>"pse"</c>, <c>"nequi"</c>, <c>"bancolombia"</c>. No todos los
///   proveedores tienen todos, y de esto depende cuál de las tres formas de
///   <see cref="PaymentAction"/> va a devolver el adapter.</param>
public sealed record PaymentSessionRequest(
    string OrderReference,
    decimal Amount,
    string Currency,
    IReadOnlyList<PaymentLineItem> Items,
    string? CustomerEmail = null,
    string? ReturnUrl = null,
    IReadOnlyDictionary<string, string>? Metadata = null,
    string? Vertical = null,
    string? CountryCode = null,
    string? PreferredMethod = null);

/// <summary>
/// Sesión de pago abierta.
/// </summary>
/// <param name="SessionId">Id de la sesión. Cuando quien responde es el router
///   (<c>RoutingPaymentProvider</c>) viene prefijado con el proveedor real
///   —<c>"wompi|ch_123"</c>— para que Capture/Status/Refund/Void enruten sin
///   almacenamiento aparte.</param>
/// <param name="Status">Estado canónico.</param>
/// <param name="Action">Qué debe hacer el cliente, si algo. <c>null</c> o
///   <see cref="PaymentActionKind.None"/> cuando no hay nada pendiente.
///   Reemplaza al par <c>RedirectUrl</c>/<c>ClientSecret</c>, que solo sabía
///   expresar el redirect y dejaba fuera el reto embebido de 3DS2 y la espera
///   de aprobación de Nequi.</param>
/// <param name="ProviderKey">Qué adapter la creó.</param>
public sealed record PaymentSession(
    string SessionId,
    PaymentStatus Status,
    PaymentAction? Action = null,
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

    /// <summary>
    /// Captura (cobra) una sesión autorizada. Idempotente.
    /// </summary>
    /// <param name="amount">Monto a capturar; <c>null</c> = el total autorizado.
    ///   La captura PARCIAL es lo que permite cobrar por noche una reserva de
    ///   hotel o cobrar solo lo que finalmente se despachó. Antes esta firma no
    ///   lo aceptaba mientras <see cref="RefundAsync"/> sí: una asimetría que no
    ///   respondía a nada del dominio.</param>
    Task<PaymentOutcome> CaptureAsync(string sessionId, decimal? amount = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Anula una sesión AUTORIZADA sin cobrar, liberando los fondos retenidos.
    /// Idempotente.
    /// </summary>
    /// <remarks>
    /// No es un reembolso: no hubo cobro. Es la contrapartida de
    /// <see cref="CaptureAsync"/> y la pieza que faltaba para compensar un hold
    /// cuando el negocio decide no seguir — soltar el cupo de una cita cuyo
    /// copago no capturó, o cancelar una reserva dentro del plazo sin penalidad.
    /// Sin esto, cada vertical improvisaba: unos confirmaban igual, otros
    /// calculaban el reembolso y no lo ejecutaban.
    /// </remarks>
    Task<PaymentOutcome> VoidAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>Reembolsa una sesión capturada (total si <paramref name="amount"/> es null).</summary>
    Task<PaymentOutcome> RefundAsync(string sessionId, decimal? amount = null, CancellationToken cancellationToken = default);
}
