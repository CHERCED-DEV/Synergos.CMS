using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Synergos.Api.Notifications.Domain;
using Synergos.Api.Notifications.Storage;
using Synergos.Api.Notifications.Transport;
using Synergos.Core;

namespace Synergos.CMS.Tests.Api;

/// <summary>
/// El borde hacia el proveedor: qué se manda, cómo se clasifica lo que vuelve, y qué se acepta
/// de regreso (HU #12).
/// </summary>
/// <remarks>
/// <para><b>Lo que más importa acá es la clasificación transitorio ↔ definitivo</b>, y no se ve
/// mirando el código: <see cref="Rejection.IsTransient"/> es lo que decide en <c>Bff.Core</c> si
/// algo se reintenta o se grita una vez. Un 5xx marcado como definitivo es un aviso que nunca
/// sale; un 4xx marcado como transitorio es una tormenta contra un proveedor que ya dijo que no.
/// Ninguna de las dos se nota en un log — se notan en la factura o en la queja del cliente.</para>
/// </remarks>
public sealed class NotificationTransportTests
{
    private sealed class RelojFalso : TimeProvider
    {
        private DateTimeOffset _now;
        public RelojFalso(DateTimeOffset inicio) => _now = inicio;
        public override DateTimeOffset GetUtcNow() => _now;
    }

    /// <summary>Un proveedor de mentira que contesta lo que le digan, o revienta.</summary>
    private sealed class Proveedor : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public HttpRequestMessage? Ultima { get; private set; }
        public string? UltimoCuerpo { get; private set; }

        public Proveedor(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Ultima = request;
            UltimoCuerpo = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return _responder(request);
        }
    }

    private static HttpResponseMessage Responde(HttpStatusCode code, string body)
        => new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static (ResendNotificationSender Sender, Proveedor Espia) Nuevo(
        Func<HttpRequestMessage, HttpResponseMessage> responder, ResendOptions? opciones = null)
    {
        var espia = new Proveedor(responder);
        var op = opciones ?? new ResendOptions { ApiKey = "re_test", From = "Avisos <avisos@ejemplo.co>" };
        var http = new HttpClient(espia) { BaseAddress = new Uri(op.BaseUrl) };
        return (new ResendNotificationSender(http, Options.Create(op), NullLogger<ResendNotificationSender>.Instance), espia);
    }

    private static Task<Result<string>> Mandar(ResendNotificationSender s)
        => s.SendAsync(Channel.Email, "ana@ejemplo.co", "Asunto", "<p>Cuerpo</p>", "envio-1");

    // ── Lo que sale ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Un_envio_aceptado_devuelve_el_id_del_proveedor()
    {
        var (sender, _) = Nuevo(_ => Responde(HttpStatusCode.OK, """{"id":"a1b2c3"}"""));

        var r = await Mandar(sender);

        Assert.True(r.IsOk);
        Assert.Equal("a1b2c3", r.Value);
    }

    [Fact]
    public async Task La_peticion_lleva_la_llave_de_idempotencia_DEL_PROVEEDOR()
    {
        // Cubre el caso que nosotros no podemos ver: la petición que sí llegó y cuya respuesta se
        // perdió. Sin esta cabecera, ese reintento produce dos correos y nuestra propia
        // idempotencia no se entera de nada.
        var (sender, espia) = Nuevo(_ => Responde(HttpStatusCode.OK, """{"id":"a1"}"""));

        await Mandar(sender);

        Assert.Equal("envio-1", espia.Ultima!.Headers.GetValues("Idempotency-Key").Single());
    }

    [Fact]
    public async Task Sin_credenciales_NO_se_toca_la_red_y_se_dice_por_que()
    {
        // Un despliegue sin configurar no puede parecer uno que funciona. Y no se intenta
        // siquiera: llamar sin credencial produciría un 401 que se leería como problema del
        // proveedor en vez de como el olvido que es.
        var (sender, espia) = Nuevo(
            _ => throw new InvalidOperationException("no debería llamarse"),
            new ResendOptions { ApiKey = null, From = null });

        var r = await Mandar(sender);

        Assert.Equal("notifications.transport_not_configured", r.Rejection!.Code);
        Assert.Null(espia.Ultima);
    }

    // ── Cómo se clasifica lo que vuelve ─────────────────────────────────────

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task Un_fallo_del_proveedor_es_TRANSITORIO(HttpStatusCode code)
    {
        var (sender, _) = Nuevo(_ => Responde(code, """{"message":"ups"}"""));

        var r = await Mandar(sender);

        Assert.Equal("notifications.transport_unavailable", r.Rejection!.Code);
        Assert.True(r.Rejection.IsTransient, $"{code} tiene que poder reintentarse");
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    public async Task Un_rechazo_del_proveedor_NO_es_transitorio(HttpStatusCode code)
    {
        // Reintentar un 401 mil veces no arregla una credencial mala: la convierte en una alerta
        // de abuso. Y reintentar una dirección inválida no la hace válida.
        var (sender, _) = Nuevo(_ => Responde(code, """{"message":"no"}"""));

        var r = await Mandar(sender);

        Assert.Equal("notifications.transport_rejected", r.Rejection!.Code);
        Assert.False(r.Rejection.IsTransient);
    }

    [Fact]
    public async Task Si_el_proveedor_acepta_SIN_dar_id_no_se_da_por_aceptado()
    {
        // Un envío aceptado del que nunca se podrá saber si llegó es exactamente lo que esta HU
        // existe para dejar de hacer. Vale más rechazarlo y reintentar.
        var (sender, _) = Nuevo(_ => Responde(HttpStatusCode.OK, """{"algo":"otro"}"""));

        var r = await Mandar(sender);

        Assert.Equal("notifications.transport_unavailable", r.Rejection!.Code);
    }

    [Fact]
    public async Task Un_corte_de_red_no_se_escapa_como_excepcion()
    {
        // El contrato de la costura dice que NO lanza. Si se escapara, el registro del envío
        // quedaría sin actualizar y el rastro perdería justo el caso que importa.
        var (sender, _) = Nuevo(_ => throw new HttpRequestException("connection refused"));

        var r = await Mandar(sender);

        Assert.Equal("notifications.transport_unavailable", r.Rejection!.Code);
        Assert.True(r.Rejection.IsTransient);
    }

    [Fact]
    public async Task Este_transporte_solo_habla_correo()
    {
        var (sender, _) = Nuevo(_ => Responde(HttpStatusCode.OK, """{"id":"a1"}"""));

        Assert.True(sender.Supports(Channel.Email));
        Assert.False(sender.Supports(Channel.Sms));
        Assert.Equal("notifications.channel_unsupported",
            (await sender.SendAsync(Channel.Sms, "3001234567", "a", "b", "k")).Rejection!.Code);
    }

    // ── Lo que se acepta de vuelta ──────────────────────────────────────────

    private const string Secreto = "whsec_c2VjcmV0by1kZS1wcnVlYmEtMTIzNDU2Nzg=";
    private static readonly DateTimeOffset Ahora = new(2026, 8, 3, 9, 0, 0, TimeSpan.Zero);

    private static WebhookVerifier Verificador(string? secreto = Secreto)
        => new(Options.Create(new ResendOptions { WebhookSecret = secreto }), new RelojFalso(Ahora));

    private static WebhookHeaders Firmado(string cuerpo, string id = "msg_1", DateTimeOffset? cuando = null)
    {
        var ts = (cuando ?? Ahora).ToUnixTimeSeconds().ToString();
        return new WebhookHeaders(id, ts, "v1," + WebhookVerifier.Firmar(Secreto, id, ts, cuerpo));
    }

    [Fact]
    public void Un_evento_bien_firmado_pasa()
    {
        const string cuerpo = """{"type":"email.delivered","data":{"email_id":"a1"}}""";

        Assert.Null(Verificador().Verify(Firmado(cuerpo), cuerpo));
    }

    [Fact]
    public void Un_cuerpo_alterado_despues_de_firmar_NO_pasa()
    {
        // Es el ataque entero: un endpoint que escribe, sin llave compartida, alcanzable desde
        // internet. Sin verificar la firma, cualquiera podría marcar como entregado un aviso que
        // rebotó — y en Gobierno "entregado" es lo que hace correr un término.
        const string cuerpo = """{"type":"email.delivered","data":{"email_id":"a1"}}""";
        var cabeceras = Firmado(cuerpo);

        var malo = Verificador().Verify(cabeceras, """{"type":"email.delivered","data":{"email_id":"OTRO"}}""");

        Assert.Equal("notifications.webhook_signature_invalid", malo!.Code);
    }

    [Fact]
    public void Una_peticion_LEGITIMA_pero_vieja_no_se_acepta()
    {
        // Sin ventana, una petición capturada sirve para siempre: no hace falta falsificar nada,
        // basta con repetir una firma legítima que se grabó.
        const string cuerpo = """{"type":"email.delivered","data":{"email_id":"a1"}}""";

        var malo = Verificador().Verify(Firmado(cuerpo, cuando: Ahora.AddHours(-2)), cuerpo);

        Assert.Equal("notifications.webhook_replay", malo!.Code);
    }

    [Fact]
    public void Sin_secreto_configurado_NO_se_acepta_ningun_evento()
    {
        // La alternativa —aceptar todo mientras no se configure— convierte un olvido de
        // despliegue en un endpoint abierto que escribe en los datos.
        const string cuerpo = """{"type":"email.delivered","data":{"email_id":"a1"}}""";

        var malo = Verificador(secreto: null).Verify(Firmado(cuerpo), cuerpo);

        Assert.Equal("notifications.webhook_not_configured", malo!.Code);
    }

    [Fact]
    public void Una_firma_de_otro_secreto_no_pasa()
    {
        const string cuerpo = """{"type":"email.delivered","data":{"email_id":"a1"}}""";
        var otro = "whsec_" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
        var ts = Ahora.ToUnixTimeSeconds().ToString();
        var cabeceras = new WebhookHeaders("msg_1", ts, "v1," + WebhookVerifier.Firmar(otro, "msg_1", ts, cuerpo));

        Assert.Equal("notifications.webhook_signature_invalid", Verificador().Verify(cabeceras, cuerpo)!.Code);
    }

    [Theory]
    [InlineData("email.sent", DeliveryStatus.Accepted)]
    [InlineData("email.delivered", DeliveryStatus.Delivered)]
    [InlineData("email.bounced", DeliveryStatus.Bounced)]
    [InlineData("email.complained", DeliveryStatus.Complained)]
    public void Cada_evento_del_proveedor_se_traduce_a_su_estado(string tipo, DeliveryStatus esperado)
    {
        var r = ProviderEventReader.Read($$"""{"type":"{{tipo}}","data":{"email_id":"a1"} }""");

        Assert.Equal(esperado, r.Value.Status);
        Assert.Equal("a1", r.Value.MessageId);
    }

    [Theory]
    [InlineData("email.opened")]
    [InlineData("email.clicked")]
    [InlineData("email.delivery_delayed")]
    [InlineData("algo.que.no.existia.ayer")]
    public void Un_evento_que_no_es_de_estado_se_lee_sin_error(string tipo)
    {
        // No puede ser un rechazo: el proveedor reintentaría durante días algo que decidimos
        // ignorar a propósito. Quién abrió un correo es otro problema, con otro régimen de datos.
        var r = ProviderEventReader.Read($$"""{"type":"{{tipo}}","data":{"email_id":"a1"} }""");

        Assert.True(r.IsOk);
        Assert.Null(r.Value.Status);
    }

    [Fact]
    public void Un_cuerpo_que_no_es_JSON_se_rechaza_sin_reventar()
        => Assert.Equal("notifications.webhook_unreadable", ProviderEventReader.Read("no soy json").Rejection!.Code);
}
