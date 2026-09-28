using System.Security.Cryptography;
using System.Text;
using Synergos.Bff.Core;
using Synergos.Bff.Tienda.Domain;
using Synergos.Core;
using Compensation = Synergos.Bff.Core.Compensation;

namespace Synergos.CMS.Tests.Bff;

/// <summary>
/// El barrido paga por lo que hay VIVO, no por la historia (#130).
/// </summary>
/// <remarks>
/// <para><b>Qué medía el defecto.</b> Desde el #112 hay un fichero por saga, y las dos consultas
/// del barrido —<c>WithPendingCompensations</c> y <c>StartedBefore</c>— filtraban en memoria
/// después de deserializar el directorio entero. Medido con estas mismas clases: 41 ms con 100
/// sagas, 341 ms con 10 000, 1 142 ms con 50 000, lineal a ≈23 µs por saga. Por cuatro
/// orquestadores y cada 60 s, para siempre.</para>
///
/// <para><b>Cómo se prueba sin un reloj, que es la mitad que cuesta.</b> Cronometrar una
/// propiedad temporal contra una máquina es cómo se aprende a ignorar un rojo
/// (<c>feedback_a_clock_test_measures_a_property_it_cannot_own</c>). Así que el discriminador no
/// es cuánto tarda: es un <b>cable trampa</b>. Las sagas terminales del fixture se dejan con un
/// cuerpo que NO se puede deserializar, así que leerlas revienta. Con el índice el barrido no
/// las toca y contesta; sin él, <c>All()</c> las lee y lanza. No afirma que un fichero corrupto
/// sea aceptable —no lo es, y nada cambió sobre eso—: afirma que no se leyó, que es lo único que
/// hacía falta observar.</para>
///
/// <para><b>Y el fixture tiene que llevar las dos clases de terminal.</b> <c>Completed</c> y
/// <c>Compensated</c> salen del índice por caminos distintos —una acaba bien, la otra es la
/// única que <c>Abrir</c> deja reintentar— y con sólo una de las dos un índice que se olvidara
/// de la otra pasaría en verde.</para>
///
/// <para><b>Lo que NO se prueba, y se nombra en vez de insinuarse cubierto: el ORDEN dentro de
/// <c>Put</c>.</b> Marcar antes de escribir y desmarcar después es lo que hace que toda caída a
/// mitad deje una marca de MÁS y nunca una de menos, y esa caída no se puede provocar dentro del
/// proceso sin inyectar un fallo en el almacén — que probaría el doble y no la regla
/// (<c>feedback_a_state_with_no_writer_cannot_be_tested_through_the_seam</c>). Lo que sí se
/// prueba es el RESIDUO que ese orden produce: <c>Una_marca_que_sobra_se_limpia_al_pasar</c>. El
/// hueco queda acá, que es donde lo va a leer quien toque <c>Put</c>.</para>
/// </remarks>
public sealed class IndiceDeSagasVivasTests : IDisposable
{
    private readonly string _raiz = Path.Combine(
        Path.GetTempPath(), "syn-indice-" + Guid.NewGuid().ToString("n"));

    // El almacén REAL y no un doble, por la razón que `AbandonoTests` ya tiene escrita: un doble
    // que reimplemente la consulta bajo prueba afirma sobre su propia copia de la lógica.
    private FileSystemSagaStore<PurchaseSaga> Almacen()
        => new(Microsoft.Extensions.Options.Options.Create(new SagaStorageOptions { Root = _raiz }));

    private string Indice => Path.Combine(_raiz, "sagas-vivas");

    private string Documento(string id)
        => Path.Combine(_raiz, "sagas",
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id))).ToLowerInvariant() + ".json");

    public void Dispose()
    {
        if (Directory.Exists(_raiz))
        {
            try { Directory.Delete(_raiz, recursive: true); } catch (IOException) { /* temporal */ }
        }
    }

    private static readonly DateTimeOffset Ahora = new(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);

    private static PurchaseSaga Compra(string id, SagaStatus estado, DateTimeOffset? empezo = null)
        => new(id, Ref.Create("tienda.comprador", "u-1"), "c-1", estado,
            Array.Empty<StockHold>(), null, null, null, Money.Of(119000, "COP"),
            new[] { Compensation.For(TiendaCompensations.ReleaseStockHold, "sh-" + id, "sin confirmar") },
            null, empezo ?? Ahora.AddHours(-2));

    /// <summary>
    /// Deja el documento de <paramref name="id"/> ilegible. <b>Es un cable trampa</b>: si algo lo
    /// lee, se sabe.
    /// </summary>
    private void Minar(string id) => File.WriteAllText(Documento(id), "esto no es json");

    // ── El barrido no lee la historia ───────────────────────────────────────

    [Fact]
    public void El_barrido_de_compensaciones_no_toca_las_sagas_terminales()
    {
        var store = Almacen();

        for (var i = 0; i < 20; i++)
        {
            var estado = i % 2 == 0 ? SagaStatus.Completed : SagaStatus.Compensated;
            store.Put(Compra($"hecha-{i:00}", estado));
        }

        store.Put(Compra("colgada", SagaStatus.Compensating));

        // Después de guardarlas, para que el índice ya esté escrito con la verdad.
        for (var i = 0; i < 20; i++) Minar($"hecha-{i:00}");

        var pendientes = store.WithPendingCompensations();

        Assert.Single(pendientes);
        Assert.Equal("colgada", pendientes[0].Id);
    }

    [Fact]
    public void El_barrido_del_abandono_tampoco()
    {
        var store = Almacen();

        for (var i = 0; i < 20; i++) store.Put(Compra($"hecha-{i:00}", SagaStatus.Completed));
        store.Put(Compra("olvidada", SagaStatus.Running));
        for (var i = 0; i < 20; i++) Minar($"hecha-{i:00}");

        var alcanzadas = store.StartedBefore(Ahora.AddHours(-1));

        Assert.Single(alcanzadas);
        Assert.Equal("olvidada", alcanzadas[0].Id);
    }

    // ── Lo que NO puede cambiar ─────────────────────────────────────────────

    /// <summary>
    /// <b>El anti-regresión que importa: no se poda nada.</b>
    /// </summary>
    /// <remarks>
    /// El almacén de sagas es también el libro de idempotencia: <c>SagaEngine.Abrir</c> resuelve
    /// la llave con <c>Find</c>, y una saga terminada que dejara de encontrarse haría que el
    /// siguiente reintento abriera una nueva y <b>volviera a cobrar</b> (defecto #41). Es
    /// exactamente el arreglo que el #130 avisó que NO había que hacer, y por eso su test va
    /// junto al que introduce el índice y no en otro fichero.
    /// </remarks>
    [Fact]
    public void Una_saga_terminada_se_sigue_encontrando_por_su_llave()
    {
        var store = Almacen();
        store.Put(Compra("k-compra-1", SagaStatus.Completed));
        store.Put(Compra("k-compra-2", SagaStatus.Compensated));

        Assert.NotNull(store.Find("k-compra-1"));
        Assert.Equal(SagaStatus.Completed, store.Find("k-compra-1")!.Status);
        Assert.NotNull(store.Find("k-compra-2"));
    }

    /// <summary>
    /// <b>Y el recibo entero, no sólo el estado.</b>
    /// </summary>
    /// <remarks>
    /// Es lo que descarta la salida que el #130 recomendaba —partir el libro de idempotencia de
    /// la saga y podar el cuerpo—: los cuatro flujos contestan un reintento con
    /// <c>Result.Ok(slot.Reusar)</c>, o sea <b>con la saga entera</b>. Un libro de
    /// <c>llave → estado</c> sabría decir «esto ya pasó» y no QUÉ pasó.
    /// </remarks>
    [Fact]
    public void Y_el_reintento_recupera_el_RECIBO_entero_y_no_solo_el_estado()
    {
        var store = Almacen();
        store.Put(Compra("k-compra-3", SagaStatus.Completed) with
        {
            OrderId = "ord-77",
            PaymentId = "pay-77",
        });

        var recuperada = store.Find("k-compra-3");

        Assert.NotNull(recuperada);
        Assert.Equal("ord-77", recuperada!.OrderId);
        Assert.Equal("pay-77", recuperada.PaymentId);
        Assert.Equal(Money.Of(119000, "COP"), recuperada.Total);
    }

    // ── El índice dice la verdad, y se cura solo ────────────────────────────

    [Fact]
    public void El_indice_lleva_las_vivas_y_solo_las_vivas()
    {
        var store = Almacen();
        store.Put(Compra("viva-1", SagaStatus.Running));
        store.Put(Compra("viva-2", SagaStatus.Compensating));
        store.Put(Compra("viva-3", SagaStatus.CompensationFailed));
        store.Put(Compra("hecha-1", SagaStatus.Completed));
        store.Put(Compra("hecha-2", SagaStatus.Compensated));

        Assert.Equal(3, Directory.EnumerateFiles(Indice, "*.id").Count());
    }

    /// <summary>
    /// Una saga que pasa de viva a terminal SALE del índice — si no, el índice crecería igual
    /// que el directorio y no habría arreglado nada.
    /// </summary>
    [Fact]
    public void Al_cerrarse_una_saga_su_marca_se_va()
    {
        var store = Almacen();
        store.Put(Compra("k", SagaStatus.Running));
        Assert.Single(Directory.EnumerateFiles(Indice, "*.id"));

        store.Put(Compra("k", SagaStatus.Completed));

        Assert.Empty(Directory.EnumerateFiles(Indice, "*.id"));
    }

    /// <summary>
    /// <b>Sin índice se recorre TODO y se reconstruye</b>: el primer arranque tras este cambio,
    /// y un volumen restaurado. El peor caso es el comportamiento de antes, nunca uno peor.
    /// </summary>
    [Fact]
    public void Sin_indice_se_reconstruye_del_disco_en_vez_de_contestar_vacio()
    {
        var store = Almacen();
        store.Put(Compra("colgada", SagaStatus.Compensating));
        store.Put(Compra("hecha", SagaStatus.Completed));

        // Lo que ve una instalación anterior al índice: los documentos están y el índice no.
        Directory.Delete(Indice, recursive: true);

        var pendientes = store.WithPendingCompensations();

        Assert.Single(pendientes);
        Assert.Equal("colgada", pendientes[0].Id);
        Assert.True(Directory.Exists(Indice), "la vuelta que recorre todo tiene que dejar el índice escrito");
        Assert.Single(Directory.EnumerateFiles(Indice, "*.id"));
    }

    /// <summary>
    /// <b>Una marca de MÁS se limpia sola</b>, que es el residuo que deja una caída entre los dos
    /// pasos de <c>Put</c> — el lado hacia el que este diseño falla a propósito.
    /// </summary>
    [Fact]
    public void Una_marca_que_sobra_se_limpia_al_pasar()
    {
        var store = Almacen();
        store.Put(Compra("colgada", SagaStatus.Compensating));

        // El residuo exacto: la saga se cerró y la marca no se llegó a borrar.
        var huerfana = Path.Combine(Indice, "0".PadLeft(64, '0') + ".id");
        File.WriteAllText(huerfana, "no-existe");
        store.Put(Compra("cerrada", SagaStatus.Completed));
        File.WriteAllText(
            Path.Combine(Indice,
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("cerrada"))).ToLowerInvariant() + ".id"),
            "cerrada");

        var pendientes = store.WithPendingCompensations();

        Assert.Single(pendientes);
        Assert.Equal("colgada", pendientes[0].Id);
        Assert.False(File.Exists(huerfana), "la marca de una saga que no existe se borra al pasar");
        Assert.Single(Directory.EnumerateFiles(Indice, "*.id"));
    }

    /// <summary>
    /// <b>El espejo, y sin él nada de lo de arriba prueba que el resultado no cambió.</b>
    /// </summary>
    /// <remarks>
    /// <para>Un índice que se dejara sagas fuera pasaría todos los tests anteriores —contestan
    /// sobre fixtures de una o tres— y devolvería menos trabajo del que hay. Acá se compara
    /// contra el recorrido completo, que es la otra derivación de la misma verdad: el orden
    /// también, porque el barrido atiende por antigüedad y perderlo cambia a quién se compensa
    /// primero.</para>
    ///
    /// <para><b>Y el punto ciego que tenía, medido y cerrado — va escrito porque la primera
    /// versión de este test lo tenía y sonaba completa.</b> Los dos caminos que compara —el
    /// índice y la reconstrucción— usan el MISMO <c>EstaViva</c>, así que quitarle un estado los
    /// deja <i>de acuerdo y equivocados</i>: comprobado sacando <c>CompensationFailed</c>, el
    /// espejo pasaba en VERDE. Dos derivaciones que comparten el predicado no son dos
    /// derivaciones. Por eso ahora se compara además contra la lista que sale del <b>FIXTURE</b>
    /// —los estados que este test puso, escritos acá y no preguntados al almacén—, y con eso la
    /// misma mutación lo pone rojo.</para>
    /// </remarks>
    [Fact]
    public void El_resultado_es_EL_MISMO_que_recorriendo_el_almacen_entero()
    {
        var store = Almacen();
        var estados = new[]
        {
            SagaStatus.Running, SagaStatus.Completed, SagaStatus.Compensating,
            SagaStatus.Compensated, SagaStatus.CompensationFailed,
        };

        var puestas = new List<PurchaseSaga>();
        for (var i = 0; i < 40; i++)
        {
            // Fechas de arranque DESORDENADAS respecto del id, o el orden por antigüedad y el
            // orden por identificador darían lo mismo y el espejo no probaría nada.
            var saga = Compra($"s-{i:00}", estados[i % estados.Length], Ahora.AddMinutes(-((i * 7) % 40) - 90));
            puestas.Add(saga);
            store.Put(saga);
        }

        // Lo que el FIXTURE dice que tiene que salir, sin preguntarle al almacén — es lo único
        // que no comparte `EstaViva` con los dos caminos que se están comparando.
        var esperadas = puestas
            .Where(s => s.Status is SagaStatus.Compensating or SagaStatus.CompensationFailed)
            .OrderBy(s => s.StartedAtUtc).ThenBy(s => s.Id, StringComparer.Ordinal)
            .Select(s => s.Id).ToList();

        var conIndice = store.WithPendingCompensations().Select(s => s.Id).ToList();
        var abandonoConIndice = store.StartedBefore(Ahora.AddHours(-1)).Select(s => s.Id).ToList();

        Directory.Delete(Indice, recursive: true);
        var sinIndice = Almacen().WithPendingCompensations().Select(s => s.Id).ToList();
        Directory.Delete(Indice, recursive: true);
        var abandonoSinIndice = Almacen().StartedBefore(Ahora.AddHours(-1)).Select(s => s.Id).ToList();

        Assert.NotEmpty(conIndice);
        Assert.NotEmpty(abandonoConIndice);
        Assert.Equal(esperadas, conIndice);
        Assert.Equal(sinIndice, conIndice);
        Assert.Equal(abandonoSinIndice, abandonoConIndice);
    }
}
