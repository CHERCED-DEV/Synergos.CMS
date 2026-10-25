using Synergos.Api.Payments.Storage;
using Synergos.Core;
using Synergos.Shared;
using Pagos = Synergos.Api.Payments.Domain;

namespace Synergos.CMS.Tests.Api;

/// <summary>
/// Un fallo pasajero deja la llave EN DUDA, no <c>Failed</c> (#183, decisión 4 del lote 1): el reintento con la misma llave le
/// pregunta al proveedor por el cobro y sólo vuelve a autorizar si no hay rastro. Así <c>transient</c> dice la verdad y no hay
/// doble cobro.
/// </summary>
/// <remarks>
/// <para><b>El caso exacto, con sus dos desenlaces</b>: la pasarela no contesta al autorizar; el reintento pregunta, y o había
/// cobro —se asienta sin autorizar otra vez— o no lo había —se autoriza con la MISMA referencia—.</para>
///
/// <para><b>Antes</b>, el fallo pasajero dejaba <c>Failed</c> y la llave cerrada: el reintento devolvía ese <c>Failed</c> para
/// siempre, aunque la pasarela volviera o el cobro hubiera salido.</para>
/// </remarks>
public sealed class CobroEnDudaTests
{
    private sealed class RelojFalso : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);
    }

    private sealed class MemoriaStore : IPaymentStore, IIdempotencyLedger
    {
        private readonly Dictionary<string, Pagos.Payment> _p = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _k = new(StringComparer.Ordinal);

        public IReadOnlyCollection<Pagos.Payment> Todos => _p.Values;
        public Pagos.Payment? Find(string id) => _p.GetValueOrDefault(id);
        public Pagos.Payment? FindByProviderReference(string providerReference)
            => _p.Values.FirstOrDefault(x => x.ProviderReference == providerReference);
        public IReadOnlyList<Pagos.Payment> ForSubject(Ref forWhat) => _p.Values.Where(x => x.For == forWhat).ToList();
        public void Put(Pagos.Payment payment) => _p[payment.Id] = payment;

        public string? Find(string scope, IdempotencyKey key) => _k.GetValueOrDefault($"{scope}|{key.Value}");
        public void Remember(string scope, IdempotencyKey key, string resultId) => _k[$"{scope}|{key.Value}"] = resultId;
    }

    /// <summary>Un proveedor con guion: lo que contesta cada autorización, en orden, y lo que sabe al buscar.</summary>
    private sealed class Proveedor : Pagos.IPaymentProvider
    {
        private readonly Queue<Func<Pagos.PaymentAttempt>> _autorizar;
        private readonly Func<Pagos.PaymentAttempt?> _buscar;

        public Proveedor(Func<Pagos.PaymentAttempt?> buscar, params Func<Pagos.PaymentAttempt>[] autorizar)
        {
            _buscar = buscar;
            _autorizar = new Queue<Func<Pagos.PaymentAttempt>>(autorizar);
        }

        public List<string> Autorizadas { get; } = [];
        public List<string> Buscadas { get; } = [];
        public string Name => "guionado";
        public bool MuevePlata => true;

        public Task<Pagos.PaymentAttempt> AuthorizeAsync(Money amount, Ref payer, string reference, CancellationToken ct = default)
        {
            Autorizadas.Add(reference);
            return Task.FromResult(_autorizar.Dequeue()());
        }

        public Task<Pagos.PaymentAttempt?> FindAuthorizationAsync(string reference, CancellationToken ct = default)
        {
            Buscadas.Add(reference);
            return Task.FromResult(_buscar());
        }

        public Task<Pagos.PaymentAttempt> CaptureAsync(string r, Money a, CancellationToken ct = default) => Task.FromResult(Pagos.PaymentAttempt.Ok(r));
        public Task<Pagos.PaymentAttempt> RefundAsync(string r, Money a, CancellationToken ct = default) => Task.FromResult(Pagos.PaymentAttempt.Ok(r));
        public Task<Pagos.PaymentAttempt> VoidAsync(string r, CancellationToken ct = default) => Task.FromResult(Pagos.PaymentAttempt.Ok(r));
    }

    private static readonly Ref Compra = Ref.Create("tienda.compra", "c-1");
    private static readonly Ref Pagador = Ref.Create("identity.member", "m-1");
    private static readonly Money Monto = Money.Of(119_000m, "COP");
    private static readonly IdempotencyKey Llave = IdempotencyKey.Of("saga-1|authorize");

    private static Func<Pagos.PaymentAttempt> Caida => () => Pagos.PaymentAttempt.Unavailable("La pasarela no respondió.");
    private static Func<Pagos.PaymentAttempt> Sale(string referencia) => () => Pagos.PaymentAttempt.Ok(referencia);

    private static (Pagos.PaymentService Servicio, MemoriaStore Store, Proveedor Proveedor) Armar(Proveedor proveedor)
    {
        var store = new MemoriaStore();
        return (new Pagos.PaymentService(store, proveedor, store, new RelojFalso()), store, proveedor);
    }

    private static Task<Result<Pagos.Payment>> Autorizar(Pagos.PaymentService servicio)
        => servicio.AuthorizeAsync(Compra, Pagador, Monto, IdentityAssertion.CmsSession, Llave);

    [Fact]
    public async Task Un_fallo_pasajero_deja_el_cobro_en_duda_con_la_llave_anotada_y_el_rechazo_transitorio()
    {
        var (servicio, store, _) = Armar(new Proveedor(() => null, Caida));

        var r = await Autorizar(servicio);

        Assert.Equal("payments.payment_provider_unavailable", r.Rejection!.Code);
        Assert.True(r.Rejection.IsTransient);
        var cobro = Assert.Single(store.Todos);
        Assert.Equal(Pagos.PaymentStatus.InDoubt, cobro.Status);
        Assert.Equal(cobro.Id, store.Find("payment", Llave));
    }

    [Fact]
    public async Task Habia_cobro_el_reintento_pregunta_lo_asienta_y_NO_vuelve_a_autorizar()
    {
        var (servicio, store, proveedor) = Armar(new Proveedor(() => Pagos.PaymentAttempt.Ok("ref-del-primero"), Caida));

        await Autorizar(servicio);
        var reintento = await Autorizar(servicio);

        Assert.True(reintento.IsOk, reintento.Rejection?.Message);
        Assert.Equal(Pagos.PaymentStatus.Authorized, reintento.Value.Status);
        Assert.Equal("ref-del-primero", reintento.Value.ProviderReference);
        Assert.Single(proveedor.Autorizadas);
        Assert.Equal([reintento.Value.Id], proveedor.Buscadas);
        Assert.Single(store.Todos);
    }

    [Fact]
    public async Task No_habia_cobro_el_reintento_pregunta_y_autoriza_con_la_MISMA_referencia()
    {
        var (servicio, store, proveedor) = Armar(new Proveedor(() => null, Caida, Sale("ref-del-segundo")));

        await Autorizar(servicio);
        var reintento = await Autorizar(servicio);

        Assert.True(reintento.IsOk, reintento.Rejection?.Message);
        Assert.Equal(Pagos.PaymentStatus.Authorized, reintento.Value.Status);
        Assert.Equal("ref-del-segundo", reintento.Value.ProviderReference);
        Assert.Equal([reintento.Value.Id, reintento.Value.Id], proveedor.Autorizadas);
        Assert.Equal([reintento.Value.Id], proveedor.Buscadas);
        Assert.Single(store.Todos);
    }

    [Fact]
    public async Task Si_no_se_puede_preguntar_sigue_en_duda_y_no_se_autoriza_a_ciegas()
    {
        var (servicio, store, proveedor) = Armar(new Proveedor(() => Pagos.PaymentAttempt.Unavailable("tampoco ahora"), Caida));

        await Autorizar(servicio);
        var reintento = await Autorizar(servicio);

        Assert.True(reintento.Rejection!.IsTransient);
        Assert.Equal(Pagos.PaymentStatus.InDoubt, Assert.Single(store.Todos).Status);
        Assert.Single(proveedor.Autorizadas);
    }

    [Fact]
    public async Task Si_el_proveedor_lo_tiene_rechazado_queda_Failed_y_el_rechazo_es_firme()
    {
        var (servicio, store, _) = Armar(new Proveedor(() => Pagos.PaymentAttempt.Declined("Fondos insuficientes."), Caida));

        await Autorizar(servicio);
        var reintento = await Autorizar(servicio);

        Assert.Equal("payments.payment_declined", reintento.Rejection!.Code);
        Assert.False(reintento.Rejection.IsTransient);
        Assert.Equal(Pagos.PaymentStatus.Failed, Assert.Single(store.Todos).Status);
    }

    [Fact]
    public async Task Una_caida_entre_la_pasarela_y_el_almacen_deja_la_llave_en_duda_y_no_libre()
    {
        // La llave se anota ANTES de hablar con la pasarela: si el proceso cae a mitad (aquí, la pasarela lanza), el reintento
        // encuentra el cobro en duda y pregunta, en vez de autorizar un segundo cobro con un id nuevo.
        var (servicio, store, proveedor) = Armar(new Proveedor(
            () => Pagos.PaymentAttempt.Ok("ref-que-si-salio"), () => throw new HttpRequestException("se cortó la conexión")));

        await Assert.ThrowsAsync<HttpRequestException>(() => Autorizar(servicio));
        Assert.Equal(Pagos.PaymentStatus.InDoubt, Assert.Single(store.Todos).Status);

        var reintento = await Autorizar(servicio);

        Assert.Equal("ref-que-si-salio", reintento.Value.ProviderReference);
        Assert.Single(proveedor.Autorizadas);
    }

    [Fact]
    public async Task Sin_credencial_la_llave_no_se_cierra_y_puesta_la_credencial_el_reintento_autoriza()
    {
        var (servicio, store, proveedor) = Armar(new Proveedor(
            () => null, () => Pagos.PaymentAttempt.NotConfigured("falta Payments:wompi:ApiKey"), Sale("ref-con-credencial")));

        var sinCredencial = await Autorizar(servicio);
        var conCredencial = await Autorizar(servicio);

        Assert.True(sinCredencial.Rejection!.IsTransient);
        Assert.True(conCredencial.IsOk, conCredencial.Rejection?.Message);
        Assert.Equal(Pagos.PaymentStatus.Authorized, Assert.Single(store.Todos).Status);
        Assert.Equal(2, proveedor.Autorizadas.Count);
    }

    [Fact]
    public async Task Un_cobro_en_duda_no_se_captura_ni_se_libera_y_lo_dice()
    {
        var (servicio, store, _) = Armar(new Proveedor(() => null, Caida));
        await Autorizar(servicio);
        var id = Assert.Single(store.Todos).Id;

        var captura = await servicio.CaptureAsync(id, IdempotencyKey.Of("saga-1|capture"));
        var liberacion = await servicio.VoidAsync(id);

        Assert.Equal("payments.payment_in_doubt", captura.Rejection!.Code);
        Assert.Equal("payments.payment_in_doubt", liberacion.Rejection!.Code);
    }

    [Fact]
    public async Task Lo_que_ya_se_resolvio_se_devuelve_tal_cual_sin_volver_a_preguntar()
    {
        var (servicio, _, proveedor) = Armar(new Proveedor(() => throw new InvalidOperationException("no debía preguntar"), Sale("ref-1")));

        var primera = await Autorizar(servicio);
        var repetida = await Autorizar(servicio);

        Assert.Equal(primera.Value.Id, repetida.Value.Id);
        Assert.Empty(proveedor.Buscadas);
        Assert.Single(proveedor.Autorizadas);
    }
}
