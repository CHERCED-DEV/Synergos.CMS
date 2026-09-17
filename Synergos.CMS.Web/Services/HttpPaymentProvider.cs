using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Con qué nombres viaja un cobro a la capacidad.
/// </summary>
/// <param name="SubjectKind">El <c>Kind</c> de aquello que se cobra — un pedido, un expediente.</param>
/// <param name="PayerKind">El <c>Kind</c> de quien paga.</param>
/// <param name="KeyPrefix">
/// Con qué se prefijan las llaves de idempotencia. Las acuña <b>quien cobra</b>, así que dos
/// consumidores distintos con la misma referencia de orden no se pisan.
/// </param>
public sealed record PaymentWireKinds(string SubjectKind, string PayerKind, string KeyPrefix);

/// <summary>
/// El <see cref="IPaymentProvider"/> que mueve plata de verdad — contra
/// <c>Synergos.Api.Payments</c>, sin orquestador en medio (#27).
/// </summary>
/// <remarks>
/// <para><b>Directo a la capacidad, y la condición de eso es que el llamador no orqueste.</b>
/// Tienda (#24), Salud (#25), Eventos (#35) y Viajes (#36) van contra su orquestador porque su
/// flujo compone varios pasos que pueden fallar a la mitad y hay algo que deshacer. Este cliente
/// es el seam de pago a secas: toca <b>una sola</b> capacidad y no sabe qué se está comprando.
/// Quien decide que sea legal encenderlo es el composer, que se niega a cablear el modo
/// <c>Api</c> mientras alguno de esos cuatro siga comprando con el motor en proceso — con plata
/// de verdad detrás, eso sería el CMS orquestando una saga que no sabe deshacer.</para>
///
/// <para><b>La llave de idempotencia se resuelve ANTES que cualquier estado</b>
/// (<c>feedback_idempotency_before_state</c>), y acá eso no es una preferencia: sale de la
/// referencia de orden que trae la petición —acuñada por el llamador antes de pedir nada—, así
/// que un reintento tras un timeout encuentra el cobro que él mismo creó en vez de cobrar dos
/// veces. Derivarla de algo que se lee después (el estado de la sesión, la respuesta anterior)
/// es exactamente el orden que este repo tiene prohibido.</para>
///
/// <para><b>«No» y «no sé» no son lo mismo, y salen distinto.</b> Un rechazo firme del medio de
/// pago se devuelve como <see cref="PaymentStatus.Failed"/> y no se reintenta; una caída, un
/// timeout o un 5xx <b>lanzan</b>, porque el seam no tiene forma de decir «no sé» y contestar
/// <c>Failed</c> ahí sería afirmar que el banco dijo que no. Quien puede decidir qué hacer con
/// esa duda es el llamador: la radicación de Gobierno la traga a propósito —el trámite no se
/// pierde porque el banco tarde— y una compra no debe.</para>
///
/// <para><b>Al pagador se le manda un identificador OPACO, no su correo.</b> La capacidad
/// necesita saber <i>que hay alguien</i> pagando, no quién es. Es lo mismo que ya hacen el
/// cliente de visitas (#33a), el de la tienda (#47) y el asiento de auditoría (#15).</para>
///
/// <para><b>Y es el primer lector del <c>actionUrl</c></b> que <c>Api.Payments</c> emitía sin
/// consumidor desde la HU #27. Con checkout hospedado la transacción nace cuando el comprador la
/// completa: autorizar devuelve a dónde hay que mandarlo, y hasta que vuelva no hay nada que
/// capturar. Eso aquí se traduce a <see cref="PaymentStatus.RequiresAction"/> +
/// <see cref="PaymentAction.Redirect"/>, que es la forma que el seam ya tenía para decirlo
/// (ADR 0116) y que nadie podía producir porque los dos proveedores que había resolvían el cobro
/// dentro del proceso.</para>
/// </remarks>
public sealed class HttpPaymentProvider : IPaymentProvider
{
    /// <summary>Cabecera de la llave compartida.</summary>
    public const string ApiKeyHeader = "X-Synergos-Key";

    /// <summary>Cabecera con la que viaja la identidad verificable de quien paga (HU #14).</summary>
    public const string IdentityHeader = "X-Synergos-Identity";

    /// <summary>Cliente nombrado del seam completo (<c>Synergos:Payments:Mode=Api</c>).</summary>
    public const string SeamClientName = "synergos-api-payments";

    /// <summary>Cliente nombrado de la tasa de Gobierno (<c>Synergos:Gob:Payments:Mode=Api</c>).</summary>
    public const string GovFeeClientName = "synergos-api-payments-gov";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IHttpClientFactory _clientes;
    private readonly string _cliente;
    private readonly Func<PaymentWireKinds> _nombres;
    private readonly ILogger<HttpPaymentProvider> _log;
    private readonly IIdentityTokenIssuer? _identidad;

    public HttpPaymentProvider(
        IHttpClientFactory clientes,
        string clienteNombrado,
        Func<PaymentWireKinds> nombres,
        ILogger<HttpPaymentProvider> log,
        IIdentityTokenIssuer? identity = null)
    {
        _clientes = clientes;
        _cliente = clienteNombrado;
        _nombres = nombres;
        _log = log;
        _identidad = identity;
    }

    /// <inheritdoc />
    public string ProviderKey => "api";

    // ── Autorizar ───────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<PaymentSession> CreateSessionAsync(
        PaymentSessionRequest request, CancellationToken cancellationToken = default)
    {
        // Las MISMAS validaciones que el motor en proceso: el llamador ya las traduce, y
        // cambiarlas acá haría que un vertical se comportara distinto según una bandera.
        ArgumentNullException.ThrowIfNull(request);
        if (request.Amount <= 0m)
        {
            throw new ArgumentException("El monto del pago debe ser mayor a cero.", nameof(request));
        }
        if (string.IsNullOrWhiteSpace(request.Currency))
        {
            throw new ArgumentException("La moneda es obligatoria.", nameof(request));
        }
        if (request.Items is null || request.Items.Count == 0)
        {
            throw new ArgumentException("La sesión de pago requiere al menos una línea.", nameof(request));
        }
        if (string.IsNullOrWhiteSpace(request.OrderReference))
        {
            // Sin ella no hay llave de idempotencia estable, y sin llave un reintento cobra dos
            // veces. Es preferible no cobrar a cobrar sin poder repetir la llamada.
            throw new ArgumentException(
                "La referencia de orden es obligatoria: es la llave de idempotencia del cobro.",
                nameof(request));
        }

        var nombres = _nombres();
        var pagador = PayerId(request);

        using var req = new HttpRequestMessage(HttpMethod.Post, "v1/payments")
        {
            Content = JsonContent.Create(new
            {
                forKind = nombres.SubjectKind,
                forId = request.OrderReference,
                payerKind = nombres.PayerKind,
                payerId = pagador,
                amount = new { amount = request.Amount, currency = request.Currency.Trim().ToUpperInvariant() },
                // El SUELO, siempre, y no se puede omitir: sin afirmacion la capacidad rechaza
                // con `payments.access_requires_identity`, y declarar algo mas fuerte sin
                // presentarlo lo rechaza con `identity.assertion_not_proven` — y hace bien.
                // Quien sube esto a IdentityToken es Api.Payments, tras verificar el token.
                assertion = IdentityAssertions.CmsSession,
            }, options: Json),
        };
        req.Headers.TryAddWithoutValidation("Idempotency-Key", Llave(nombres, request.OrderReference));

        await PresentarIdentidadAsync(req, request, pagador, cancellationToken).ConfigureAwait(false);

        using var res = await Enviar(req, cancellationToken).ConfigureAwait(false);

        if (res.IsSuccessStatusCode)
        {
            var cobro = await Leer(res, cancellationToken).ConfigureAwait(false);
            return new PaymentSession(cobro.Id, EstadoDe(cobro), AccionDe(cobro), ProviderKey);
        }

        var problema = await LeerProblemaAsync(res, cancellationToken).ConfigureAwait(false);
        GritarSiEsLaLlave(res, "autorizar el cobro");

        // Un rechazo FIRME del medio de pago es una respuesta, no un fallo: se devuelve como
        // Failed y no se reintenta. Todo lo demás —no contestó, no está configurado, un 5xx— es
        // «no sé», y eso lanza: ver el <remarks> de la clase.
        if (EsRechazoFirme(res, problema))
        {
            _log.LogWarning(
                "Api.Payments rechazó el cobro de {Orden}: {Code} — {Detalle}",
                request.OrderReference, problema?.Code ?? "-", problema?.Detail ?? "-");

            // Sin identificador a propósito: la capacidad no lo devuelve en un rechazo, y
            // fabricar uno dejaría en el expediente una sesión que no se puede consultar.
            return new PaymentSession(string.Empty, PaymentStatus.Failed, Action: null, ProviderKey);
        }

        throw Caida("autorizar el cobro", res, problema);
    }

    // ── Consultar ───────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<PaymentOutcome> GetStatusAsync(
        string sessionId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) return NoExiste(sessionId);

        var cobro = await BuscarAsync(sessionId, cancellationToken).ConfigureAwait(false);
        return cobro is null ? NoExiste(sessionId) : Resultado(cobro);
    }

    // ── Capturar ────────────────────────────────────────────────────────────

    /// <inheritdoc />
    /// <remarks>
    /// <b><c>Api.Payments</c> no hace captura PARCIAL, y eso no se disimula.</b> El seam la
    /// admite —cobrar por noche una reserva, cobrar sólo lo despachado— y la capacidad captura lo
    /// autorizado o nada. Pedir menos se rechaza diciéndolo; capturar el total «porque es lo que
    /// hay» cobraría de más y el llamador lo leería como un éxito.
    /// </remarks>
    public async Task<PaymentOutcome> CaptureAsync(
        string sessionId, decimal? amount = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) return NoExiste(sessionId);

        if (amount is decimal pedido)
        {
            var actual = await BuscarAsync(sessionId, cancellationToken).ConfigureAwait(false);
            if (actual is null) return NoExiste(sessionId);
            if (pedido != Monto(actual.Amount))
            {
                return Resultado(actual) with
                {
                    FailureReason = $"Api.Payments captura lo autorizado ({Monto(actual.Amount)} "
                        + $"{actual.Amount?.Currency}) o nada; se pidió capturar {pedido}.",
                };
            }
        }

        var nombres = _nombres();
        using var req = new HttpRequestMessage(
            HttpMethod.Post, $"v1/payments/{Uri.EscapeDataString(sessionId)}/capture");
        req.Headers.TryAddWithoutValidation("Idempotency-Key", $"{Llave(nombres, sessionId)}:capture");

        return await MoverAsync(req, sessionId, "capturar el cobro", cancellationToken).ConfigureAwait(false);
    }

    // ── Liberar ─────────────────────────────────────────────────────────────

    /// <inheritdoc />
    /// <remarks>
    /// Sin llave de idempotencia, igual que el endpoint: liberar lo liberado devuelve lo mismo,
    /// y exigir una cabecera que no protege de nada sólo enseña a inventar llaves.
    /// </remarks>
    public async Task<PaymentOutcome> VoidAsync(
        string sessionId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) return NoExiste(sessionId);

        using var req = new HttpRequestMessage(
            HttpMethod.Post, $"v1/payments/{Uri.EscapeDataString(sessionId)}/void");

        return await MoverAsync(req, sessionId, "liberar la autorización", cancellationToken).ConfigureAwait(false);
    }

    // ── Devolver ────────────────────────────────────────────────────────────

    /// <inheritdoc />
    /// <remarks>
    /// <para><b>Devolver es un movimiento RELATIVO</b>, así que lleva llave (#57). La del seam se
    /// arma con el monto porque es lo único que distingue una devolución de otra: el efecto es
    /// que dos devoluciones parciales <i>del mismo monto sobre el mismo cobro</i> se funden en
    /// una. Se eligió ese lado a propósito — no devolver dos veces por un reintento es peor
    /// negocio que obligar a quien devuelve dos mitades iguales a pedirlo como una sola.</para>
    ///
    /// <para>Sin monto se devuelve lo que QUEDE por devolver, leído de la capacidad: el endpoint
    /// exige un monto y no tiene la noción de «todo».</para>
    /// </remarks>
    public async Task<PaymentOutcome> RefundAsync(
        string sessionId, decimal? amount = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) return NoExiste(sessionId);

        var cobro = await BuscarAsync(sessionId, cancellationToken).ConfigureAwait(false);
        if (cobro is null) return NoExiste(sessionId);

        var monto = amount ?? Monto(cobro.Refundable);
        if (monto <= 0m)
        {
            return Resultado(cobro) with { FailureReason = $"No se puede devolver {monto}." };
        }

        var nombres = _nombres();
        using var req = new HttpRequestMessage(
            HttpMethod.Post, $"v1/payments/{Uri.EscapeDataString(sessionId)}/refund")
        {
            Content = JsonContent.Create(new
            {
                amount = new { amount = monto, currency = cobro.Amount?.Currency ?? "COP" },
                reason = "cms",
            }, options: Json),
        };
        req.Headers.TryAddWithoutValidation(
            "Idempotency-Key",
            $"{Llave(nombres, sessionId)}:refund:{monto.ToString(CultureInfo.InvariantCulture)}");

        return await MoverAsync(req, sessionId, "devolver el cobro", cancellationToken).ConfigureAwait(false);
    }

    // ── Contra la capacidad ─────────────────────────────────────────────────

    private HttpClient Cliente() => _clientes.CreateClient(_cliente);

    private async Task<HttpResponseMessage> Enviar(HttpRequestMessage req, CancellationToken ct)
        => await Cliente().SendAsync(req, ct).ConfigureAwait(false);

    private async Task<CobroDto?> BuscarAsync(string sessionId, CancellationToken ct)
    {
        using var res = await Cliente()
            .GetAsync($"v1/payments/{Uri.EscapeDataString(sessionId)}", ct).ConfigureAwait(false);

        if (res.StatusCode == HttpStatusCode.NotFound) return null;
        if (!res.IsSuccessStatusCode)
        {
            var problema = await LeerProblemaAsync(res, ct).ConfigureAwait(false);
            GritarSiEsLaLlave(res, "consultar el cobro");
            throw Caida("consultar el cobro", res, problema);
        }

        return await Leer(res, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Manda una operación que mueve el cobro y devuelve el estado REAL, venga de donde venga.
    /// </summary>
    /// <remarks>
    /// Cuando la capacidad rechaza por estado —ya capturado, ya liberado— lo correcto no es
    /// inventar un resultado sino <b>releer el cobro</b> y contar lo que hay, con el motivo al
    /// lado. Un <c>AmountCaptured</c> en cero sobre un cobro capturado sería un dato falso, y el
    /// llamador lo escribiría en su expediente.
    /// </remarks>
    private async Task<PaymentOutcome> MoverAsync(
        HttpRequestMessage req, string sessionId, string queHacia, CancellationToken ct)
    {
        using var res = await Enviar(req, ct).ConfigureAwait(false);

        if (res.IsSuccessStatusCode)
        {
            return Resultado(await Leer(res, ct).ConfigureAwait(false));
        }

        if (res.StatusCode == HttpStatusCode.NotFound) return NoExiste(sessionId);

        var problema = await LeerProblemaAsync(res, ct).ConfigureAwait(false);
        GritarSiEsLaLlave(res, queHacia);

        if (!EsRechazoFirme(res, problema)) throw Caida(queHacia, res, problema);

        var actual = await BuscarAsync(sessionId, ct).ConfigureAwait(false);
        var motivo = problema?.Detail ?? $"Api.Payments rechazó {queHacia}.";
        return actual is null
            ? NoExiste(sessionId) with { FailureReason = motivo }
            : Resultado(actual) with { FailureReason = motivo };
    }

    private static async Task<CobroDto> Leer(HttpResponseMessage res, CancellationToken ct)
        => await res.Content.ReadFromJsonAsync<CobroDto>(Json, ct).ConfigureAwait(false)
           ?? throw new InvalidOperationException("Api.Payments contestó sin cuerpo.");

    private static async Task<ProblemaDto?> LeerProblemaAsync(HttpResponseMessage res, CancellationToken ct)
    {
        try { return await res.Content.ReadFromJsonAsync<ProblemaDto>(Json, ct).ConfigureAwait(false); }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or HttpRequestException)
        {
            return null;
        }
    }

    /// <summary>
    /// Si lo que contestó la capacidad es un «no» del medio de pago y no un «no sé».
    /// </summary>
    /// <remarks>
    /// <para>La capacidad ya hizo esa distinción por nosotros (<c>PaymentRules.FromAttempt</c>):
    /// un rechazo firme sale como <c>Conflict</c> y marcado <c>transient: false</c>; una caída de
    /// la pasarela y una credencial que falta salen como <c>Unavailable</c> y transitorias. Se
    /// mira <b>esa bandera</b> y no el código de estado: repetir aquí la tabla de códigos sería
    /// una segunda verdad que se desincroniza.</para>
    ///
    /// <para>Si el cuerpo no se puede leer, <b>es «no sé»</b>: tratarlo como rechazo firme
    /// convertiría cualquier intermediario que devuelva HTML en «el banco dijo que no».</para>
    /// </remarks>
    private static bool EsRechazoFirme(HttpResponseMessage res, ProblemaDto? problema)
        => problema is not null
           && problema.Transient == false
           && (int)res.StatusCode is >= 400 and < 500;

    private static InvalidOperationException Caida(
        string queHacia, HttpResponseMessage res, ProblemaDto? problema)
        => new($"Api.Payments no pudo {queHacia} ({(int)res.StatusCode}"
               + (problema?.Code is { Length: > 0 } c ? $", {c}" : string.Empty) + ").");

    /// <summary>
    /// Un 401 es la llave, y sólo un 401.
    /// </summary>
    /// <remarks>
    /// <c>SharedKeyAuth</c> emite <b>401 y nunca 403</b>. Tratar el 403 como «llave mala» manda a
    /// revisar una configuración correcta mientras el rechazo real pasa desapercibido; ya se
    /// cometió dos veces en este repo y lo destapó levantar los procesos, no un test.
    /// </remarks>
    private void GritarSiEsLaLlave(HttpResponseMessage res, string queHacia)
    {
        if (res.StatusCode != HttpStatusCode.Unauthorized) return;

        _log.LogError(
            "Api.Payments respondió 401 al {Que}: la llave compartida falta o es inválida. "
            + "Revisar Synergos:Payments:ApiKey (o Synergos:Gob:Payments:ApiKey).", queHacia);
    }

    // ── Traducción ──────────────────────────────────────────────────────────

    /// <summary>La llave de idempotencia: el prefijo de quien cobra + la referencia del llamador.</summary>
    private static string Llave(PaymentWireKinds nombres, string referencia)
        => $"{nombres.KeyPrefix}:{referencia}".ToLowerInvariant();

    /// <summary>
    /// Un identificador estable del pagador que <b>no es su correo</b>.
    /// </summary>
    /// <remarks>
    /// Estable para que dos cobros de la misma persona se reconozcan, y opaco para que la
    /// capacidad —que cuenta plata, no personas— no acumule direcciones de correo. No pretende
    /// ser anonimato: es no esparcir lo que no hace falta esparcir.
    /// </remarks>
    private static string Seudonimo(string quien) => SeudonimoDePersona.De(quien);

    /// <summary>Quién paga, en el vocabulario del árbol de servicios.</summary>
    /// <remarks>
    /// <para><b>El <c>MemberKey</c> cuando hay sesión, y el seudónimo del correo si no.</b> La
    /// política vive acá y no dentro del helper (#120): con sesión, quien paga ES su miembro y no
    /// hace falta seudónimo. Es la misma decisión que <c>HttpShopOrderService.BuyerId</c>.</para>
    ///
    /// <para><b>Y es lo que hace que el token sea prueba y no adorno</b>: la capacidad rechaza un
    /// token que nombre a otro (<c>token_subject_mismatch</c>), así que el sujeto que se firma
    /// tiene que ser EXACTAMENTE este valor. Presentar un token del miembro mientras viaja el
    /// seudónimo de su correo no daría un cobro peor firmado: daría un rechazo.</para>
    ///
    /// <para><b>Los cobros anteriores no se tocan.</b> Un pagador con sesión que ya pagó quedó
    /// anotado con el seudónimo de su correo, y eso sigue diciendo la verdad sobre sí mismo. Es
    /// el mismo criterio que el <c>PaidWith</c> nulo: lo viejo no se reescribe para que parezca
    /// nuevo.</para>
    /// </remarks>
    internal static string PayerId(PaymentSessionRequest request)
        => request.PayerMemberKey is Guid k && k != Guid.Empty
            ? k.ToString("n")
            : Seudonimo(request.CustomerEmail ?? request.OrderReference);

    /// <summary>
    /// Presenta la identidad de quien paga, <b>sólo si hay sesión detrás</b>.
    /// </summary>
    /// <remarks>
    /// <para><b>Sin <c>MemberKey</c> no se pide token, y no es pereza.</b> En el pago de invitado
    /// el pagador es el seudónimo de un correo que alguien escribió en un formulario y que no ha
    /// comprobado nadie: pedir un token para él haría que la capacidad anotara
    /// <c>IdentityToken</c> sobre una identidad que no verificó nadie — el defecto #42, ahora con
    /// la firma de por medio para taparlo mejor.</para>
    ///
    /// <para><b>El emisor NUNCA lanza</b>, así que sin <c>Api.Identity</c> esto es <c>null</c> y
    /// el cobro sale declarando <c>CmsSession</c>, que es lo que se hacía antes. Un trámite no se
    /// cae porque la identidad esté caída — y acá menos, porque el motor ya decidió que la
    /// radicación no se aborta si la tasa no sale.</para>
    /// </remarks>
    private async Task PresentarIdentidadAsync(
        HttpRequestMessage req, PaymentSessionRequest request, string pagador, CancellationToken ct)
    {
        if (_identidad is null) return;
        if (request.PayerMemberKey is not Guid miembro || miembro == Guid.Empty) return;

        var nombres = _nombres();
        var token = await _identidad.IssueAsync(
            new IdentitySubject(nombres.PayerKind, pagador, Array.Empty<string>()), ct).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(token))
        {
            req.Headers.TryAddWithoutValidation(IdentityHeader, token);
        }
    }

    /// <summary>
    /// El <c>actionUrl</c> de la capacidad, leído (#27).
    /// </summary>
    /// <remarks>
    /// Es el único sitio del repo que lo consume. Con checkout hospedado, autorizar sólo firma la
    /// intención y la transacción nace cuando el comprador la completa: sin esto, el CMS creería
    /// que ya hay algo que capturar.
    /// </remarks>
    private static PaymentAction? AccionDe(CobroDto cobro)
        => string.IsNullOrWhiteSpace(cobro.ActionUrl)
            ? null
            : PaymentAction.Redirect(new Uri(cobro.ActionUrl!, UriKind.RelativeOrAbsolute));

    private static PaymentStatus EstadoDe(CobroDto cobro) => cobro.Status switch
    {
        // Autorizado CON acción pendiente no es autorizado para quien llama: no hay nada que
        // capturar hasta que el comprador vuelva.
        "Authorized" => string.IsNullOrWhiteSpace(cobro.ActionUrl)
            ? PaymentStatus.Authorized
            : PaymentStatus.RequiresAction,
        "Captured" => Monto(cobro.Refunded) > 0m && Monto(cobro.Refundable) <= 0m
            ? PaymentStatus.Refunded
            : PaymentStatus.Captured,
        // «Liberado sin cobrar» es Cancelled en el vocabulario del CMS; Refunded sería decir que
        // salió plata que nunca entró.
        "Voided" => PaymentStatus.Cancelled,
        _ => PaymentStatus.Failed,
    };

    private static PaymentOutcome Resultado(CobroDto cobro)
        => new(cobro.Id,
            EstadoDe(cobro),
            cobro.Status == "Captured" ? Monto(cobro.Amount) : 0m,
            null,
            Monto(cobro.Refunded));

    private static PaymentOutcome NoExiste(string? sessionId)
        => new(sessionId ?? string.Empty, PaymentStatus.Failed, 0m, "Sesión de pago no encontrada.");

    // La forma de lo que llega. Vive acá porque es el contrato HTTP con una capacidad concreta,
    // no vocabulario del CMS.
    private sealed record MontoDto(decimal Amount, string? Currency);

    /// <summary>
    /// Los montos llegan ANULABLES a propósito.
    /// </summary>
    /// <remarks>
    /// La capacidad los emite siempre, y aun así leerlos como no-anulables convertiría una
    /// respuesta rara —un intermediario, una versión anterior— en un <c>NullReferenceException</c>
    /// a mitad de un cobro. Ausente se lee como cero, que es lo que significa.
    /// </remarks>
    private static decimal Monto(MontoDto? m) => m?.Amount ?? 0m;

    private sealed record CobroDto(
        string Id, string Status, MontoDto? Amount, MontoDto? Refunded, MontoDto? Refundable,
        string? ActionUrl);

    private sealed record ProblemaDto(string? Code, string? Detail, bool? Transient);
}
