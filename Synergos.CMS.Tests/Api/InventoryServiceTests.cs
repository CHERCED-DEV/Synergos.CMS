using Synergos.Api.Inventory.Storage;
using Synergos.Core;
using Synergos.Shared;
using Modelo = Synergos.Api.Inventory.Domain;

namespace Synergos.CMS.Tests.Api;

/// <summary>
/// Cubre <see cref="Modelo.InventoryService"/> — y sobre todo el ajuste, que es donde vivía el
/// defecto #30.
/// </summary>
/// <remarks>
/// <para><b>Este fichero no existía.</b> `Api.Inventory` tenía tests de reglas
/// (<c>RestantesRulesTests</c>) y ninguno del servicio, así que nada probaba la composición de
/// regla + almacén — que es donde ocurre el ajuste. Y los que había eran secuenciales: reservar,
/// liberar, comprobar. <b>En secuencia el leer-sumar-escribir es correcto</b>, así que los tests
/// codificaban la misma suposición que el código: que un ajuste ocurre solo.</para>
///
/// <para>Los de concurrencia de acá abajo corren 100 rondas a propósito. Un defecto de carrera
/// que falla una vez de cada veinte pasa lo suficiente como para que alguien lo marque flaky y lo
/// desactive, que es peor que no tenerlo.</para>
/// </remarks>
public sealed class InventoryServiceTests
{
    private const int Rondas = 100;

    private sealed class RelojFalso : TimeProvider
    {
        private DateTimeOffset _now;
        public RelojFalso(DateTimeOffset inicio) => _now = inicio;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Avanzar(TimeSpan d) => _now += d;
    }

    /// <summary>
    /// El almacén de mentira. Un <see cref="Dictionary{TKey,TValue}"/> pelado basta porque
    /// TODO acceso pasa por el cerrojo del servicio — y si algún día dejara de pasar, este
    /// diccionario reventaría bajo los tests de concurrencia, que es exactamente lo que se
    /// querría que hiciera.
    /// </summary>
    /// <remarks>
    /// <para><b><see cref="DemoraEnLeer"/> es lo que hace que los tests de carrera detecten de
    /// verdad.</b> Sin ella no detectan: se comprobó mutando el servicio para que sumara sobre
    /// una lectura tomada fuera del cerrojo —el defecto #30 exacto— y de tres corridas <b>una
    /// pasó entera</b>. La ventana entre leer y escribir es de nanosegundos, así que dos hilos
    /// casi nunca la pisan y el test queda a merced del planificador.</para>
    ///
    /// <para>Un test de carrera que falla a veces es <i>peor que no tenerlo</i>: pasa lo bastante
    /// como para que alguien lo marque flaky y lo desactive, y el defecto se queda. Retrasar la
    /// lectura ensancha esa ventana hasta que el solapamiento deja de ser suerte.</para>
    ///
    /// <para>Y no penaliza al código correcto: si las lecturas ocurren <i>dentro</i> del cerrojo,
    /// la demora simplemente las serializa y el resultado sigue siendo el bueno. La demora sólo
    /// puede producir un descuadre si hay algo que descuadrar.</para>
    /// </remarks>
    private sealed class MemoriaStore : IStockStore, IIdempotencyLedger
    {
        private readonly Dictionary<string, Modelo.StockItem> _items = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _keys = new(StringComparer.Ordinal);

        public TimeSpan DemoraEnLeer { get; set; } = TimeSpan.Zero;

        public Modelo.StockItem? Find(string id)
        {
            if (DemoraEnLeer > TimeSpan.Zero) Thread.Sleep(DemoraEnLeer);
            return _items.GetValueOrDefault(id);
        }
        public Modelo.StockItem? FindBySubject(Ref subject) => _items.Values.FirstOrDefault(i => i.Subject == subject);
        public Modelo.StockItem? FindByHold(string holdId) => _items.Values.FirstOrDefault(i => i.Holds.Any(h => h.Id == holdId));
        public void Put(Modelo.StockItem item) => _items[item.Id] = item;

        public string? Find(string scope, IdempotencyKey key) => _keys.GetValueOrDefault($"{scope}|{key.Value}");
        public void Remember(string scope, IdempotencyKey key, string resultId) => _keys[$"{scope}|{key.Value}"] = resultId;
    }

    private static readonly DateTimeOffset Ahora = new(2026, 8, 4, 10, 0, 0, TimeSpan.Zero);
    private static readonly Ref Producto = Ref.Create("tienda.producto", "p-1");
    private static readonly Ref Comprador = Ref.Create("identity.member", "m-1");

    private static IdempotencyKey Llave(string s) => IdempotencyKey.Of(s);

    private static (Modelo.InventoryService Svc, MemoriaStore Store) Nuevo()
    {
        var store = new MemoriaStore();
        return (new Modelo.InventoryService(store, store, new RelojFalso(Ahora)), store);
    }

    private static (Modelo.InventoryService Svc, string Id) ConExistencias(int onHand, string llave = "d1")
    {
        var (svc, _) = Nuevo();
        return (svc, Declarar(svc, onHand, llave));
    }

    private static string Declarar(Modelo.InventoryService svc, int onHand, string llave)
        => svc.Declare(Producto, onHand, Array.Empty<Modelo.StockUnit>(), Llave(llave)).Value.Id;

    /// <summary>
    /// Lanza <paramref name="cuantos"/> escritores de verdad y los suelta a la vez.
    /// </summary>
    /// <remarks>
    /// Con <see cref="Task.Run(Action)"/> los trabajos son tan cortos que el planificador los
    /// puede correr en fila y no solaparse nunca. Hilos dedicados más una barrera garantizan que
    /// los N están dentro de la llamada al mismo tiempo, que es la condición que el defecto
    /// necesita para manifestarse.
    /// </remarks>
    private static void ALaVez(int cuantos, Action<int> quehacer)
    {
        using var salida = new Barrier(cuantos);
        var hilos = Enumerable.Range(0, cuantos)
            .Select(i => new Thread(() => { salida.SignalAndWait(); quehacer(i); }) { IsBackground = true })
            .ToList();

        foreach (var h in hilos) h.Start();
        foreach (var h in hilos) Assert.True(h.Join(TimeSpan.FromSeconds(30)), "un escritor se colgó");
    }

    // ── El defecto #30: dos escritores a la vez ─────────────────────────────

    [Fact]
    public void Dos_devoluciones_a_la_vez_no_pierden_ninguna()
    {
        // LA MUTACIÓN DEL TICKET, cien veces. stock=10, +2 y +3 simultáneos, tiene que dar 15.
        // Con el ajuste absoluto daba 13: los dos leían 10, uno escribía 12 y el otro 13, y la
        // última escritura pisaba a la primera. Dos unidades desaparecidas del inventario sin
        // excepción, sin log, sin nada.
        for (var ronda = 0; ronda < Rondas; ronda++)
        {
            var (svc, store) = Nuevo();
            var id = Declarar(svc, 10, $"d{ronda}");
            store.DemoraEnLeer = TimeSpan.FromMilliseconds(1);

            var deltas = new[] { 2, 3 };
            ALaVez(2, i => svc.AdjustBy(id, deltas[i], Llave($"r{ronda}-{i}")));

            store.DemoraEnLeer = TimeSpan.Zero;
            Assert.Equal(15, svc.Get(id).Value.OnHand);
        }
    }

    [Fact]
    public void Cinco_ajustes_concurrentes_se_aplican_los_cinco()
    {
        // La versión dura del anterior: cuantos más escritores, más se pierde. Con el ajuste
        // absoluto, cinco devoluciones de +1 sobre 0 dejaban 1.
        for (var ronda = 0; ronda < Rondas; ronda++)
        {
            var (svc, store) = Nuevo();
            var id = Declarar(svc, 0, $"d{ronda}");
            store.DemoraEnLeer = TimeSpan.FromMilliseconds(1);

            ALaVez(5, i => svc.AdjustBy(id, 1, Llave($"r{ronda}-{i}")));

            store.DemoraEnLeer = TimeSpan.Zero;
            Assert.Equal(5, svc.Get(id).Value.OnHand);
        }
    }

    [Fact]
    public void El_absoluto_SIGUE_perdiendo_ajustes_y_por_eso_no_es_la_forma_de_devolver()
    {
        // Deja escrito POR QUÉ el arreglo fue cambiar la forma del ajuste y no meter otro
        // cerrojo. El cerrojo del servicio ya estaba, y no servía de nada: el leer-sumar-escribir
        // ocurre FUERA de la capacidad —en el llamador— y ningún cerrojo de acá adentro lo
        // alcanza. `AdjustTo` es correcto para lo suyo («conté y hay 47»), y sigue siendo
        // incorrecto para devolver.
        var (svc, id) = ConExistencias(10);

        // Los dos leen 10 antes de que ninguno escriba: es el llamador quien calcula el total.
        var leidoPorA = svc.Get(id).Value.OnHand;
        var leidoPorB = svc.Get(id).Value.OnHand;
        svc.AdjustTo(id, leidoPorA + 2);
        svc.AdjustTo(id, leidoPorB + 3);

        Assert.Equal(13, svc.Get(id).Value.OnHand);   // 15 sería lo correcto
    }

    // ── La idempotencia, que el relativo vuelve obligatoria ─────────────────

    [Fact]
    public void Un_reintento_del_mismo_ajuste_NO_suma_dos_veces()
    {
        // Sin esto, el arreglo habría cambiado «se pierde un ajuste» por «se aplica de más», que
        // es el mismo descuadre con el signo al revés. Y el motor de sagas reintenta hasta ocho.
        var (svc, id) = ConExistencias(10);

        for (var i = 0; i < 8; i++) svc.AdjustBy(id, 3, Llave("la-misma"));

        Assert.Equal(13, svc.Get(id).Value.OnHand);
    }

    [Fact]
    public void La_llave_se_resuelve_ANTES_de_las_reglas_de_estado()
    {
        // feedback_idempotency_before_state. Al revés, el reintento choca con lo que él mismo
        // creó: el primer intento deja el total justo en lo apartado, y el segundo —al recalcular
        // sobre el nuevo estado— bajaría por debajo y saldría por below_held. El llamador vería
        // un rechazo por un ajuste que SÍ se aplicó.
        var (svc, id) = ConExistencias(10);
        svc.Hold(id, 8, Array.Empty<string>(), Comprador, null, Llave("h1"));

        var primero = svc.AdjustBy(id, -2, Llave("la-misma"));
        var reintento = svc.AdjustBy(id, -2, Llave("la-misma"));

        Assert.True(primero.IsOk);
        Assert.True(reintento.IsOk);
        Assert.Equal(8, reintento.Value.OnHand);
    }

    [Fact]
    public void Llaves_distintas_son_ajustes_distintos()
    {
        var (svc, id) = ConExistencias(10);

        svc.AdjustBy(id, 3, Llave("a"));
        svc.AdjustBy(id, 3, Llave("b"));

        Assert.Equal(16, svc.Get(id).Value.OnHand);
    }

    // ── Lo que el ajuste relativo sigue rechazando ──────────────────────────

    [Fact]
    public void Un_delta_de_cero_se_rechaza()
    {
        // Una llamada que el llamador creyó que hacía algo. Aceptarla en silencio esconde el
        // defecto de arriba hasta que alguien cuadre el inventario a mano.
        var (svc, id) = ConExistencias(10);

        var r = svc.AdjustBy(id, 0, Llave("a"));

        Assert.Equal("inventory.bad_delta", r.Rejection!.Code);
    }

    [Fact]
    public void Un_delta_que_deja_el_total_negativo_se_rechaza()
    {
        var (svc, id) = ConExistencias(3);

        var r = svc.AdjustBy(id, -5, Llave("a"));

        Assert.Equal("inventory.negative_stock", r.Rejection!.Code);
        Assert.Equal(3, svc.Get(id).Value.OnHand);
    }

    [Fact]
    public void Un_delta_que_deja_el_total_por_debajo_de_lo_apartado_se_rechaza()
    {
        // Dejaría apartados que nadie puede cumplir, y el error saldría al entregar en vez de al
        // ajustar.
        var (svc, id) = ConExistencias(10);
        svc.Hold(id, 8, Array.Empty<string>(), Comprador, null, Llave("h1"));

        var r = svc.AdjustBy(id, -5, Llave("a"));

        Assert.Equal("inventory.below_held", r.Rejection!.Code);
        Assert.Equal(10, svc.Get(id).Value.OnHand);
    }

    [Fact]
    public void Un_item_con_unidades_nombradas_no_se_ajusta_por_cantidad()
    {
        // Habría que decir CUÁLES se agregan o quitan, no cuántas.
        var (svc, _) = Nuevo();
        var unidades = new[] { new Modelo.StockUnit("A1", 1, 1), new Modelo.StockUnit("A2", 1, 2) };
        var item = svc.Declare(Producto, 2, unidades, Llave("d1")).Value;

        Assert.Equal("inventory.named_units_fixed", svc.AdjustBy(item.Id, 1, Llave("a")).Rejection!.Code);
        Assert.Equal("inventory.named_units_fixed", svc.AdjustTo(item.Id, 3).Rejection!.Code);
    }

    [Fact]
    public void Ajustar_un_item_que_no_existe_da_not_found()
    {
        var (svc, _) = Nuevo();

        Assert.Equal("inventory.item_not_found", svc.AdjustBy("no-existe", 1, Llave("a")).Rejection!.Code);
        Assert.Equal("inventory.item_not_found", svc.AdjustTo("no-existe", 1).Rejection!.Code);
    }

    // ── El absoluto sigue haciendo lo suyo ──────────────────────────────────

    [Fact]
    public void El_absoluto_fija_el_total_contado()
    {
        // No se va a ninguna parte: un recuento físico dice cuánto HAY, no cuánto cambió, y
        // expresarlo como delta obligaría a quien contó a restar contra una lectura vieja — que
        // es justamente el defecto que se acaba de arreglar, del revés.
        var (svc, id) = ConExistencias(10);

        var r = svc.AdjustTo(id, 47);

        Assert.Equal(47, r.Value.OnHand);
    }
}
