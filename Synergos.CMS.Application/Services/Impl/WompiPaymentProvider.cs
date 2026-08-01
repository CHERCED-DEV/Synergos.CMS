using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Adapter de <see cref="IPaymentProvider"/> sobre Wompi (Bancolombia) — el PSP
/// recomendado para arrancar en Colombia, por documentación pública completa,
/// sandbox self-service y cobertura nativa de Nequi y Botón Bancolombia.
/// </summary>
/// <remarks>
/// <para><b>Usa Web Checkout, no la API directa con tokenización.</b> Es una
/// decisión, no una limitación: el checkout hospedado cubre tarjeta, PSE, Nequi,
/// Bancolombia y efectivo con UN solo flujo, y deja los datos de tarjeta fuera
/// de nuestros servidores — la carga de PCI se queda en Wompi. La API directa
/// da más control sobre la experiencia a cambio de tokenizar cada método y
/// asumir ese alcance; es la evolución natural, no el punto de partida.</para>
///
/// <para>Por eso este adapter devuelve siempre
/// <see cref="PaymentActionKind.Redirect"/>: el checkout hospedado resuelve
/// internamente el reto 3DS y la espera de Nequi. Las otras dos formas de
/// <see cref="PaymentAction"/> siguen en el contrato porque las necesita la API
/// directa y porque otros PSPs las exponen — no son andamio muerto.</para>
///
/// <para><b>PSE no confirma en el retorno.</b> El cliente vuelve del banco y la
/// transacción puede seguir <c>PENDING</c> minutos. La documentación de Wompi lo
/// dice literalmente: hay que consultar hasta alcanzar un estado final. Quien
/// resuelve eso de verdad es el webhook (fase 4); <see cref="GetStatusAsync"/>
/// es el respaldo para cuando el evento se pierde.</para>
///
/// <para>Lógica pura en Application: recibe un <see cref="HttpClient"/> ya
/// configurado y no conoce <c>IHttpClientFactory</c> ni ASP.NET Core (ADR 0002).
/// El composer lo arma como named client, igual que los 12 que ya existen.</para>
///
/// <para><b>⚠️ No verificado contra el sandbox de Wompi.</b> Lo escrito responde
/// a la documentación pública; la primera corrida con llaves de prueba es
/// obligatoria antes de confiar en esto.</para>
/// </remarks>
public sealed class WompiPaymentProvider : IPaymentProvider
{
    /// <summary>Base del Web Checkout hospedado.</summary>
    private const string CheckoutBase = "https://checkout.wompi.co/p/";

    // Wompi responde en snake_case (amount_in_cents, status_message). Sin esta
    // política los campos quedan en su default —cero, null— y el adapter
    // reportaría "aprobado por $0" en vez de fallar: un test lo atrapó.
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;
    private readonly PaymentsSettings _settings;

    /// <param name="http">Cliente con <c>BaseAddress</c> apuntando al API de
    ///   Wompi (<c>https://sandbox.wompi.co/v1/</c> o producción) y la
    ///   resiliencia ya aplicada por el composer.</param>
    public WompiPaymentProvider(HttpClient http, PaymentsSettings settings)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public string ProviderKey => "wompi";

    /// <summary>
    /// Arma la URL del Web Checkout con su firma de integridad. No hay llamada
    /// HTTP: la transacción nace cuando el cliente completa el checkout, y
    /// nosotros nos enteramos por el evento.
    /// </summary>
    /// <remarks>
    /// El <c>SessionId</c> que devolvemos es la REFERENCIA del comercio, no un
    /// id de Wompi — todavía no existe ninguno. Es también la llave con la que
    /// se reconcilia después, así que tiene que ser la misma que viaja firmada.
    /// </remarks>
    public Task<PaymentSession> CreateSessionAsync(
        PaymentSessionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(_settings.WompiPublicKey)
            || string.IsNullOrWhiteSpace(_settings.WompiIntegritySecret))
        {
            // Fail-closed y explícito. Sin llaves, un adapter que "casi"
            // funciona es peor que uno que se niega: el checkout llegaría hasta
            // el final y fallaría en el paso más caro de rehacer.
            throw new InvalidOperationException(
                "Wompi requiere WompiPublicKey y WompiIntegritySecret configuradas.");
        }

        var cents = WompiSignature.ToCents(request.Amount);
        var currency = (request.Currency ?? "COP").ToUpperInvariant();
        var signature = WompiSignature.Integrity(
            request.OrderReference, cents, currency, _settings.WompiIntegritySecret);

        var url = new CheckoutUrl(CheckoutBase)
            .Q("public-key", _settings.WompiPublicKey)
            .Q("currency", currency)
            .Q("amount-in-cents", cents.ToString(CultureInfo.InvariantCulture))
            .Q("reference", request.OrderReference)
            .Q("signature:integrity", signature)
            .QIf("redirect-url", ResolveReturnUrl(request))
            .QIf("customer-data:email", request.CustomerEmail)
            // El riel preferido llega como sugerencia: Wompi muestra el resto
            // igual, así que no es un filtro sino un atajo para el comprador.
            .QIf("payment-methods", MapMethod(request.PreferredMethod))
            .Build();

        return Task.FromResult(new PaymentSession(
            SessionId: request.OrderReference,
            Status: PaymentStatus.RequiresAction,
            Action: PaymentAction.Redirect(new Uri(url)),
            ProviderKey: ProviderKey));
    }

    /// <summary>
    /// Consulta la transacción por la referencia del comercio.
    /// </summary>
    public async Task<PaymentOutcome> GetStatusAsync(
        string sessionId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return new PaymentOutcome(sessionId ?? string.Empty, PaymentStatus.Failed, 0m,
                "Referencia vacía.");
        }

        try
        {
            var response = await _http
                .GetAsync($"transactions?reference={Uri.EscapeDataString(sessionId)}", cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                // Un fallo de red o un 5xx NO es un pago fallido: es un pago de
                // estado desconocido. Devolver Failed haría que el llamante
                // liberara stock de una compra que quizá sí ocurrió.
                return new PaymentOutcome(sessionId, PaymentStatus.Pending, 0m,
                    $"Wompi respondió {(int)response.StatusCode}; estado desconocido.");
            }

            var body = await response.Content
                .ReadFromJsonAsync<WompiListResponse>(Json, cancellationToken)
                .ConfigureAwait(false);

            var tx = body?.Data?.FirstOrDefault();
            if (tx is null)
            {
                // Todavía no existe: el cliente no completó el checkout.
                return new PaymentOutcome(sessionId, PaymentStatus.Pending, 0m);
            }

            return new PaymentOutcome(
                sessionId,
                MapStatus(tx.Status),
                MapStatus(tx.Status) == PaymentStatus.Captured
                    ? FromCents(tx.AmountInCents)
                    : 0m,
                tx.StatusMessage);
        }
        catch (HttpRequestException ex)
        {
            return new PaymentOutcome(sessionId, PaymentStatus.Pending, 0m,
                $"No se pudo consultar Wompi: {ex.Message}");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new PaymentOutcome(sessionId, PaymentStatus.Pending, 0m,
                "Timeout consultando Wompi; estado desconocido.");
        }
    }

    /// <summary>
    /// En Web Checkout la captura la hace Wompi al aprobar: no hay una
    /// autorización nuestra que capturar después. Así que capturar es
    /// <b>constatar</b>, y por eso delega en <see cref="GetStatusAsync"/>.
    /// </summary>
    /// <remarks>
    /// Se ignora <paramref name="amount"/> a propósito y se dice acá en vez de
    /// fingir que se honró: la captura parcial exige el flujo de autorización
    /// diferida de la API directa. Un adapter que aceptara el monto en silencio
    /// cobraría el total y reportaría lo pedido.
    /// </remarks>
    public Task<PaymentOutcome> CaptureAsync(
        string sessionId, decimal? amount = null, CancellationToken cancellationToken = default)
        => GetStatusAsync(sessionId, cancellationToken);

    /// <summary>
    /// Web Checkout no deja anular: o el cliente completó y se cobró, o nunca
    /// hubo transacción. Devolver <see cref="PaymentStatus.Cancelled"/> sin más
    /// mentiría sobre un cobro que quizá ocurrió, así que se constata primero.
    /// </summary>
    public async Task<PaymentOutcome> VoidAsync(
        string sessionId, CancellationToken cancellationToken = default)
    {
        var current = await GetStatusAsync(sessionId, cancellationToken).ConfigureAwait(false);

        // Nunca se cobró: anular es exactamente lo que el llamante quería.
        if (current.Status is PaymentStatus.Pending or PaymentStatus.RequiresAction)
        {
            return current with { Status = PaymentStatus.Cancelled, AmountCaptured = 0m };
        }

        // Ya se cobró: esto es un reembolso y el llamante tiene que pedirlo como
        // tal. Decirlo es más útil que un Cancelled falso.
        return current with
        {
            FailureReason = current.Status == PaymentStatus.Captured
                ? "Wompi Web Checkout ya capturó: usá RefundAsync."
                : current.FailureReason,
        };
    }

    /// <summary>Reembolso, total o parcial, sobre una transacción aprobada.</summary>
    public async Task<PaymentOutcome> RefundAsync(
        string sessionId, decimal? amount = null, CancellationToken cancellationToken = default)
    {
        var current = await GetStatusAsync(sessionId, cancellationToken).ConfigureAwait(false);
        if (current.Status != PaymentStatus.Captured)
        {
            return current with
            {
                FailureReason = $"Sólo se reembolsa lo capturado (estado actual {current.Status}).",
            };
        }

        try
        {
            var payload = new Dictionary<string, object?>
            {
                ["transaction_id"] = current.SessionId,
                ["amount_in_cents"] = amount is decimal a
                    ? WompiSignature.ToCents(a)
                    : WompiSignature.ToCents(current.AmountCaptured),
            };

            var response = await _http
                .PostAsJsonAsync("refunds", payload, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                // El reembolso NO se dio por hecho: sigue Captured. Marcarlo
                // como Refunded sin confirmación dejaría al comprador sin plata
                // y al comercio creyendo que se la devolvió.
                return current with
                {
                    FailureReason = $"Wompi rechazó el reembolso ({(int)response.StatusCode}).",
                };
            }

            return current with
            {
                Status = PaymentStatus.Refunded,
                FailureReason = null,
            };
        }
        catch (HttpRequestException ex)
        {
            return current with { FailureReason = $"No se pudo reembolsar: {ex.Message}" };
        }
    }

    // ── Mapeos ───────────────────────────────────────────────────────────

    /// <summary>
    /// Estados de Wompi a los siete canónicos.
    /// </summary>
    /// <remarks>
    /// Lo desconocido cae a <see cref="PaymentStatus.Pending"/> y NO a
    /// <c>Failed</c>: un estado nuevo que Wompi agregue mañana no puede hacer
    /// que el sistema dé por perdida una compra que sigue viva.
    /// </remarks>
    public static PaymentStatus MapStatus(string? wompiStatus) =>
        (wompiStatus ?? string.Empty).Trim().ToUpperInvariant() switch
        {
            "APPROVED" => PaymentStatus.Captured,
            "DECLINED" => PaymentStatus.Failed,
            "ERROR" => PaymentStatus.Failed,
            "VOIDED" => PaymentStatus.Cancelled,
            "PENDING" => PaymentStatus.Pending,
            _ => PaymentStatus.Pending,
        };

    /// <summary>Riel nuestro → nombre de método de Wompi. Desconocido = sin filtro.</summary>
    private static string? MapMethod(string? method) =>
        (method ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "card" => "CARD",
            "pse" => "PSE",
            "nequi" => "NEQUI",
            "bancolombia" => "BANCOLOMBIA_TRANSFER",
            _ => null,
        };

    private static decimal FromCents(long cents) => cents / 100m;

    private string? ResolveReturnUrl(PaymentSessionRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.ReturnUrl)) return request.ReturnUrl;
        if (string.IsNullOrWhiteSpace(_settings.ReturnUrlBase)) return null;
        return $"{_settings.ReturnUrlBase.TrimEnd('/')}/{request.OrderReference}";
    }

    // ── DTOs de respuesta ────────────────────────────────────────────────

    private sealed record WompiListResponse(List<WompiTransaction>? Data);

    private sealed record WompiTransaction(
        string? Id,
        string? Status,
        string? StatusMessage,
        long AmountInCents,
        string? Reference);

    /// <summary>
    /// Arma la URL del checkout escapando siempre y omitiendo lo vacío.
    /// </summary>
    /// <remarks>
    /// Existe porque la firma de integridad viaja EN la URL: un parámetro mal
    /// escapado la invalida y Wompi rechaza la transacción con un mensaje que
    /// no dice cuál fue. Concatenar a mano acá es exactamente donde se rompe.
    /// </remarks>
    private sealed class CheckoutUrl
    {
        private readonly System.Text.StringBuilder _sb;
        private bool _first = true;

        public CheckoutUrl(string baseUrl) => _sb = new System.Text.StringBuilder(baseUrl);

        public CheckoutUrl Q(string key, string value)
        {
            _sb.Append(_first ? '?' : '&')
               .Append(Uri.EscapeDataString(key))
               .Append('=')
               .Append(Uri.EscapeDataString(value));
            _first = false;
            return this;
        }

        public CheckoutUrl QIf(string key, string? value)
            => string.IsNullOrWhiteSpace(value) ? this : Q(key, value);

        public string Build() => _sb.ToString();
    }
}
