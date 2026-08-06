using Microsoft.Extensions.Logging.Abstractions;
using Synergos.Api.Payments.Storage;
using Synergos.Core;
using Synergos.Shared;
using Pagos = Synergos.Api.Payments.Domain;

namespace Synergos.CMS.Tests.Api;

/// <summary>
/// Lo que el medio de pago contesta, y qué se hace con cada respuesta (HU #27).
/// </summary>
/// <remarks>
/// <para><b>El defecto que esto cierra no era «no cobra»: era «no se puede saber por qué no
/// cobró».</b> La costura devolvía <c>bool</c>, así que «el banco dijo que no» y «la pasarela no
/// contestó» eran el mismo valor — y salían los dos como <c>Unavailable</c>, o sea transitorios.
/// Un rechazo firme se reintentaba ocho veces contra una tarjeta que ya había dicho que no.</para>
///
/// <para>Con cuatro respuestas posibles, la decisión de reintentar deja de ser una adivinanza.</para>
/// </remarks>
public sealed class PaymentProviderOutcomeTests
{
    private sealed class RelojFalso : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 8, 4, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
    }

    private sealed class MemoriaStore : IPaymentStore, IIdempotencyLedger
    {
        private readonly Dictionary<string, Pagos.Payment> _p = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _k = new(StringComparer.Ordinal);

        public Pagos.Payment? Find(string id) => _p.GetValueOrDefault(id);
        public IReadOnlyList<Pagos.Payment> ForSubject(Ref forWhat) => _p.Values.Where(x => x.For == forWhat).ToList();
        public void Put(Pagos.Payment payment) => _p[payment.Id] = payment;

        public string? Find(string scope, IdempotencyKey key) => _k.GetValueOrDefault($"{scope}|{key.Value}");
        public void Remember(string scope, IdempotencyKey key, string resultId) => _k[$"{scope}|{key.Value}"] = resultId;
    }

    /// <summary>Un proveedor que contesta lo que el test le diga.</summary>
    private sealed class ProveedorGuionado : Pagos.IPaymentProvider
    {
        private readonly Pagos.PaymentAttempt _respuesta;
        public ProveedorGuionado(Pagos.PaymentAttempt r) => _respuesta = r;

        public string Name => "guionado";
        public bool MuevePlata => true;

        public Pagos.PaymentAttempt Authorize(Money amount, Ref payer) => _respuesta;
        public Pagos.PaymentAttempt Capture(string r, Money a) => _respuesta;
        public Pagos.PaymentAttempt Refund(string r, Money a) => _respuesta;
        public Pagos.PaymentAttempt Void(string r) => _respuesta;
    }

    private static readonly Ref Compra = Ref.Create("tienda.compra", "c-1");
    private static readonly Ref Pagador = Ref.Create("identity.member", "m-1");
    private static Money Cop(decimal x) => Money.Of(x, "COP");
    private static IdempotencyKey Llave(string s) => IdempotencyKey.Of(s);

    private static Pagos.PaymentService Con(Pagos.PaymentAttempt respuesta)
    {
        var store = new MemoriaStore();
        return new Pagos.PaymentService(store, new ProveedorGuionado(respuesta), store, new RelojFalso());
    }

    // ── Las cuatro respuestas ───────────────────────────────────────────────

    [Fact]
    public void Un_rechazo_del_banco_NO_es_transitorio_y_lleva_su_motivo()
    {
        // Es la mitad del defecto: reintentar un rechazo firme no cambia la respuesta, solo
        // molesta al comprador. Y «fondos insuficientes» lleva a una acción; «el pago falló» no.
        var svc = Con(Pagos.PaymentAttempt.Declined("Fondos insuficientes."));

        var r = svc.Authorize(Compra, Pagador, Cop(119000), Llave("a"));

        Assert.Equal("payments.payment_declined", r.Rejection!.Code);
        Assert.False(r.Rejection.IsTransient);
        Assert.Contains("Fondos insuficientes", r.Rejection.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Una_pasarela_caida_SI_es_transitoria()
    {
        // La otra mitad: no se sabe qué pasó, y no saberlo es razón para volver a preguntar.
        var svc = Con(Pagos.PaymentAttempt.Unavailable("La pasarela no respondió."));

        var r = svc.Authorize(Compra, Pagador, Cop(119000), Llave("a"));

        Assert.Equal("payments.payment_provider_unavailable", r.Rejection!.Code);
        Assert.True(r.Rejection.IsTransient);
    }

    [Fact]
    public void Rechazado_y_caido_NO_son_el_mismo_codigo()
    {
        // El corazón de la HU. Con un bool eran indistinguibles, y por eso los dos salían
        // reintentables.
        var declinado = Con(Pagos.PaymentAttempt.Declined("no")).Authorize(Compra, Pagador, Cop(1000), Llave("a"));
        var caido = Con(Pagos.PaymentAttempt.Unavailable("timeout")).Authorize(Compra, Pagador, Cop(1000), Llave("b"));

        Assert.NotEqual(declinado.Rejection!.Code, caido.Rejection!.Code);
        Assert.NotEqual(declinado.Rejection.IsTransient, caido.Rejection.IsTransient);
    }

    [Fact]
    public void Sin_credencial_se_rechaza_y_NO_se_aparenta_cobrar()
    {
        // Un despliegue a medias no puede parecer uno que funciona: eso son pedidos «pagados»
        // que nadie cobró, descubiertos cuando alguien cuadre la caja.
        var svc = Con(Pagos.PaymentAttempt.NotConfigured("falta Payments:wompi:ApiKey"));

        var r = svc.Authorize(Compra, Pagador, Cop(119000), Llave("a"));

        Assert.Equal("payments.transport_not_configured", r.Rejection!.Code);
        Assert.True(r.Rejection.IsTransient);   // la operación NO ocurrió: se puede reintentar
    }

    [Fact]
    public void El_intento_rechazado_QUEDA_registrado()
    {
        // Un rechazo sin rastro deja al cliente diciendo «yo lo intenté» y al sistema sin manera
        // de saber si es cierto.
        var store = new MemoriaStore();
        var svc = new Pagos.PaymentService(
            store, new ProveedorGuionado(Pagos.PaymentAttempt.Declined("no")), store, new RelojFalso());

        svc.Authorize(Compra, Pagador, Cop(1000), Llave("a"));

        var registrados = store.ForSubject(Compra);
        Assert.Single(registrados);
        Assert.Equal(Pagos.PaymentStatus.Failed, registrados[0].Status);
    }

    // ── El proveedor que no está configurado ────────────────────────────────

    [Fact]
    public void NotConfiguredPaymentProvider_rechaza_las_CUATRO_operaciones()
    {
        // Si alguna dijera que sí, el despliegue a medias volvería a parecer uno que funciona
        // — por esa operación.
        var p = new NotConfiguredPaymentProvider(
            "wompi", "Payments:wompi:ApiKey", NullLogger<NotConfiguredPaymentProvider>.Instance);

        Assert.Equal(Pagos.PaymentOutcome.NotConfigured, p.Authorize(Cop(1000), Pagador).Outcome);
        Assert.Equal(Pagos.PaymentOutcome.NotConfigured, p.Capture("r", Cop(1000)).Outcome);
        Assert.Equal(Pagos.PaymentOutcome.NotConfigured, p.Refund("r", Cop(1000)).Outcome);
        Assert.Equal(Pagos.PaymentOutcome.NotConfigured, p.Void("r").Outcome);
        Assert.False(p.MuevePlata);
    }

    [Fact]
    public void Los_proveedores_que_NO_mueven_plata_lo_declaran()
    {
        // Es lo que permite que un gate los eche de producción en vez de confiar en el nombre.
        // El CMS ya tuvo el defecto de `Provider=Wompi` sirviendo el stub en silencio.
        Assert.False(new LoggingPaymentProvider(NullLogger<LoggingPaymentProvider>.Instance).MuevePlata);
        Assert.False(new NotConfiguredPaymentProvider("x", "y", NullLogger<NotConfiguredPaymentProvider>.Instance).MuevePlata);
    }
}
