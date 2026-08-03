using Synergos.Api.Cart.Storage;
using Synergos.Core;
using Synergos.Shared;
using Modelo = Synergos.Api.Cart.Domain;

namespace Synergos.CMS.Tests.Api;

/// <summary>
/// Cubre <see cref="Modelo.CartService"/> — la composición de reglas, almacén y vencimiento.
/// </summary>
public sealed class CartServiceTests
{
    private sealed class RelojFalso : TimeProvider
    {
        private DateTimeOffset _now;
        public RelojFalso(DateTimeOffset inicio) => _now = inicio;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Avanzar(TimeSpan d) => _now += d;
    }

    private sealed class MemoriaStore : ICartStore, IIdempotencyLedger
    {
        private readonly Dictionary<string, Modelo.Cart> _carts = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _keys = new(StringComparer.Ordinal);

        public Modelo.Cart? Find(string id) => _carts.GetValueOrDefault(id);
        public IReadOnlyList<Modelo.Cart> ForOwner(Ref owner) => _carts.Values.Where(c => c.Owner == owner).ToList();
        public void Put(Modelo.Cart cart) => _carts[cart.Id] = cart;

        public string? Find(string scope, IdempotencyKey key) => _keys.GetValueOrDefault($"{scope}|{key.Value}");
        public void Remember(string scope, IdempotencyKey key, string resultId) => _keys[$"{scope}|{key.Value}"] = resultId;

        public int Cuantas => _carts.Count;
    }

    private static readonly DateTimeOffset Ahora = new(2026, 3, 2, 10, 0, 0, TimeSpan.Zero);
    private static readonly Ref Duenio = Ref.Create("identity.member", "m-1");
    private static Ref Sku(string id) => Ref.Create("shop.producto", id);

    private static (Modelo.CartService Svc, MemoriaStore Store, RelojFalso Reloj) Nuevo()
    {
        var store = new MemoriaStore();
        var reloj = new RelojFalso(Ahora);
        return (new Modelo.CartService(store, store, reloj), store, reloj);
    }

    private static IdempotencyKey Llave(string s) => IdempotencyKey.Of(s);

    private static Modelo.Cart Abierta(Modelo.CartService svc, string llave = "c1", TimeSpan? ttl = null)
        => svc.Open(Duenio, ttl, Llave(llave)).Value;

    [Fact]
    public void Poner_la_misma_linea_dos_veces_REEMPLAZA_y_no_suma()
    {
        // Es lo que hace que un reintento tras un timeout no duplique. Con semántica de suma, el
        // cliente termina con seis unidades de algo que pidió dos veces.
        var (svc, _, _) = Nuevo();
        var carrito = Abierta(svc);

        svc.SetLine(carrito.Id, Sku("a"), 2);
        svc.SetLine(carrito.Id, Sku("a"), 2);
        var final = svc.SetLine(carrito.Id, Sku("a"), 3);

        Assert.Single(final.Value.Lines);
        Assert.Equal(3, final.Value.TotalUnits);
    }

    [Fact]
    public void Lineas_distintas_conviven()
    {
        var (svc, _, _) = Nuevo();
        var carrito = Abierta(svc);

        svc.SetLine(carrito.Id, Sku("a"), 2);
        var final = svc.SetLine(carrito.Id, Sku("b"), 1);

        Assert.Equal(2, final.Value.Lines.Count);
        Assert.Equal(3, final.Value.TotalUnits);
    }

    [Fact]
    public void Reintentar_ABRIR_con_la_misma_llave_no_crea_otra_canasta()
    {
        var (svc, store, _) = Nuevo();

        var a = svc.Open(Duenio, null, Llave("misma"));
        var b = svc.Open(Duenio, null, Llave("misma"));

        Assert.Equal(a.Value.Id, b.Value.Id);
        Assert.Equal(1, store.Cuantas);
    }

    [Fact]
    public void Quitar_una_linea_que_NO_esta_no_es_un_error()
    {
        // Es el resultado que el llamador quería. Rechazarlo obligaría a cada cliente a
        // distinguir "no pude" de "ya no estaba".
        var (svc, _, _) = Nuevo();
        var carrito = Abierta(svc);

        var r = svc.RemoveLine(carrito.Id, Sku("fantasma"));

        Assert.True(r.IsOk);
        Assert.Empty(r.Value.Lines);
    }

    [Fact]
    public void Una_canasta_VENCIDA_no_admite_cambios()
    {
        var (svc, _, reloj) = Nuevo();
        var carrito = Abierta(svc, ttl: TimeSpan.FromHours(1));
        svc.SetLine(carrito.Id, Sku("a"), 1);

        reloj.Avanzar(TimeSpan.FromHours(2));

        Assert.Equal("cart.expired", svc.SetLine(carrito.Id, Sku("b"), 1).Rejection!.Code);
        Assert.Equal("cart.expired", svc.RemoveLine(carrito.Id, Sku("a")).Rejection!.Code);
        Assert.Equal("cart.expired", svc.CheckOut(carrito.Id).Rejection!.Code);
    }

    [Fact]
    public void Una_canasta_vencida_TODAVIA_se_puede_leer()
    {
        // Vencer no borra: quien vuelve al día siguiente tiene que poder ver qué llevaba para
        // rearmarla, aunque ya no pueda tocarla.
        var (svc, _, reloj) = Nuevo();
        var carrito = Abierta(svc, ttl: TimeSpan.FromHours(1));
        svc.SetLine(carrito.Id, Sku("a"), 2);
        reloj.Avanzar(TimeSpan.FromHours(2));

        var leida = svc.Get(carrito.Id);

        Assert.True(leida.IsOk);
        Assert.Equal(2, leida.Value.TotalUnits);
        Assert.False(leida.Value.IsOpen(Ahora.AddHours(2)));
    }

    [Fact]
    public void Cerrar_una_canasta_VACIA_se_rechaza()
    {
        var (svc, _, _) = Nuevo();
        var carrito = Abierta(svc);

        Assert.Equal("cart.empty_cart", svc.CheckOut(carrito.Id).Rejection!.Code);
    }

    [Fact]
    public void Tras_CERRAR_la_canasta_queda_marcada_y_no_admite_cambios()
    {
        var (svc, _, _) = Nuevo();
        var carrito = Abierta(svc);
        svc.SetLine(carrito.Id, Sku("a"), 1);

        var cerrada = svc.CheckOut(carrito.Id);

        Assert.True(cerrada.Value.CheckedOut);
        Assert.Equal("cart.already_checked_out", svc.SetLine(carrito.Id, Sku("b"), 1).Rejection!.Code);
        Assert.Equal("cart.already_checked_out", svc.CheckOut(carrito.Id).Rejection!.Code);
    }

    [Fact]
    public void Cerrar_NO_borra_lo_que_llevaba()
    {
        // "Nunca existió" y "se convirtió en el pedido X" son cosas distintas cuando alguien
        // reclama que compró y no le llegó.
        var (svc, _, _) = Nuevo();
        var carrito = Abierta(svc);
        svc.SetLine(carrito.Id, Sku("a"), 2);
        svc.CheckOut(carrito.Id);

        var leida = svc.Get(carrito.Id);

        Assert.Equal(2, leida.Value.TotalUnits);
    }

    [Fact]
    public void Una_canasta_LLENA_todavia_deja_corregir_una_linea_que_ya_tiene()
    {
        // Si el tope se aplicara también al reemplazo, una canasta llena quedaría congelada: no
        // se podría ni corregir una cantidad.
        var (svc, _, _) = Nuevo();
        var carrito = Abierta(svc);
        for (var i = 0; i < Modelo.CartRules.MaxLines; i++) svc.SetLine(carrito.Id, Sku($"s{i}"), 1);

        Assert.True(svc.SetLine(carrito.Id, Sku("s0"), 9).IsOk);
        Assert.Equal("cart.too_many_lines", svc.SetLine(carrito.Id, Sku("nueva"), 1).Rejection!.Code);
    }

    [Fact]
    public void Un_TTL_fuera_de_rango_no_abre_canasta()
    {
        var (svc, _, _) = Nuevo();

        Assert.Equal("cart.bad_ttl", svc.Open(Duenio, TimeSpan.Zero, Llave("k1")).Rejection!.Code);
        Assert.Equal("cart.bad_ttl", svc.Open(Duenio, Modelo.CartRules.MaxTtl + TimeSpan.FromDays(1), Llave("k2")).Rejection!.Code);
    }

    [Fact]
    public void Listar_SIN_dueno_se_rechaza()
    {
        var (svc, _, _) = Nuevo();

        Assert.Equal("cart.owner_required", svc.ListForOwner(null, 0, 10).Rejection!.Code);
    }

    [Fact]
    public void Las_canastas_de_OTRO_dueno_no_salen()
    {
        var (svc, _, _) = Nuevo();
        Abierta(svc, "c1");
        svc.Open(Ref.Create("identity.member", "m-2"), null, Llave("c2"));

        var mias = svc.ListForOwner(Duenio, 0, 10);

        Assert.Single(mias.Value.Items);
        Assert.Equal(Duenio, mias.Value.Items[0].Owner);
    }
}
