using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Synergos.Api.Notifications.Domain;
using Synergos.Api.Notifications.Endpoints;
using Synergos.Api.Notifications.Transport;
using Synergos.Api.Notifications.Storage;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.CMS.Tests.Api;

/// <summary>
/// El estado real de un envío: que se reserve antes de tocar la red, y que lo que cuenta el
/// proveedor después no lo haga retroceder (HU #12).
/// </summary>
/// <remarks>
/// <para><b>Antes había dos estados y mentían.</b> <c>Sent</c> quería decir «el transporte lo
/// aceptó», que no es «llegó»: un correo aceptado y rebotado media hora después se quedaba para
/// siempre como enviado. Lo que se prueba acá no es que el enum tenga más valores — es que el
/// rastro diga la verdad cuando el mundo real llega tarde y desordenado.</para>
/// </remarks>
public sealed class NotificationDeliveryStateTests
{
    private sealed class RelojFalso : TimeProvider
    {
        private DateTimeOffset _now;
        public RelojFalso(DateTimeOffset inicio) => _now = inicio;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Avanzar(TimeSpan d) => _now += d;
    }

    /// <summary>Transporte que se puede romper y arreglar a mitad de un test.</summary>
    private sealed class Transporte : INotificationSender
    {
        public List<string> Llaves { get; } = new();
        public Rejection? Falla { get; set; }
        public HashSet<Channel> Canales { get; } = new() { Channel.Email };
        private int _n;

        public bool Supports(Channel channel) => Canales.Contains(channel);

        public Task<Result<string>> SendAsync(
            Channel channel, string address, string subject, string body,
            string idempotencyKey, CancellationToken ct = default)
        {
            Llaves.Add(idempotencyKey);
            return Task.FromResult(Falla is null
                ? Result.Ok($"prov-{++_n}")
                : Result.Rejected<string>(Falla));
        }
    }

    private sealed class Memoria : ITemplateStore, IDeliveryStore, IIdempotencyLedger
    {
        private readonly Dictionary<string, Template> _t = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Delivery> _d = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _k = new(StringComparer.Ordinal);

        Template? ITemplateStore.Find(string id) => _t.GetValueOrDefault(id);
        public Template? FindByKey(string key) => _t.Values.FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));
        public IReadOnlyList<Template> All() => _t.Values.ToList();
        public void Put(Template item) => _t[item.Id] = item;

        Delivery? IDeliveryStore.Find(string id) => _d.GetValueOrDefault(id);
        public Delivery? FindByProviderMessageId(string providerMessageId)
            => _d.Values.FirstOrDefault(x => x.ProviderMessageId == providerMessageId);
        public IReadOnlyList<Delivery> ForRecipient(Ref recipient) => _d.Values.Where(x => x.To == recipient).ToList();
        public IReadOnlyList<Delivery> WithStatus(DeliveryStatus status) => _d.Values.Where(x => x.Status == status).ToList();
        public void Put(Delivery delivery) => _d[delivery.Id] = delivery;

        public string? Find(string scope, IdempotencyKey key) => _k.GetValueOrDefault($"{scope}|{key.Value}");
        public void Remember(string scope, IdempotencyKey key, string resultId) => _k[$"{scope}|{key.Value}"] = resultId;

        public int Envios => _d.Count;
    }

    private static readonly DateTimeOffset Ahora = new(2026, 8, 3, 9, 0, 0, TimeSpan.Zero);
    private static readonly Ref Ana = Ref.Create("identity.member", "m-1");
    private const string Correo = "ana@ejemplo.co";

    private sealed record Contexto(NotificationService Svc, Memoria Store, Transporte Transporte, RelojFalso Reloj);

    private static Contexto Nuevo()
    {
        var store = new Memoria();
        var transporte = new Transporte();
        var reloj = new RelojFalso(Ahora);
        var svc = new NotificationService(store, store, transporte, store, reloj);
        svc.SaveTemplate("aviso", Channel.Email, "Asunto", "Cuerpo", IdempotencyKey.Of("t1"));
        return new Contexto(svc, store, transporte, reloj);
    }

    private static Task<Result<Delivery>> Mandar(Contexto c, string llave = "k1")
        => c.Svc.SendAsync(Ana, Correo, "aviso", null, IdempotencyKey.Of(llave));

    // ── La reserva antes de la red ──────────────────────────────────────────

    [Fact]
    public async Task Un_envio_aceptado_guarda_el_id_del_PROVEEDOR()
    {
        // Sin esto, "entregado" y "rebotado" quedan fuera de alcance PARA SIEMPRE: el proveedor
        // avisa citando su id, no el nuestro. Y no es algo que se pueda añadir después sobre los
        // envíos viejos, porque el evento ya pasó y no vuelve.
        var ctx = Nuevo();

        var r = await Mandar(ctx);

        Assert.True(r.IsOk);
        Assert.Equal(DeliveryStatus.Accepted, r.Value.Status);
        Assert.Equal("prov-1", r.Value.ProviderMessageId);
    }

    [Fact]
    public async Task Un_fallo_TRANSITORIO_deja_el_envio_consultable_y_reintentable()
    {
        // El proveedor caído no puede producir un Failed definitivo: el aviso todavía puede
        // salir. Queda Queued —visible, consultable— y el rechazo dice que se reintente.
        var ctx = Nuevo();
        ctx.Transporte.Falla = NotificationRules.TransportUnavailable("504");

        var r = await Mandar(ctx);

        Assert.False(r.IsOk);
        Assert.Equal("notifications.transport_unavailable", r.Rejection!.Code);
        Assert.True(r.Rejection.IsTransient, "un proveedor caído tiene que poder reintentarse");
        Assert.Equal(1, ctx.Store.Envios);   // el rastro existe aunque no haya salido
        Assert.Equal(DeliveryStatus.Queued, ctx.Store.ForRecipient(Ana)[0].Status);
    }

    [Fact]
    public async Task Reintentar_tras_un_fallo_transitorio_SI_vuelve_a_intentar_el_envio()
    {
        // Es la mitad difícil de la idempotencia. "No mandar dos veces" no puede significar "no
        // mandar nunca más": un envío que se quedó en Queued porque el proveedor estaba caído
        // tiene que poder salir cuando vuelva, con la MISMA llave y sobre el MISMO registro.
        var ctx = Nuevo();
        ctx.Transporte.Falla = NotificationRules.TransportUnavailable("504");
        var primero = await Mandar(ctx);
        Assert.False(primero.IsOk);

        ctx.Transporte.Falla = null;
        var segundo = await Mandar(ctx);

        Assert.True(segundo.IsOk);
        Assert.Equal(DeliveryStatus.Accepted, segundo.Value.Status);
        Assert.Equal(1, ctx.Store.Envios);            // un solo registro
        Assert.Equal(2, ctx.Transporte.Llaves.Count); // dos intentos
    }

    [Fact]
    public async Task Reintentar_lo_que_YA_salio_no_lo_manda_otra_vez()
    {
        // La otra mitad, y la que más duele si falla: a la persona le llega un segundo correo
        // idéntico. Con el envío ya aceptado, la llave devuelve lo que hay y nadie toca la red.
        var ctx = Nuevo();
        var a = await Mandar(ctx);

        var b = await Mandar(ctx);

        Assert.Equal(a.Value.Id, b.Value.Id);
        Assert.Single(ctx.Transporte.Llaves);
    }

    [Fact]
    public async Task El_transporte_recibe_una_llave_ESTABLE_entre_reintentos()
    {
        // Es lo que cubre el caso que nosotros no podemos ver: la petición que sí llegó al
        // proveedor y cuya respuesta se perdió. Si la llave cambiara en cada intento, ese caso
        // produce dos correos y nuestra propia idempotencia no se entera.
        var ctx = Nuevo();
        ctx.Transporte.Falla = NotificationRules.TransportUnavailable("504");
        await Mandar(ctx);
        ctx.Transporte.Falla = null;
        await Mandar(ctx);

        Assert.Equal(ctx.Transporte.Llaves[0], ctx.Transporte.Llaves[1]);
    }

    [Fact]
    public async Task Un_canal_sin_transporte_se_rechaza_ANTES_de_registrar_nada()
    {
        // "Este despliegue no habla SMS" es una decisión de configuración, no un fallo de envío.
        // Si se registrara primero, quedaría un rastro de avisos que nunca tuvieron por dónde
        // salir, indistinguible de los que fallaron de verdad.
        var ctx = Nuevo();
        ctx.Svc.SaveTemplate("sms", Channel.Sms, "A", "B", IdempotencyKey.Of("t2"));

        var r = await ctx.Svc.SendAsync(Ana, "3001234567", "sms", null, IdempotencyKey.Of("k9"));

        Assert.Equal("notifications.channel_unsupported", r.Rejection!.Code);
        Assert.Equal(0, ctx.Store.Envios);
    }

    // ── Lo que el proveedor cuenta después ──────────────────────────────────

    [Fact]
    public async Task El_webhook_lleva_el_envio_de_aceptado_a_entregado()
    {
        var ctx = Nuevo();
        await Mandar(ctx);

        var r = ctx.Svc.RecordProviderEvent("ev-1", "prov-1", DeliveryStatus.Delivered);

        Assert.True(r.IsOk);
        Assert.Equal(DeliveryStatus.Delivered, r.Value.Status);
    }

    [Fact]
    public async Task Un_evento_que_llega_FUERA_DE_ORDEN_no_hace_retroceder_el_estado()
    {
        // Pasa de verdad: los eventos viajan por red y no vienen ordenados. Con "el último gana",
        // un `accepted` que llega después de `delivered` deja marcado como "quizá no llegó" un
        // correo que sí llegó — y ése es justo el dato del que depende un término legal.
        var ctx = Nuevo();
        await Mandar(ctx);
        ctx.Svc.RecordProviderEvent("ev-1", "prov-1", DeliveryStatus.Delivered);

        var tarde = ctx.Svc.RecordProviderEvent("ev-2", "prov-1", DeliveryStatus.Accepted);

        Assert.True(tarde.IsOk);   // no es un error: el proveedor tiene que ver un 2xx
        Assert.Equal(DeliveryStatus.Delivered, tarde.Value.Status);
    }

    [Fact]
    public async Task La_queja_SI_avanza_por_encima_de_la_entrega()
    {
        // Es lo único que ocurre legítimamente después de haber llegado, y cuesta la reputación
        // del remitente: si se ignorara por "ya está entregado", nadie se enteraría.
        var ctx = Nuevo();
        await Mandar(ctx);
        ctx.Svc.RecordProviderEvent("ev-1", "prov-1", DeliveryStatus.Delivered);

        var queja = ctx.Svc.RecordProviderEvent("ev-2", "prov-1", DeliveryStatus.Complained);

        Assert.Equal(DeliveryStatus.Complained, queja.Value.Status);
    }

    [Fact]
    public async Task El_MISMO_evento_dos_veces_se_aplica_una_sola()
    {
        // Los proveedores reentregan el mismo webhook durante días hasta ver un 2xx. La llave es
        // la del evento del proveedor: la nuestra no distingue una reentrega de una novedad.
        var ctx = Nuevo();
        await Mandar(ctx);
        var a = ctx.Svc.RecordProviderEvent("ev-1", "prov-1", DeliveryStatus.Delivered);
        var cuando = a.Value.StatusAtUtc;

        ctx.Reloj.Avanzar(TimeSpan.FromHours(2));
        var b = ctx.Svc.RecordProviderEvent("ev-1", "prov-1", DeliveryStatus.Delivered);

        Assert.True(b.IsOk);
        Assert.Equal(cuando, b.Value.StatusAtUtc);   // no se re-sella
    }

    [Fact]
    public async Task Un_evento_de_un_mensaje_AJENO_no_se_apunta_contra_ninguno()
    {
        // Pasa con correos de otra cuenta del mismo proveedor o de un despliegue anterior. Lo
        // único inaceptable sería apuntarlo contra un envío cualquiera.
        var ctx = Nuevo();
        await Mandar(ctx);

        var r = ctx.Svc.RecordProviderEvent("ev-1", "prov-de-otro", DeliveryStatus.Bounced);

        Assert.Equal("notifications.unknown_provider_message", r.Rejection!.Code);
        Assert.Equal(DeliveryStatus.Accepted, ctx.Store.ForRecipient(Ana)[0].Status);
    }

    [Fact]
    public void Un_evento_sin_su_propio_id_no_se_procesa()
    {
        // Sin id de evento no hay idempotencia posible, y sin idempotencia una reentrega del
        // proveedor se aplica otra vez.
        var ctx = Nuevo();

        Assert.Equal("notifications.provider_event_id_required",
            ctx.Svc.RecordProviderEvent(null, "prov-1", DeliveryStatus.Delivered).Rejection!.Code);
    }

    // ── Que alguien LLAME al verificador ────────────────────────────────────

    [Fact]
    public async Task El_endpoint_del_webhook_NO_procesa_un_evento_sin_firma()
    {
        // Este test existe porque una mutación lo destapó: se quitó la verificación de firma del
        // endpoint y no falló NADA. Los tests del verificador probaban el verificador; ninguno
        // probaba que alguien lo llamara. Un endpoint que escribe en los datos y no va detrás de
        // la llave compartida no puede depender de que nadie borre esa línea.
        var ctx = Nuevo();
        await Mandar(ctx);
        var verificador = new WebhookVerifier(
            Options.Create(new ResendOptions { WebhookSecret = "whsec_c2VjcmV0by1kZS1wcnVlYmEtMTIzNDU2Nzg=" }),
            ctx.Reloj);

        var r = WebhookHandler.Handle(
            new WebhookHeaders("ev-1", null, null),
            """{"type":"email.bounced","data":{"email_id":"prov-1"} }""",
            verificador, ctx.Svc);

        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsAssignableFrom<IStatusCodeHttpResult>(r).StatusCode);
        Assert.Equal(DeliveryStatus.Accepted, ctx.Store.ForRecipient(Ana)[0].Status);   // nada se tocó
    }

    [Fact]
    public async Task El_endpoint_del_webhook_SI_anota_un_evento_bien_firmado()
    {
        // La otra mitad: si el verificador rechazara todo, el test de arriba pasaría igual y el
        // webhook no serviría para nada.
        const string secreto = "whsec_c2VjcmV0by1kZS1wcnVlYmEtMTIzNDU2Nzg=";
        const string cuerpo = """{"type":"email.bounced","data":{"email_id":"prov-1"} }""";
        var ctx = Nuevo();
        await Mandar(ctx);
        var verificador = new WebhookVerifier(Options.Create(new ResendOptions { WebhookSecret = secreto }), ctx.Reloj);
        var ts = Ahora.ToUnixTimeSeconds().ToString();

        WebhookHandler.Handle(
            new WebhookHeaders("ev-1", ts, "v1," + WebhookVerifier.Firmar(secreto, "ev-1", ts, cuerpo)),
            cuerpo, verificador, ctx.Svc);

        Assert.Equal(DeliveryStatus.Bounced, ctx.Store.ForRecipient(Ana)[0].Status);
    }

    // ── La regla del avance, suelta ─────────────────────────────────────────

    [Theory]
    [InlineData(DeliveryStatus.Queued, DeliveryStatus.Accepted, DeliveryStatus.Accepted)]
    [InlineData(DeliveryStatus.Accepted, DeliveryStatus.Delivered, DeliveryStatus.Delivered)]
    [InlineData(DeliveryStatus.Delivered, DeliveryStatus.Accepted, DeliveryStatus.Delivered)]
    [InlineData(DeliveryStatus.Delivered, DeliveryStatus.Complained, DeliveryStatus.Complained)]
    [InlineData(DeliveryStatus.Bounced, DeliveryStatus.Delivered, DeliveryStatus.Bounced)]
    [InlineData(DeliveryStatus.Complained, DeliveryStatus.Delivered, DeliveryStatus.Complained)]
    public void El_estado_avanza_pero_nunca_retrocede(DeliveryStatus actual, DeliveryStatus llega, DeliveryStatus esperado)
        => Assert.Equal(esperado, NotificationRules.Advance(actual, llega));
}
