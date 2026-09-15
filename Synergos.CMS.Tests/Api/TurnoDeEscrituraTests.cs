using Synergos.Core;
using Synergos.Shared;

namespace Synergos.CMS.Tests.Api;

/// <summary>
/// Un solo escritor por capacidad aunque haya dos réplicas (#112).
/// </summary>
/// <remarks>
/// <para><b>DOS instancias sobre la misma raíz, y eso ES el fixture.</b> El semáforo en memoria de
/// <see cref="StoreWriteGate"/> es de la instancia, así que dos instancias sólo se pueden excluir
/// por el fichero — que es exactamente el camino que recorren dos procesos. Con una sola
/// instancia estos tests pasarían por el semáforo y no probarían nada del #112.</para>
/// </remarks>
public sealed class TurnoDeEscrituraTests : IDisposable
{
    private readonly string _raiz = Path.Combine(
        Path.GetTempPath(), "syn-turno-" + Guid.NewGuid().ToString("n"));

    public void Dispose()
    {
        try { if (Directory.Exists(_raiz)) Directory.Delete(_raiz, recursive: true); }
        catch (IOException) { /* el temporal no es el sujeto del test */ }
    }

    private StoreWriteGate Turno(int segundos = 1) => new(_raiz, "inventory", segundos);

    [Fact]
    public async Task Mientras_una_replica_escribe_la_otra_no_entra()
    {
        var a = Turno();
        var b = Turno();

        using var tomado = await a.TryEnterAsync();
        Assert.NotNull(tomado);

        Assert.Null(await b.TryEnterAsync());
    }

    [Fact]
    public async Task Cuando_la_primera_suelta_la_otra_entra()
    {
        var a = Turno();
        var b = Turno();

        (await a.TryEnterAsync())!.Dispose();

        using var segundo = await b.TryEnterAsync();
        Assert.NotNull(segundo);
    }

    /// <summary>
    /// Rendirse es <b>transitorio</b>, y eso es lo que decide si un orquestador reintenta o
    /// deshace una saga sana.
    /// </summary>
    /// <remarks>
    /// Mismo trato que <c>notifications.retry_in_flight</c>: no es que no se pueda, es que otro
    /// está en curso. Si esto fuera <c>Conflict</c>, <c>Bff.Core</c> compensaría una compra que
    /// sólo tenía que esperar quince milisegundos.
    /// </remarks>
    [Fact]
    public void Rendirse_es_transitorio_y_lleva_el_prefijo_de_la_capacidad()
    {
        var ocupado = Turno().Ocupado;

        Assert.Equal(RejectionKind.Unavailable, ocupado.Kind);
        Assert.True(ocupado.IsTransient);
        Assert.Equal("inventory.store_busy", ocupado.Code);
    }

    /// <summary>
    /// Las dos colas comen del MISMO presupuesto.
    /// </summary>
    /// <remarks>
    /// <para><b>El fixture tiene que hacer cola DOS veces, y por eso hay dos llamadas a la misma
    /// instancia.</b> Con instancias distintas sólo se hace cola en el fichero y el test pasaría
    /// en verde con los dos presupuestos puestos: el segundo llamador de <c>b</c> espera primero
    /// el semáforo de hilos de <c>b</c> y sólo después el cerrojo de fichero, que es la única
    /// secuencia donde se nota si el reloj se reinicia en medio.</para>
    ///
    /// <para>Se mide un techo y no un valor: lo que se afirma es que esperar no puede pasar del
    /// presupuesto configurado, porque una espera del doble no se lee como «ocupado» sino como
    /// colgado — y quien llama se va por su propio plazo antes de ver el 503. Comprobado mutando:
    /// volviendo a calcular el límite DESPUÉS del semáforo, este test tarda ~1,95 s y se pone
    /// rojo.</para>
    /// </remarks>
    [Fact]
    public async Task La_espera_total_no_pasa_del_presupuesto()
    {
        var a = Turno();
        var b = Turno();

        using var tomado = await a.TryEnterAsync();
        Assert.NotNull(tomado);

        var primero = b.TryEnterAsync();          // se queda con el semáforo de hilos de `b`
        await Task.Delay(50);                     // y le da tiempo a llegar al fichero

        var reloj = System.Diagnostics.Stopwatch.StartNew();
        var segundo = await b.TryEnterAsync();    // hace cola en el semáforo Y después en el fichero
        reloj.Stop();

        Assert.Null(await primero);
        Assert.Null(segundo);
        Assert.True(reloj.Elapsed < TimeSpan.FromMilliseconds(1800),
            $"Esperó {reloj.ElapsedMilliseconds} ms con un presupuesto de 1000 ms: las dos colas "
            + "están cobrando cada una su espera entera.");
    }

    /// <summary>El turno se toma de nuevo tantas veces como haga falta.</summary>
    [Fact]
    public async Task El_turno_se_toma_y_se_suelta_repetidamente()
    {
        var a = Turno();
        for (var i = 0; i < 5; i++)
        {
            var tomado = await a.TryEnterAsync();
            Assert.NotNull(tomado);
            tomado!.Dispose();
        }
    }
}
