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
    /// Las dos colas comen del MISMO presupuesto — <b>contado, no cronometrado</b>.
    /// </summary>
    /// <remarks>
    /// <para><b>Este test medía un reloj de pared y era flaky</b> (#125): afirmaba «esperó menos
    /// de 1800 ms con un presupuesto de 1000», falló en una corrida de la suite entera y pasó
    /// suelto y en la siguiente. Se caía bajo carga de la máquina, no por el código que vigila —y
    /// un test de reloj que falla intermitente es cómo se aprende a ignorar un rojo, que es
    /// justamente lo que este repo tiene escrito en el otro sentido: «flake» no es una causa
    /// raíz. El día que se cayera por el defecto, nadie lo iba a mirar.</para>
    ///
    /// <para><b>La salida NO fue subir el margen.</b> Eso lo habría vuelto un test que no prueba
    /// nada. El plazo pasó a ser un TIPO —<c>StoreWriteGate.Presupuesto</c>— creado una vez al
    /// entrar, así que la regla se comprueba con funciones puras del instante que se les pasa, y
    /// el turno acepta un presupuesto ya hecho para poder entrar con uno <b>gastado</b>.</para>
    ///
    /// <para><b>Y la mutación sigue funcionando, que es la mitad que cuenta</b>: volver a crear el
    /// presupuesto después del semáforo —el defecto original— deja el último de estos tests
    /// esperando los cinco segundos enteros en vez de rendirse. Por eso ese margen es de 25× y no
    /// de 1,8×: la diferencia entre bueno y roto pasó de 800 ms a cinco segundos.</para>
    /// </remarks>
    [Fact]
    public void El_presupuesto_se_reparte_entre_las_dos_colas_y_no_se_reinicia()
    {
        var t0 = new DateTime(2026, 9, 16, 12, 0, 0, DateTimeKind.Utc);
        var presupuesto = StoreWriteGate.Presupuesto.Desde(t0, TimeSpan.FromSeconds(1));

        // La primera cola se lleva 900 ms: a la segunda le quedan 100, no 1000.
        Assert.Equal(TimeSpan.FromMilliseconds(100), presupuesto.Restante(t0.AddMilliseconds(900)));
        Assert.False(presupuesto.Agotado(t0.AddMilliseconds(900)));

        // Y si se lo lleva entero, a la segunda no le queda nada.
        Assert.Equal(TimeSpan.Zero, presupuesto.Restante(t0.AddMilliseconds(1000)));
        Assert.True(presupuesto.Agotado(t0.AddMilliseconds(1000)));
    }

    /// <summary>
    /// Un presupuesto pasado de plazo pide CERO, no «espera para siempre».
    /// </summary>
    /// <remarks>
    /// El caso feo y el que no se ve: <c>SemaphoreSlim.WaitAsync</c> trata <c>-1 ms</c> como
    /// espera infinita, así que un <c>Restante</c> que devolviera el negativo sin recortar
    /// convertiría «el presupuesto se acabó» en «espera lo que haga falta» — el contrario exacto
    /// de lo que el tipo existe para garantizar, y sin que nada falle: el llamador simplemente se
    /// queda.
    /// </remarks>
    [Fact]
    public void Un_presupuesto_vencido_pide_cero_y_no_espera_infinita()
    {
        var t0 = new DateTime(2026, 9, 16, 12, 0, 0, DateTimeKind.Utc);
        var presupuesto = StoreWriteGate.Presupuesto.Desde(t0, TimeSpan.FromSeconds(1));

        Assert.Equal(TimeSpan.Zero, presupuesto.Restante(t0.AddSeconds(30)));
        Assert.NotEqual(TimeSpan.FromMilliseconds(-1), presupuesto.Restante(t0.AddSeconds(30)));
    }

    /// <summary>
    /// Con el presupuesto gastado se intenta el fichero UNA vez y se rinde — sin re-armar el reloj.
    /// </summary>
    /// <remarks>
    /// <para><b>Es el test que caza la mutación</b>, y el que reemplaza al cronómetro. El turno se
    /// pide con un presupuesto ya vencido y con el cerrojo tomado por la otra instancia: lo
    /// correcto es intentar el fichero una vez —rendirse sin mirar rechazaría un cerrojo que
    /// puede estar libre— y devolver <c>null</c> en el acto.</para>
    ///
    /// <para>El presupuesto de la instancia es de <b>cinco segundos</b> a propósito: si alguien
    /// vuelve a calcular el límite después del semáforo, este test pasa de milisegundos a cinco
    /// segundos enteros. El margen de 500 ms es de 10× contra el bueno y de 1/10 contra el roto,
    /// así que no depende de lo cargada que esté la máquina — que era el defecto del test
    /// anterior, donde bueno y roto se llevaban 800 ms.</para>
    /// </remarks>
    [Fact]
    public async Task Con_el_presupuesto_gastado_se_rinde_en_el_acto()
    {
        var a = Turno(segundos: 5);
        var b = Turno(segundos: 5);

        using var tomado = await a.TryEnterAsync();
        Assert.NotNull(tomado);

        var reloj = System.Diagnostics.Stopwatch.StartNew();
        var segundo = await b.TryEnterAsync(
            StoreWriteGate.Presupuesto.Gastado(DateTime.UtcNow), CancellationToken.None);
        reloj.Stop();

        Assert.Null(segundo);
        Assert.True(reloj.Elapsed < TimeSpan.FromMilliseconds(500),
            $"Tardó {reloj.ElapsedMilliseconds} ms con el presupuesto YA gastado y 5 s "
            + "configurados: el reloj se está re-armando después del semáforo.");
    }

    /// <summary>
    /// Y con el presupuesto gastado y el cerrojo LIBRE, entra.
    /// </summary>
    /// <remarks>
    /// La otra mitad, y la que impide que el test de arriba pase por la razón equivocada: si el
    /// turno se rindiera sin intentar el fichero, aquél saldría verde igual. Rendirse sin mirar
    /// rechazaría con un 503 un cerrojo que el de al lado acaba de soltar — un rechazo que no le
    /// corresponde a nadie.
    /// </remarks>
    [Fact]
    public async Task Con_el_presupuesto_gastado_pero_el_cerrojo_libre_SI_entra()
    {
        var b = Turno(segundos: 5);

        using var turno = await b.TryEnterAsync(
            StoreWriteGate.Presupuesto.Gastado(DateTime.UtcNow), CancellationToken.None);

        Assert.NotNull(turno);
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
