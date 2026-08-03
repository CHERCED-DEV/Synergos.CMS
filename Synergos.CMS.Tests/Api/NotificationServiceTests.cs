using Synergos.Api.Notifications.Domain;
using Synergos.Api.Notifications.Storage;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.CMS.Tests.Api;

/// <summary>
/// Cubre <see cref="NotificationService"/> — la composición de plantillas, tope y transporte.
/// </summary>
/// <remarks>
/// Acá el defecto que más duele no es un rechazo mal puesto: es <b>mandarle dos veces el mismo
/// correo a una persona</b>. Por eso el transporte de prueba cuenta sus envíos — es la única
/// forma de que un test pueda ver la diferencia entre "se registró una vez" y "salió una vez".
/// </remarks>
public sealed class NotificationServiceTests
{
    private sealed class RelojFalso : TimeProvider
    {
        private DateTimeOffset _now;
        public RelojFalso(DateTimeOffset inicio) => _now = inicio;
        public override DateTimeOffset GetUtcNow() => _now;
        public async Task Avanzar(TimeSpan d) => _now += d;
    }

    /// <summary>Transporte que cuenta lo que sale y puede fallar a pedido.</summary>
    private sealed class TransporteEspia : INotificationSender
    {
        public List<(Channel Canal, string Direccion, string Asunto, string Cuerpo, string Llave)> Enviados { get; } = new();

        /// <summary>Si viene, se rechaza con esto en vez de aceptar.</summary>
        public Rejection? Falla { get; set; }

        /// <summary>Canales que este transporte de mentira dice saber hablar.</summary>
        public HashSet<Channel> Canales { get; } = new() { Channel.Email, Channel.Sms, Channel.Push };

        public int Contador;

        public bool Supports(Channel channel) => Canales.Contains(channel);

        public Task<Result<string>> SendAsync(
            Channel channel, string address, string subject, string body,
            string idempotencyKey, CancellationToken ct = default)
        {
            Enviados.Add((channel, address, subject, body, idempotencyKey));
            return Task.FromResult(Falla is null
                ? Result.Ok($"prov-{++Contador}")
                : Result.Rejected<string>(Falla));
        }
    }

    private sealed class MemoriaStore : ITemplateStore, IDeliveryStore, IIdempotencyLedger
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
        public void Put(Delivery delivery) => _d[delivery.Id] = delivery;

        public string? Find(string scope, IdempotencyKey key) => _k.GetValueOrDefault($"{scope}|{key.Value}");
        public void Remember(string scope, IdempotencyKey key, string resultId) => _k[$"{scope}|{key.Value}"] = resultId;

        public int Envios => _d.Count;
    }

    private static readonly DateTimeOffset Ahora = new(2026, 3, 2, 10, 0, 0, TimeSpan.Zero);
    private static readonly Ref Ana = Ref.Create("identity.member", "m-1");
    private const string Correo = "ana@ejemplo.co";

    private sealed record Contexto(NotificationService Svc, MemoriaStore Store, TransporteEspia Transporte, RelojFalso Reloj);

    private static Contexto Nuevo()
    {
        var store = new MemoriaStore();
        var transporte = new TransporteEspia();
        var reloj = new RelojFalso(Ahora);
        return new Contexto(new NotificationService(store, store, transporte, store, reloj), store, transporte, reloj);
    }

    private static IdempotencyKey Llave(string s) => IdempotencyKey.Of(s);

    private static void ConPlantilla(NotificationService svc, string key = "cita.recordatorio",
        string asunto = "Tu cita del {fecha}", string cuerpo = "Hola {nombre}, te esperamos el {fecha}.")
        => svc.SaveTemplate(key, Channel.Email, asunto, cuerpo, Llave($"t-{key}"));

    private static Dictionary<string, string> Datos => new() { ["nombre"] = "Ana", ["fecha"] = "5 de marzo" };

    [Fact]
    public async Task Reintentar_con_la_misma_llave_NO_manda_el_correo_dos_veces()
    {
        // El defecto que más duele de esta capacidad: al reintento tras un timeout, la persona
        // le llega un segundo correo idéntico. Contar los envíos del transporte es la única
        // forma de distinguir "se registró una vez" de "salió una vez".
        var (svc, store, transporte, _) = Nuevo();
        ConPlantilla(svc);

        var a = await svc.SendAsync(Ana, Correo, "cita.recordatorio", Datos, Llave("misma"));
        var b = await svc.SendAsync(Ana, Correo, "cita.recordatorio", Datos, Llave("misma"));

        Assert.Equal(a.Value.Id, b.Value.Id);
        Assert.Single(transporte.Enviados);
        Assert.Equal(1, store.Envios);
    }

    [Fact]
    public async Task Los_marcadores_se_rellenan_en_asunto_Y_cuerpo()
    {
        var (svc, _, transporte, _) = Nuevo();
        ConPlantilla(svc);

        await svc.SendAsync(Ana, Correo, "cita.recordatorio", Datos, Llave("k1"));

        Assert.Equal("Tu cita del 5 de marzo", transporte.Enviados[0].Asunto);
        Assert.Equal("Hola Ana, te esperamos el 5 de marzo.", transporte.Enviados[0].Cuerpo);
    }

    [Fact]
    public async Task Un_marcador_SIN_valor_no_manda_nada()
    {
        // Rellenar a medias mandaría "Hola {nombre}" o "Hola ,". Lo que importa acá es que el
        // rechazo ocurre ANTES del transporte: nada sale, y no queda un envío registrado.
        var (svc, store, transporte, _) = Nuevo();
        ConPlantilla(svc);

        var bad = await svc.SendAsync(Ana, Correo, "cita.recordatorio", new Dictionary<string, string> { ["nombre"] = "Ana" }, Llave("k1"));

        Assert.Equal("notifications.missing_placeholder", bad.Rejection!.Code);
        Assert.Empty(transporte.Enviados);
        Assert.Equal(0, store.Envios);
    }

    [Fact]
    public async Task Un_fallo_DEFINITIVO_del_transporte_deja_rastro_y_se_propaga()
    {
        // Antes esto devolvía Ok con estado Failed: el llamador no se enteraba de que su aviso no
        // había salido, y un orquestador seguía adelante como si nada. Ahora el rastro queda —la
        // peor combinación sigue siendo "la persona no recibió nada y el sistema no lo sabe"—
        // pero además se propaga, porque quien pidió el aviso tiene derecho a saberlo.
        var (svc, _, transporte, _) = Nuevo();
        ConPlantilla(svc);
        transporte.Falla = NotificationRules.TransportRejected("dirección inexistente");

        var r = await svc.SendAsync(Ana, Correo, "cita.recordatorio", Datos, Llave("k1"));

        Assert.False(r.IsOk);
        Assert.Equal("notifications.transport_rejected", r.Rejection!.Code);
        Assert.False(r.Rejection.IsTransient);
        Assert.Equal(DeliveryStatus.Failed, svc.GetDelivery(transporte.Enviados[0].Llave).Value.Status);
    }

    [Fact]
    public async Task El_tope_de_frecuencia_corta_y_se_LIBERA_al_pasar_la_ventana()
    {
        // Sin tope, un lazo con un fallo manda mil correos. Pero un tope que no se libera deja a
        // la persona sin avisos para siempre — y eso también es un defecto.
        var (svc, _, _, reloj) = Nuevo();
        ConPlantilla(svc);
        for (var i = 0; i < NotificationRules.MaxPerRecipient; i++)
        {
            await svc.SendAsync(Ana, Correo, "cita.recordatorio", Datos, Llave($"k{i}"));
        }

        var cortado = await svc.SendAsync(Ana, Correo, "cita.recordatorio", Datos, Llave("uno-mas"));
        reloj.Avanzar(NotificationRules.RateWindow + TimeSpan.FromMinutes(1));
        var despues = await svc.SendAsync(Ana, Correo, "cita.recordatorio", Datos, Llave("tras-la-ventana"));

        Assert.Equal("notifications.rate_limited", cortado.Rejection!.Code);
        Assert.True(despues.IsOk, $"la ventana no liberó: {despues.Rejection}");
    }

    [Fact]
    public async Task El_tope_es_POR_DESTINATARIO()
    {
        // Si fuera global, un envío masivo dejaría sin avisos a todo el mundo.
        var (svc, _, _, _) = Nuevo();
        ConPlantilla(svc);
        for (var i = 0; i < NotificationRules.MaxPerRecipient; i++)
        {
            await svc.SendAsync(Ana, Correo, "cita.recordatorio", Datos, Llave($"k{i}"));
        }

        var otra = await svc.SendAsync(Ref.Create("identity.member", "m-2"), "otro@ejemplo.co", "cita.recordatorio", Datos, Llave("otro"));

        Assert.True(otra.IsOk);
    }

    [Fact]
    public async Task Una_plantilla_INEXISTENTE_da_NotFound_y_no_manda_nada()
    {
        var (svc, _, transporte, _) = Nuevo();

        var bad = await svc.SendAsync(Ana, Correo, "no.existe", Datos, Llave("k1"));

        Assert.Equal(RejectionKind.NotFound, bad.Rejection!.Kind);
        Assert.Empty(transporte.Enviados);
    }

    [Fact]
    public async Task Una_direccion_del_canal_equivocado_no_manda_nada()
    {
        var (svc, _, transporte, _) = Nuevo();
        ConPlantilla(svc);

        var bad = await svc.SendAsync(Ana, "3001234567", "cita.recordatorio", Datos, Llave("k1"));

        Assert.Equal("notifications.address_channel_mismatch", bad.Rejection!.Code);
        Assert.Empty(transporte.Enviados);
    }

    [Fact]
    public async Task Dos_plantillas_con_la_misma_clave_no_conviven()
    {
        // Con dos, pedir 'cita.recordatorio' devolvería una u otra según el orden del almacén —
        // y el texto que le llega a la persona cambiaría sin que nadie tocara nada.
        var (svc, _, _, _) = Nuevo();
        ConPlantilla(svc);

        var repetida = svc.SaveTemplate("cita.recordatorio", Channel.Sms, "otro", "otro", Llave("t2"));

        Assert.Equal("notifications.key_taken", repetida.Rejection!.Code);
    }

    [Fact]
    public async Task Reintentar_guardar_una_plantilla_devuelve_la_misma()
    {
        var (svc, _, _, _) = Nuevo();

        var a = svc.SaveTemplate("x", Channel.Email, "a", "b", Llave("misma"));
        var b = svc.SaveTemplate("x", Channel.Email, "a", "b", Llave("misma"));

        Assert.Equal(a.Value.Id, b.Value.Id);
    }

    [Fact]
    public async Task Listar_envios_SIN_destinatario_se_rechaza()
    {
        // Sin filtro sería un volcado del rastro de avisos de todo el mundo.
        var (svc, _, _, _) = Nuevo();

        Assert.Equal("notifications.recipient_required", svc.ListDeliveries(null, 0, 10).Rejection!.Code);
    }

    [Fact]
    public async Task Lo_mas_RECIENTE_sale_primero_en_el_rastro()
    {
        var (svc, _, _, reloj) = Nuevo();
        ConPlantilla(svc, "simple", "Aviso {n}", "Cuerpo");
        foreach (var n in new[] { "una", "dos", "tres" })
        {
            await svc.SendAsync(Ana, Correo, "simple", new Dictionary<string, string> { ["n"] = n }, Llave(n));
            reloj.Avanzar(TimeSpan.FromMinutes(1));
        }

        var rastro = svc.ListDeliveries(Ana, 0, 10);

        Assert.Equal(new[] { "Aviso tres", "Aviso dos", "Aviso una" }, rastro.Value.Items.Select(d => d.Subject));
    }
}
