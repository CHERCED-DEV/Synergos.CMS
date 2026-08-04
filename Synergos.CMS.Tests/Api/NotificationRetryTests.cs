using Synergos.Api.Notifications.Domain;
using Synergos.Api.Notifications.Storage;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.CMS.Tests.Api;

/// <summary>
/// Lo que la capacidad pone para que alguien pueda barrer los avisos colgados (HU #29).
/// </summary>
/// <remarks>
/// <para><b>El reparto que estos tests fijan:</b> acá vive QUÉ está colgado y CÓMO se reintenta un
/// envío; el CUÁNDO y el CUÁNTAS VECES viven en <c>Bff.Core</c>. Por eso no hay ni un test de
/// techo en este fichero: buscarlo acá sería la señal de que la lógica se duplicó.</para>
///
/// <para><b>Y el defecto que cierran:</b> un <c>Queued</c> eterno se lee como «va en camino». Un
/// aviso que nadie reintenta y nadie abandona es peor que uno que falla, porque el rastro afirma
/// lo contrario de lo que pasó.</para>
/// </remarks>
public sealed class NotificationRetryTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 8, 4, 10, 0, 0, TimeSpan.Zero);
    private static readonly Ref Ana = Ref.Create("identity.member", "m-1");
    private const string Correo = "ana@ejemplo.co";

    private sealed class Reloj : TimeProvider
    {
        private DateTimeOffset _now = Ahora;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Avanzar(TimeSpan d) => _now += d;
    }

    /// <summary>Transporte que falla a pedido y puede quedarse colgado hasta que se le suelte.</summary>
    private sealed class Transporte : INotificationSender
    {
        public Rejection? Falla { get; set; }
        public int Salidas;

        /// <summary>Si viene, el envío espera acá dentro — así se puede provocar el solape real.</summary>
        public TaskCompletionSource? Retencion { get; set; }

        /// <summary>Se completa cuando un envío ya entró y está esperando en <see cref="Retencion"/>.</summary>
        public TaskCompletionSource Entro { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Supports(Channel channel) => true;

        public async Task<Result<string>> SendAsync(
            Channel channel, string address, string subject, string body,
            string idempotencyKey, CancellationToken ct = default)
        {
            Interlocked.Increment(ref Salidas);
            Entro.TrySetResult();
            if (Retencion is { } espera) await espera.Task.ConfigureAwait(false);
            return Falla is null ? Result.Ok($"prov-{Salidas}") : Result.Rejected<string>(Falla);
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
        public Delivery? FindByProviderMessageId(string p) => _d.Values.FirstOrDefault(x => x.ProviderMessageId == p);
        public IReadOnlyList<Delivery> ForRecipient(Ref to) => _d.Values.Where(x => x.To == to).ToList();
        public IReadOnlyList<Delivery> WithStatus(DeliveryStatus s) => _d.Values.Where(x => x.Status == s).ToList();
        public void Put(Delivery d) => _d[d.Id] = d;

        public string? Find(string scope, IdempotencyKey key) => _k.GetValueOrDefault($"{scope}|{key.Value}");
        public void Remember(string scope, IdempotencyKey key, string id) => _k[$"{scope}|{key.Value}"] = id;
    }

    private sealed record Contexto(NotificationService Svc, Transporte Transporte, Reloj Reloj);

    private static Contexto Nuevo()
    {
        var store = new Memoria();
        var transporte = new Transporte();
        var reloj = new Reloj();
        var svc = new NotificationService(store, store, transporte, store, reloj);
        svc.SaveTemplate("cita.recordatorio", Channel.Email, "Tu cita", "Te esperamos.", IdempotencyKey.Of("t"));
        return new Contexto(svc, transporte, reloj);
    }

    /// <summary>Deja un envío colgado de verdad: el proveedor falló con algo transitorio.</summary>
    private static async Task<Delivery> Colgado(Contexto ctx, string llave = "k1")
    {
        ctx.Transporte.Falla = NotificationRules.TransportUnavailable("timeout");
        var r = await ctx.Svc.SendAsync(Ana, Correo, "cita.recordatorio", null, IdempotencyKey.Of(llave));

        Assert.False(r.IsOk);
        var envio = ctx.Svc.GetDelivery(ctx.Svc.ListQueued(0, 50).Value.Items[^1].Id).Value;
        Assert.Equal(DeliveryStatus.Queued, envio.Status);
        return envio;
    }

    // ── Qué está colgado ────────────────────────────────────────────────────

    [Fact]
    public async Task Lo_QUEUED_se_ve_sin_saber_de_quien_es()
    {
        // `ListDeliveries` EXIGE destinatario —sin él sería un volcado del rastro de todo el
        // mundo—. El barrido necesita exactamente lo contrario, y por eso es otra consulta y no
        // un parámetro opcional de aquélla: aflojar aquel filtro abriría el volcado a cualquiera.
        var ctx = Nuevo();
        await Colgado(ctx);

        Assert.Single(ctx.Svc.ListQueued(0, 50).Value.Items);
        Assert.False(ctx.Svc.ListDeliveries(null, 0, 50).IsOk);
    }

    [Fact]
    public async Task Lo_que_YA_salio_no_esta_colgado()
    {
        var ctx = Nuevo();
        await ctx.Svc.SendAsync(Ana, Correo, "cita.recordatorio", null, IdempotencyKey.Of("sale"));

        Assert.Empty(ctx.Svc.ListQueued(0, 50).Value.Items);
    }

    [Fact]
    public async Task El_orden_es_por_antiguedad()
    {
        // Si el barrido se corta a la mitad, lo atendido es lo que más lleva esperando.
        var ctx = Nuevo();
        var primero = await Colgado(ctx, "a");
        ctx.Reloj.Avanzar(TimeSpan.FromMinutes(10));
        var segundo = await Colgado(ctx, "b");

        var orden = ctx.Svc.ListQueued(0, 50).Value.Items.Select(d => d.Id).ToList();

        Assert.Equal(new[] { primero.Id, segundo.Id }, orden);
    }

    // ── Cómo se reintenta ───────────────────────────────────────────────────

    [Fact]
    public async Task Reintentar_NO_pide_la_llave_original()
    {
        // El registro guarda todo lo que el envío necesita. Exigir la llave del llamador obligaría
        // al barrido a guardar un estado que no es suyo — y no lo tiene: el llamador se fue.
        var ctx = Nuevo();
        var envio = await Colgado(ctx);
        ctx.Transporte.Falla = null;

        var r = await ctx.Svc.RetryAsync(envio.Id);

        Assert.True(r.IsOk);
        Assert.Equal(DeliveryStatus.Accepted, r.Value.Status);
        Assert.NotNull(r.Value.ProviderMessageId);
    }

    [Fact]
    public async Task El_contador_sube_SALGA_COMO_SALGA()
    {
        // Si solo subiera al fallar, un envío que alterna fallo y silencio no llegaría nunca al
        // techo: se reintentaría para siempre, que es justo lo que esta HU viene a cerrar.
        var ctx = Nuevo();
        var envio = await Colgado(ctx);

        await ctx.Svc.RetryAsync(envio.Id);
        Assert.Equal(1, ctx.Svc.GetDelivery(envio.Id).Value.Attempts);

        ctx.Transporte.Falla = null;
        await ctx.Svc.RetryAsync(envio.Id);
        Assert.Equal(2, ctx.Svc.GetDelivery(envio.Id).Value.Attempts);
    }

    [Fact]
    public async Task La_ultima_causa_queda_escrita()
    {
        // Sin esto, «se rindió tras ocho intentos» no le dice a nadie qué arreglar.
        var ctx = Nuevo();
        var envio = await Colgado(ctx);

        await ctx.Svc.RetryAsync(envio.Id);

        Assert.Contains("timeout", ctx.Svc.GetDelivery(envio.Id).Value.LastError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Un_rechazo_DEFINITIVO_saca_el_envio_del_barrido()
    {
        // «La dirección no existe» no mejora con insistir. Reintentarlo ocho veces gasta la cuota
        // del proveedor y castiga la reputación del remitente.
        var ctx = Nuevo();
        var envio = await Colgado(ctx);
        ctx.Transporte.Falla = NotificationRules.TransportRejected("dirección inexistente");

        await ctx.Svc.RetryAsync(envio.Id);

        Assert.Equal(DeliveryStatus.Failed, ctx.Svc.GetDelivery(envio.Id).Value.Status);
        Assert.Empty(ctx.Svc.ListQueued(0, 50).Value.Items);
    }

    [Fact]
    public async Task Lo_que_YA_salio_no_se_reintenta()
    {
        var ctx = Nuevo();
        var r = await ctx.Svc.SendAsync(Ana, Correo, "cita.recordatorio", null, IdempotencyKey.Of("ok"));

        var reintento = await ctx.Svc.RetryAsync(r.Value.Id);

        Assert.False(reintento.IsOk);
        Assert.EndsWith(".not_retryable", reintento.Rejection!.Code, StringComparison.Ordinal);
        Assert.Equal(1, ctx.Transporte.Salidas);   // no salió un segundo correo
    }

    [Fact]
    public async Task Dos_barridos_a_la_vez_NO_mandan_dos_avisos()
    {
        // Cada orquestador levanta su propio barrido, así que esto NO es hipotético: dos pueden
        // pedir el reintento del mismo envío en el mismo segundo. El cerrojo del servicio no
        // alcanza porque el envío se hace fuera de él —a propósito, para no dejar la capacidad
        // esperando a un tercero— y en esa ventana los dos ven `Queued`.
        //
        // El transporte se queda RETENIDO adentro para que el solape sea real y no una carrera
        // que a veces ocurra.
        var ctx = Nuevo();
        var envio = await Colgado(ctx);

        ctx.Transporte.Falla = null;
        ctx.Transporte.Retencion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var primero = ctx.Svc.RetryAsync(envio.Id);
        await ctx.Transporte.Entro.Task;              // el primero ya está dentro del proveedor

        // Acotado, y no por prolijidad: al quitar la guardia, el segundo intento ENTRA al
        // proveedor y se queda esperando ahí junto al primero — así que un `await` a secas cuelga
        // el test para siempre en vez de ponerlo rojo. Un test que se cuelga al mutar no vigila:
        // deja la corrida parada y a nadie mirando. (Pasó de verdad, y costó una corrida entera.)
        var tarea = ctx.Svc.RetryAsync(envio.Id);
        Assert.Same(tarea, await Task.WhenAny(tarea, Task.Delay(TimeSpan.FromSeconds(10))));

        var segundo = await tarea;

        Assert.False(segundo.IsOk);
        Assert.EndsWith(".retry_in_flight", segundo.Rejection!.Code, StringComparison.Ordinal);

        // Y transitorio, no definitivo: el otro intento está EN CURSO, no es que el envío sea
        // irreintentable. Si se marcara como conflicto, el barrido dejaría de volver por él.
        Assert.True(segundo.Rejection.IsTransient);

        ctx.Transporte.Retencion.SetResult();
        await primero;

        Assert.Equal(2, ctx.Transporte.Salidas);      // el original colgado + UN reintento
    }

    [Fact]
    public async Task Cuando_el_reintento_termina_el_envio_vuelve_a_ser_reintentable()
    {
        // La marca de «en vuelo» se suelta pase lo que pase. Un envío que se queda marcado no lo
        // reintenta nadie nunca más — y el síntoma sería un `Queued` eterno, el mismo que esto
        // viene a cerrar.
        var ctx = Nuevo();
        var envio = await Colgado(ctx);

        await ctx.Svc.RetryAsync(envio.Id);           // falla, sigue Queued
        var otra = await ctx.Svc.RetryAsync(envio.Id);

        Assert.DoesNotContain("retry_in_flight", otra.Rejection!.Code, StringComparison.Ordinal);
    }

    // ── Rendirse es un estado, no un silencio ───────────────────────────────

    [Fact]
    public async Task Rendirse_deja_la_causa_a_la_vista()
    {
        var ctx = Nuevo();
        var envio = await Colgado(ctx);

        var r = ctx.Svc.GiveUp(envio.Id, "8 intentos sin salir. Última causa: timeout");

        Assert.Equal(DeliveryStatus.GivenUp, r.Value.Status);
        Assert.Contains("8 intentos", r.Value.LastError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Lo_ABANDONADO_sale_del_barrido_y_no_se_reintenta()
    {
        // Es la mitad del punto: si siguiera en la lista, el barrido volvería por él cada minuto
        // para siempre y «rendirse» no querría decir nada.
        var ctx = Nuevo();
        var envio = await Colgado(ctx);

        ctx.Svc.GiveUp(envio.Id, "se acabó");

        Assert.Empty(ctx.Svc.ListQueued(0, 50).Value.Items);
        Assert.False((await ctx.Svc.RetryAsync(envio.Id)).IsOk);
    }

    [Fact]
    public async Task Rendirse_dos_veces_es_lo_mismo_que_una()
    {
        // El barrido puede repetir la orden: si la respuesta de la primera se perdió, vuelve a
        // verlo en la lista... salvo que no, porque ya no está. Aun así se pide idempotente para
        // que una orden repetida no invente un rechazo que quien barre tendría que interpretar.
        var ctx = Nuevo();
        var envio = await Colgado(ctx);

        ctx.Svc.GiveUp(envio.Id, "primera");
        var segunda = ctx.Svc.GiveUp(envio.Id, "segunda");

        Assert.True(segunda.IsOk);
        Assert.Equal("primera", segunda.Value.LastError);   // no se pisa la causa original
    }

    [Fact]
    public async Task No_se_abandona_lo_que_ya_salio()
    {
        // Marcar como perdido un correo que la persona recibió es mentir en el rastro, que es el
        // defecto que esta capacidad ya tuvo una vez con `Sent`.
        var ctx = Nuevo();
        var r = await ctx.Svc.SendAsync(Ana, Correo, "cita.recordatorio", null, IdempotencyKey.Of("ok"));

        Assert.False(ctx.Svc.GiveUp(r.Value.Id, "porque sí").IsOk);
    }
}
