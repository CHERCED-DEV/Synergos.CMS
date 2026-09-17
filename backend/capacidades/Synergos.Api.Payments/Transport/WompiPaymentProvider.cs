using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Synergos.Api.Payments.Domain;
using Synergos.Core;

namespace Synergos.Api.Payments.Transport;

/// <summary>
/// El medio de pago de verdad, sobre Wompi (Bancolombia).
/// </summary>
/// <remarks>
/// <para><b>Todo lo que este fichero hace es traducir.</b> De un monto, un pagador y una
/// referencia, a una petición HTTP; y del resultado de esa petición, a uno de los cuatro
/// <see cref="PaymentOutcome"/> que ya existen. No decide a quién cobrar, ni cuándo, ni qué se
/// deshace si algo falla — eso ya está decidido cuando lo llaman. Si algún día aparece una regla
/// de negocio acá, está en el fichero equivocado.</para>
///
/// <para><b>Usa Web Checkout, no la API directa con tokenización.</b> Es una decisión, no una
/// limitación: el checkout hospedado cubre tarjeta, PSE, Nequi, Bancolombia y efectivo con UN
/// solo flujo, y deja los datos de tarjeta fuera de nuestros servidores — la carga de PCI se
/// queda en Wompi. La API directa da más control a cambio de tokenizar cada método y asumir ese
/// alcance; es la evolución natural, no el punto de partida. Por eso <see cref="AuthorizeAsync"/>
/// devuelve un <see cref="PaymentAttempt.ActionUrl"/>.</para>
///
/// <para><b>Y por eso «autorizar» acá no es «reservar cupo en el medio de pago».</b> Con checkout
/// hospedado, autorizar es <i>crear la intención y firmarla</i>: nada está reservado y el
/// comprador todavía no vio nada. Quien constata que la plata se movió es
/// <see cref="CaptureAsync"/>, que pregunta — y contesta <b>transitorio</b> mientras la
/// transacción no exista o siga <c>PENDING</c>, que es lo que pasa con PSE durante minutos. Eso
/// encaja con la máquina de sagas sin tocarla: mientras el comprador no complete, capturar se
/// reintenta; cuando el barrido se rinde, libera — y liberar una intención que nadie pagó no
/// cuesta nada.</para>
///
/// <para><b>⚠️ No verificado contra el sandbox de Wompi.</b> Lo escrito responde a la
/// documentación pública; la primera corrida con llaves de prueba es obligatoria antes de
/// confiar en esto.</para>
/// </remarks>
public sealed class WompiPaymentProvider : IPaymentProvider
{
    /// <summary>
    /// Wompi cobra en pesos colombianos y nada más.
    /// </summary>
    /// <remarks>
    /// Se comprueba ACÁ y no en el borde porque es una propiedad del proveedor, no del cobro:
    /// otra pasarela aceptaría dólares y la regla de <c>PaymentRules</c> tendría que saber cuál
    /// está puesta. Y es el único rechazo que este adaptador puede dar <b>sin tocar la red</b>,
    /// que es lo que deja ejercitar de verdad la rama «el pago falló, soltá el stock».
    /// </remarks>
    public const string MonedaUnica = Money.Cop;

    // Wompi responde en snake_case (amount_in_cents, status_message). Sin esta política los
    // campos quedan en su default —cero, null— y el adaptador reportaría «aprobado por $0» en vez
    // de fallar.
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;
    private readonly WompiOptions _options;
    private readonly ILogger<WompiPaymentProvider> _log;

    public WompiPaymentProvider(HttpClient http, IOptions<WompiOptions> options, ILogger<WompiPaymentProvider> log)
    {
        _options = options.Value;
        _log = log;
        _http = http;
        _http.BaseAddress ??= new Uri(_options.BaseUrl);
        _http.Timeout = _options.Timeout;
        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        }
    }

    public string Name => WompiOptions.ProviderName;

    /// <inheritdoc />
    /// <remarks>Dice que SÍ: es el primero del árbol de servicios que mueve plata de verdad.</remarks>
    public bool MuevePlata => true;

    // ── Autorizar: firmar la intención ──────────────────────────────────────

    /// <summary>
    /// Crea la referencia del comercio y firma la URL del checkout. <b>No hay llamada HTTP</b>: la
    /// transacción nace cuando el comprador completa el checkout.
    /// </summary>
    public Task<PaymentAttempt> AuthorizeAsync(Money amount, Ref payer, CancellationToken ct = default)
    {
        if (!_options.IsConfigured)
        {
            return Task.FromResult(PaymentAttempt.NotConfigured($"Falta {_options.QueFalta()}."));
        }

        if (!string.Equals(amount.Currency, MonedaUnica, StringComparison.Ordinal))
        {
            // Rechazo FIRME y con el motivo puesto: reintentar con la misma moneda no la va a
            // volver aceptable, y «el pago falló» no le diría a nadie qué arreglar.
            return Task.FromResult(PaymentAttempt.Declined(
                $"Wompi cobra en {MonedaUnica} y el cobro viene en {amount.Currency}."));
        }

        var centavos = WompiSignature.ToCents(amount.Amount);
        if (centavos <= 0)
        {
            return Task.FromResult(PaymentAttempt.Declined(
                $"Wompi no acepta un cobro de {amount}: en centavos da {centavos}."));
        }

        // La referencia del comercio es lo que se firma y lo único con lo que después se puede
        // conciliar. Es opaca a propósito: el `Ref` del pagador NO entra —una referencia viaja en
        // la URL del navegador del comprador y en los correos de la pasarela—.
        var referencia = $"syn-{Guid.NewGuid():n}";
        var firma = WompiSignature.Integrity(referencia, centavos, MonedaUnica, _options.IntegritySecret!);

        var url = new UrlDeCheckout(_options.CheckoutBaseUrl)
            .Q("public-key", _options.PublicKey!)
            .Q("currency", MonedaUnica)
            .Q("amount-in-cents", centavos.ToString(CultureInfo.InvariantCulture))
            .Q("reference", referencia)
            .Q("signature:integrity", firma)
            .QSiHay("redirect-url", _options.RedirectUrl)
            .Build();

        return Task.FromResult(PaymentAttempt.Ok(referencia, url));
    }

    // ── Capturar: constatar que la plata se movió ───────────────────────────

    /// <summary>
    /// Pregunta por la transacción y traduce su estado.
    /// </summary>
    /// <remarks>
    /// <b>Se comprueba el monto aprobado contra el pedido</b>, y no es paranoia: la firma de
    /// integridad cubre el monto <i>tal como se envió</i>, así que un error de centavos produce
    /// una transacción perfectamente válida por la cifra equivocada. Sin esta comparación, cobrar
    /// cien veces menos pasa el checkout, pasa la firma y se descubre cuadrando la caja.
    /// </remarks>
    public async Task<PaymentAttempt> CaptureAsync(string providerReference, Money amount, CancellationToken ct = default)
    {
        if (!_options.IsConfigured) return PaymentAttempt.NotConfigured($"Falta {_options.QueFalta()}.");

        var consulta = await BuscarAsync(providerReference, ct).ConfigureAwait(false);
        if (consulta.Fallo is { } fallo) return fallo;

        var tx = consulta.Transaccion;
        if (tx is null)
        {
            // Todavía no existe: el comprador no completó el checkout. TRANSITORIO — que es lo
            // que deja al barrido reintentar y, al rendirse, liberar una intención que nadie pagó.
            return PaymentAttempt.Unavailable(
                $"La transacción {providerReference} todavía no existe en Wompi: el comprador no completó el checkout.");
        }

        var estado = (tx.Status ?? string.Empty).Trim().ToUpperInvariant();
        switch (estado)
        {
            case "APPROVED":
                var esperados = WompiSignature.ToCents(amount.Amount);
                if (tx.AmountInCents != esperados)
                {
                    _log.LogError(
                        "Wompi aprobó {Aprobado} centavos y se esperaban {Esperado} en {Ref}.",
                        tx.AmountInCents, esperados, providerReference);
                    return PaymentAttempt.Declined(
                        $"Wompi aprobó {tx.AmountInCents} centavos y el cobro era de {esperados}.");
                }
                return PaymentAttempt.Ok(tx.Id ?? providerReference);

            case "DECLINED":
            case "ERROR":
                return PaymentAttempt.Declined(tx.StatusMessage ?? $"Wompi rechazó la transacción ({estado}).");

            case "VOIDED":
                return PaymentAttempt.Declined("La transacción se anuló en Wompi; hay que volver a cobrar.");

            default:
                // PENDING y lo que Wompi agregue mañana. Un estado nuevo NO puede darse por
                // perdido: transitorio significa «todavía no se sabe», que es la verdad.
                return PaymentAttempt.Unavailable($"Wompi todavía no resuelve {providerReference} (estado {estado}).");
        }
    }

    // ── Devolver ────────────────────────────────────────────────────────────

    /// <summary>Devuelve, total o parcialmente, sobre una transacción aprobada.</summary>
    /// <remarks>
    /// Se pide con el <b>identificador de Wompi</b> y no con la referencia del comercio, así que
    /// hay que buscarla primero. Un reintento de este adaptador es seguro porque la llave de
    /// idempotencia se resuelve <i>antes</i>, en <c>PaymentService</c>: acá nunca llega dos veces
    /// la misma devolución.
    /// </remarks>
    public async Task<PaymentAttempt> RefundAsync(string providerReference, Money amount, CancellationToken ct = default)
    {
        if (!_options.IsConfigured) return PaymentAttempt.NotConfigured($"Falta {_options.QueFalta()}.");

        var consulta = await BuscarAsync(providerReference, ct).ConfigureAwait(false);
        if (consulta.Fallo is { } fallo) return fallo;

        var tx = consulta.Transaccion;
        if (tx is null || !string.Equals(tx.Status, "APPROVED", StringComparison.OrdinalIgnoreCase))
        {
            return PaymentAttempt.Declined(
                $"Solo se devuelve lo aprobado, y {providerReference} está {tx?.Status ?? "sin transacción"}.");
        }

        var carga = JsonSerializer.Serialize(new
        {
            transaction_id = tx.Id,
            amount_in_cents = WompiSignature.ToCents(amount.Amount),
        });

        using var peticion = new HttpRequestMessage(HttpMethod.Post, "refunds")
        {
            Content = new StringContent(carga, Encoding.UTF8, "application/json"),
        };

        var (respuesta, transporte) = await HablarAsync(peticion, ct).ConfigureAwait(false);
        if (transporte is { } caido) return caido;

        using var acuse = respuesta!;
        {
            var texto = await acuse.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return acuse.IsSuccessStatusCode
                ? PaymentAttempt.Ok(tx.Id)
                : Clasificar(acuse.StatusCode, texto, "la devolución");
        }
    }

    // ── Liberar ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Suelta una intención que nadie pagó.
    /// </summary>
    /// <remarks>
    /// <para><b>Acá la compensación cambia de carácter y por eso no es un «siempre sí».</b> Si la
    /// transacción no existe —o Wompi la rechazó— no hubo cobro y liberar es exactamente lo que
    /// quería quien llama. Si está <c>APPROVED</c>, la plata YA se movió: liberar dejaría de ser
    /// liberar y pasaría a ser devolver, y decir que sí ahí es cómo un comprador se queda sin su
    /// dinero mientras el sistema anota que lo soltó.</para>
    ///
    /// <para><b>Y <c>PENDING</c> es transitorio, no un «no».</b> Con PSE el desenlace llega
    /// minutos después: contestar que se liberó sería mentir sobre un cobro que quizá está
    /// ocurriendo. El compensador reintenta; cuando la transacción resuelva, esto contesta que sí
    /// (no se cobró) o que no (se cobró y hace falta una persona), y las dos son la verdad.</para>
    /// </remarks>
    public async Task<PaymentAttempt> VoidAsync(string providerReference, CancellationToken ct = default)
    {
        if (!_options.IsConfigured) return PaymentAttempt.NotConfigured($"Falta {_options.QueFalta()}.");

        var consulta = await BuscarAsync(providerReference, ct).ConfigureAwait(false);
        if (consulta.Fallo is { } fallo) return fallo;

        var estado = (consulta.Transaccion?.Status ?? string.Empty).Trim().ToUpperInvariant();
        return estado switch
        {
            "" or "DECLINED" or "ERROR" or "VOIDED" => PaymentAttempt.Ok(providerReference),

            "APPROVED" => PaymentAttempt.Declined(
                "La transacción ya se aprobó en Wompi: eso no se libera, se devuelve."),

            _ => PaymentAttempt.Unavailable(
                $"Wompi todavía no resuelve {providerReference} (estado {estado}): no se sabe si hubo cobro."),
        };
    }

    // ── El viaje a Wompi ────────────────────────────────────────────────────

    private sealed record Consulta(WompiTransaction? Transaccion, PaymentAttempt? Fallo);

    private async Task<Consulta> BuscarAsync(string referencia, CancellationToken ct)
    {
        using var peticion = new HttpRequestMessage(
            HttpMethod.Get, $"transactions?reference={Uri.EscapeDataString(referencia)}");

        var (respuesta, transporte) = await HablarAsync(peticion, ct).ConfigureAwait(false);
        if (transporte is { } caido) return new Consulta(null, caido);

        using var acuse = respuesta!;
        {
            var texto = await acuse.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!acuse.IsSuccessStatusCode)
            {
                return new Consulta(null, Clasificar(acuse.StatusCode, texto, "la consulta"));
            }

            try
            {
                var cuerpo = JsonSerializer.Deserialize<WompiListResponse>(texto, Json);
                return new Consulta(cuerpo?.Data?.FirstOrDefault(), null);
            }
            catch (JsonException ex)
            {
                // Contestó 200 con algo que no entendemos. NO es un rechazo del comprador: es
                // ruido del que no se puede concluir nada, así que se vuelve a preguntar.
                _log.LogWarning(ex, "Wompi contestó 200 con un cuerpo ilegible.");
                return new Consulta(null, PaymentAttempt.Unavailable("Wompi contestó algo que no se pudo leer."));
            }
        }
    }

    private async Task<(HttpResponseMessage? Respuesta, PaymentAttempt? Fallo)> HablarAsync(
        HttpRequestMessage peticion, CancellationToken ct)
    {
        try
        {
            return (await _http.SendAsync(peticion, ct).ConfigureAwait(false), null);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            // Se agotó el tiempo, NO nos cancelaron. Transitorio: un timeout no dice «no se
            // cobró», dice «no sé», y las dos llevan a acciones opuestas.
            _log.LogWarning("Wompi no respondió en {Timeout}.", _options.Timeout);
            return (null, PaymentAttempt.Unavailable("Wompi no respondió a tiempo."));
        }
        catch (HttpRequestException ex)
        {
            _log.LogWarning(ex, "No se pudo hablar con Wompi.");
            return (null, PaymentAttempt.Unavailable($"No se pudo hablar con Wompi: {ex.Message}"));
        }
    }

    /// <summary>
    /// Qué clase de «no» es un código de estado de Wompi.
    /// </summary>
    /// <remarks>
    /// <para><b>Un 401 NO es un rechazo del comprador.</b> Mapearlo a <c>Declined</c> le diría a
    /// quien compra que su banco dijo que no, cuando lo que pasa es que nuestra llave está mal —y
    /// además lo dejaría sin reintento, así que el cobro se perdería aunque el operador arreglara
    /// la credencial un minuto después. Es exactamente lo que
    /// <see cref="PaymentOutcome.NotConfigured"/> describe: un defecto de despliegue.</para>
    ///
    /// <para>429 y 5xx se reintentan; el resto de los 4xx no — un cuerpo mal formado no se
    /// arregla mandándolo otra vez, y el motivo del proveedor viaja para que se pueda leer qué
    /// estaba mal.</para>
    /// </remarks>
    private PaymentAttempt Clasificar(HttpStatusCode codigo, string cuerpo, string operacion)
    {
        var detalle = $"{(int)codigo} {Recortar(cuerpo)}";

        if (codigo is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            _log.LogError("Wompi no aceptó nuestra credencial en {Operacion}: {Detalle}", operacion, detalle);
            return PaymentAttempt.NotConfigured(
                $"Wompi no aceptó la credencial (Payments:wompi:ApiKey): {detalle}");
        }

        if (codigo == HttpStatusCode.TooManyRequests || (int)codigo >= 500)
        {
            _log.LogWarning("Wompi falló en {Operacion}: {Detalle}", operacion, detalle);
            return PaymentAttempt.Unavailable($"Wompi falló al atender {operacion}: {detalle}");
        }

        _log.LogWarning("Wompi rechazó {Operacion}: {Detalle}", operacion, detalle);
        return PaymentAttempt.Declined($"Wompi rechazó {operacion}: {detalle}");
    }

    /// <summary>Lo que va al log se recorta: la respuesta de un proveedor puede traer el cuerpo entero.</summary>
    private static string Recortar(string s)
        => string.IsNullOrWhiteSpace(s) ? "(sin cuerpo)" : s.Length <= 300 ? s : s[..300] + "…";

    // ── Lo que Wompi contesta ───────────────────────────────────────────────

    private sealed record WompiListResponse(List<WompiTransaction>? Data);

    private sealed record WompiTransaction(
        string? Id, string? Status, string? StatusMessage, long AmountInCents, string? Reference);

    /// <summary>
    /// Arma la URL del checkout escapando siempre y omitiendo lo vacío.
    /// </summary>
    /// <remarks>
    /// Existe porque la firma de integridad viaja EN la URL: un parámetro mal escapado la
    /// invalida y Wompi rechaza la transacción con un mensaje que no dice cuál fue. Concatenar a
    /// mano es exactamente donde se rompe.
    /// </remarks>
    private sealed class UrlDeCheckout
    {
        private readonly StringBuilder _sb;
        private bool _primero = true;

        public UrlDeCheckout(string baseUrl) => _sb = new StringBuilder(baseUrl);

        public UrlDeCheckout Q(string clave, string valor)
        {
            _sb.Append(_primero ? '?' : '&')
               .Append(Uri.EscapeDataString(clave))
               .Append('=')
               .Append(Uri.EscapeDataString(valor));
            _primero = false;
            return this;
        }

        public UrlDeCheckout QSiHay(string clave, string? valor)
            => string.IsNullOrWhiteSpace(valor) ? this : Q(clave, valor);

        public string Build() => _sb.ToString();
    }
}
