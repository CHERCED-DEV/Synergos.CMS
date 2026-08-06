using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Synergos.Bff.Core;
using Synergos.Bff.Tienda.Domain;
using Synergos.Core;

namespace Synergos.CMS.Tests.Bff;

/// <summary>
/// Qué significa encontrar una llave de idempotencia ya usada (defecto #41).
/// </summary>
/// <remarks>
/// <para><b>El defecto era de significado, no de código.</b> Los dos orquestadores trataban
/// «la llave existe» como «esto ya pasó, devolvé lo de antes». Cierto mientras la compra vive o
/// salió bien; falso cuando se deshizo entera: ahí no queda nada que duplicar y devolver la saga
/// muerta <b>encierra al comprador para siempre</b>, porque la llave se deriva de lo que compra y
/// por lo tanto nunca cambia.</para>
///
/// <para><b>Y las dos salidas obvias fallan</b>, que es lo que hace que esto merezca tests
/// propios: con llave fresca por intento, un reintento tras un timeout compra dos veces; con
/// llave fija, una compra fallida encierra. Los tests de acá cubren <b>las dos</b> a la vez — que
/// se pueda reintentar lo deshecho, y que un reintento sobre lo vivo siga sin duplicar.</para>
///
/// <para>Se prueba contra <see cref="SagaEngine{TSaga}"/> directamente y no a través de un flujo:
/// la regla es del motor, y los dos flujos la comparten justamente para que no haya dos.</para>
/// </remarks>
public sealed class LlaveDeIdempotenciaTests
{
    /// <summary>Un almacén en memoria — acá no se prueba durabilidad, se prueba la decisión.</summary>
    private sealed class StoreFalso : ISagaStore<PurchaseSaga>
    {
        private readonly Dictionary<string, PurchaseSaga> _sagas = new(StringComparer.Ordinal);

        public PurchaseSaga? Find(string id) => _sagas.TryGetValue(id, out var s) ? s : null;
        public void Put(PurchaseSaga saga) => _sagas[saga.Id] = saga;
        public IReadOnlyList<PurchaseSaga> WithPendingCompensations() => Array.Empty<PurchaseSaga>();
        public IReadOnlyList<PurchaseSaga> StartedBefore(DateTimeOffset l) => Array.Empty<PurchaseSaga>();
        public int Cuantas => _sagas.Count;
    }

    /// <summary>Nunca se llama: <c>Abrir</c> decide sin deshacer nada.</summary>
    private sealed class NoDeshaceNada : ICompensationExecutor<PurchaseSaga>
    {
        public Task<Rejection?> UndoAsync(PurchaseSaga s, Compensation c, CancellationToken ct)
            => throw new InvalidOperationException("Abrir() no puede compensar nada.");
    }

    /// <summary>Tampoco: sin avisos que mandar, no hay red que tocar.</summary>
    private sealed class SinRed : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
            => throw new InvalidOperationException("Abrir() no puede salir a la red.");
    }

    private static (SagaEngine<PurchaseSaga> Motor, StoreFalso Store) Nuevo()
    {
        var store = new StoreFalso();
        var vocabulario = new SagaVocabulary("tienda", "la compra");

        // Los dos colaboradores de abajo LANZAN si alguien los usa, a propósito: `Abrir` decide
        // mirando el almacén y nada más. Si mañana empezara a compensar o a avisar por su cuenta,
        // estos tests se caen — que es justo lo que hay que enterarse.
        var motor = new SagaEngine<PurchaseSaga>(
            store,
            new Compensator<PurchaseSaga>(new NoDeshaceNada(), TimeProvider.System,
                NullLogger<Compensator<PurchaseSaga>>.Instance),
            new CompensationAlert(new SinRed(), vocabulario, Options.Create(new AlertOptions())),
            vocabulario,
            TimeProvider.System,
            NullLogger<SagaEngine<PurchaseSaga>>.Instance);
        return (motor, store);
    }

    private static PurchaseSaga Saga(string id, SagaStatus estado) => new(
        id, Ref.Create("tienda.comprador", "c1"), "cart1", estado,
        Array.Empty<StockHold>(), null, null, null, Money.Of(1000, "COP"),
        Array.Empty<Compensation>(), null, DateTimeOffset.UnixEpoch);

    // ── El defecto ──────────────────────────────────────────────────────────

    [Fact]
    public void Tras_una_compra_DESHECHA_la_misma_llave_arranca_una_nueva()
    {
        // EL DEFECTO #41. Le rechazan la tarjeta, la saga compensa —el cupo volvió, el cobro se
        // liberó, no se emitió nada— y el comprador arregla la tarjeta y lo intenta otra vez.
        // Antes recibía la saga muerta, y la recibiría siempre: la llave se deriva de lo que
        // compra, así que nunca cambia.
        var (motor, _) = Nuevo();
        motor.Put(Saga("k1", SagaStatus.Compensated));

        var slot = motor.Abrir("k1");

        Assert.Null(slot.Reusar);
        Assert.NotEqual("k1", slot.Id);
    }

    [Fact]
    public void La_saga_muerta_NO_se_sobrescribe()
    {
        // Sobrescribirla sería peor que el defecto: se perdería qué falló y —lo grave— las
        // compensaciones que el barrido todavía pudiera estar reintentando.
        var (motor, store) = Nuevo();
        motor.Put(Saga("k1", SagaStatus.Compensated));

        var slot = motor.Abrir("k1");
        motor.Put(Saga(slot.Id, SagaStatus.Running));

        Assert.Equal(2, store.Cuantas);
        Assert.Equal(SagaStatus.Compensated, motor.Find("k1")!.Status);
    }

    // ── Lo que NO se puede perder al arreglarlo ─────────────────────────────

    [Theory]
    [InlineData(SagaStatus.Running)]        // hay cupo apartado y plata retenida
    [InlineData(SagaStatus.Compensating)]   // todavía se está devolviendo
    [InlineData(SagaStatus.Completed)]      // salió bien: dos clics no son dos compras
    public void Sobre_una_saga_VIVA_la_misma_llave_sigue_devolviendo_ESA(SagaStatus estado)
    {
        // La propiedad que hacía correcta la llave derivada del carrito, y que arreglar el
        // defecto no puede romper: un reintento por timeout NO compra dos veces.
        var (motor, _) = Nuevo();
        motor.Put(Saga("k1", estado));

        var slot = motor.Abrir("k1");

        Assert.NotNull(slot.Reusar);
        Assert.Equal("k1", slot.Reusar!.Id);
    }

    [Fact]
    public void Una_compensacion_que_FALLO_no_deja_reintentar()
    {
        // `CompensationFailed` significa que algo quedó colgado y necesita una persona. Dejar
        // comprar otra vez esconde ese estado detrás de una compra nueva, y el cupo que no se
        // pudo devolver se pierde sin que nadie lo mire.
        var (motor, _) = Nuevo();
        motor.Put(Saga("k1", SagaStatus.CompensationFailed));

        var slot = motor.Abrir("k1");

        Assert.NotNull(slot.Reusar);
        Assert.Equal(SagaStatus.CompensationFailed, slot.Reusar!.Status);
    }

    [Fact]
    public void El_reintento_del_reintento_tampoco_duplica()
    {
        // La propiedad más sutil: el identificador del segundo intento es DETERMINISTA. Si el
        // segundo intento se cae por timeout y el comprador reintenta, tiene que caer en la misma
        // saga y no abrir una tercera. Con un Guid nuevo por intento, acá habría dos compras.
        var (motor, store) = Nuevo();
        motor.Put(Saga("k1", SagaStatus.Compensated));

        var segundo = motor.Abrir("k1");
        motor.Put(Saga(segundo.Id, SagaStatus.Running));

        var reintento = motor.Abrir("k1");

        Assert.NotNull(reintento.Reusar);
        Assert.Equal(segundo.Id, reintento.Reusar!.Id);
        Assert.Equal(2, store.Cuantas);
    }

    [Fact]
    public void Tres_compras_deshechas_seguidas_abren_tres_sagas_distintas()
    {
        // Y el tercer intento se busca desde la RAÍZ de la llave, no desde el id del segundo: sin
        // eso, `k1#2#2` crecería sin fin y el cuarto intento no encontraría a los anteriores.
        var (motor, store) = Nuevo();
        motor.Put(Saga("k1", SagaStatus.Compensated));

        var s2 = motor.Abrir("k1");
        motor.Put(Saga(s2.Id, SagaStatus.Compensated));

        var s3 = motor.Abrir("k1");
        motor.Put(Saga(s3.Id, SagaStatus.Running));

        Assert.Equal(3, store.Cuantas);
        Assert.NotEqual(s2.Id, s3.Id);
    }

    [Fact]
    public void Una_llave_que_YA_trae_sufijo_se_busca_desde_su_raiz()
    {
        // Lo destapó mutar el gate: el test de arriba llamaba siempre con la llave raíz, así que
        // `Raiz()` no se ejercitaba y romperlo no ponía nada en rojo. Un mutante que sobrevive
        // señala código sin probar, no un test de más.
        //
        // Y lo que protege es real: el identificador de una saga SÍ puede volver con sufijo —el
        // CMS lo guarda como referencia del pedido y lo usa para confirmar—, así que basta con
        // que alguien lo pase de vuelta acá para que, sin raíz, se busque `k1#2#2`, luego
        // `k1#2#2#2`, y el identificador crezca sin fin sin encontrar nunca a sus hermanos.
        var (motor, _) = Nuevo();
        motor.Put(Saga("k1", SagaStatus.Compensated));
        motor.Put(Saga("k1#2", SagaStatus.Compensated));

        var slot = motor.Abrir("k1#2");

        Assert.Null(slot.Reusar);
        Assert.Equal("k1#3", slot.Id);
    }

    [Fact]
    public void Una_llave_nunca_usada_se_usa_tal_cual()
    {
        // El camino normal, y el que hace que los identificadores sean legibles: sin sufijo hasta
        // que haga falta uno.
        var (motor, _) = Nuevo();

        var slot = motor.Abrir("k1");

        Assert.Null(slot.Reusar);
        Assert.Equal("k1", slot.Id);
    }
}
