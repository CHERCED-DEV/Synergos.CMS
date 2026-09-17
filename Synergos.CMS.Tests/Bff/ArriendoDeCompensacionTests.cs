using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Synergos.Bff.Core;
using Synergos.Bff.Tienda.Domain;
using Synergos.Core;
using Compensation = Synergos.Bff.Core.Compensation;

namespace Synergos.CMS.Tests.Bff;

/// <summary>
/// Dos barridos sobre el mismo almacén no compensan lo mismo dos veces (#34).
/// </summary>
/// <remarks>
/// <para><b>El defecto que reproducen estos tests.</b> El barrido tomaba lo pendiente y lo
/// ejecutaba, y <i>nada marcaba que alguien ya lo estuviera ejecutando</i>. Dos réplicas del mismo
/// orquestador contra el mismo <c>Storage:Root</c> ven la misma saga pendiente y las dos la
/// deshacen — y una compensación <b>no es idempotente por naturaleza</b>: «devolver 2 unidades»
/// ejecutado dos veces devuelve 4 (#30), y un reembolso doble es lo mismo con plata.</para>
///
/// <para><b>Hoy no muerde porque hay una sola instancia, no porque el barrido esté bien.</b> Eso
/// es una limitación del almacén sosteniendo una propiedad del motor, y es exactamente el tipo de
/// suposición que se rompe el día que se cambia de almacén — que es la primera razón que
/// <c>CLAUDE.md</c> §11 da para tocarlo.</para>
///
/// <para><b>DOS almacenes sobre el mismo directorio, y eso ES el fixture.</b> Un proceso se simula
/// con su propia instancia de <see cref="FileSystemSagaStore{TSaga}"/> y su propio motor: lo que
/// se reproduce es que dos barridos independientes miran el mismo directorio a la vez. Con un solo
/// motor compartido los dos pasos irían por el mismo <c>lock</c> y <b>el defecto no se
/// reproduce</b> — el test pasaría en verde sin arriendo y no probaría nada (la lección de
/// <c>AbandonoTests</c>, con otro disfraz).</para>
///
/// <para><b>Lo que ya NO hace falta simular es el caché</b> (#112). Esta nota decía que dos
/// instancias eran dos cachés y que ésa era «la única diferencia que importa entre un proceso y
/// otro». Era cierto y dejó de serlo: <c>JsonCollectionStore</c> guarda un fichero por documento y
/// relee el disco en cada lectura, así que una réplica ya no puede decidir contra la foto de su
/// arranque. El arriendo <b>sigue haciendo falta</b> por lo otro, que es lo que el caché nunca
/// tapó: dos barridos que miran el disco <i>en el mismo instante</i> ven los dos la misma
/// compensación pendiente.</para>
/// </remarks>
public sealed class ArriendoDeCompensacionTests : IDisposable
{
    private readonly string _raiz = Path.Combine(
        Path.GetTempPath(), "syn-arriendo-test-" + Guid.NewGuid().ToString("n"));

    public void Dispose()
    {
        if (Directory.Exists(_raiz))
        {
            try { Directory.Delete(_raiz, recursive: true); } catch { /* best-effort */ }
        }
    }

    private static readonly DateTimeOffset Ahora = new(2026, 8, 4, 12, 0, 0, TimeSpan.Zero);

    private sealed class RelojFalso : TimeProvider
    {
        private DateTimeOffset _now;
        public RelojFalso(DateTimeOffset inicio) => _now = inicio;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Avanzar(TimeSpan d) => _now += d;
    }

    /// <summary>Cuántas veces se ejecutó la compensación, sumando los dos «procesos».</summary>
    private sealed class Contador
    {
        private int _n;
        public void Mas() => Interlocked.Increment(ref _n);
        public int Total => Volatile.Read(ref _n);
    }

    /// <summary>
    /// Deshace siempre bien, y deja constancia de que lo hizo.
    /// </summary>
    /// <remarks>
    /// Que SIEMPRE salga bien es parte del fixture: si fallara, la segunda ejecución se podría
    /// explicar como un reintento legítimo y el test dejaría de distinguir el defecto de la
    /// conducta correcta.
    /// </remarks>
    private sealed class Ejecutor : ICompensationExecutor<PurchaseSaga>
    {
        private readonly Contador _contador;
        private readonly Func<Task>? _mientrasDentro;

        public Ejecutor(Contador contador, Func<Task>? mientrasDentro = null)
        {
            _contador = contador;
            _mientrasDentro = mientrasDentro;
        }

        public async Task<Rejection?> UndoAsync(PurchaseSaga saga, Compensation pending, CancellationToken ct)
        {
            _contador.Mas();
            if (_mientrasDentro is not null) await _mientrasDentro();
            return null;
        }
    }

    /// <summary>Una fábrica de clientes que lanza: si alguien avisa acá, es que algo se colgó.</summary>
    private sealed class SinRed : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
            => throw new InvalidOperationException(
                "Estos tests compensan bien: nadie tendría que estar avisando a la guardia.");
    }

    /// <summary>Un «proceso»: su propio almacén (o sea su propio caché) y su propio arriendo.</summary>
    private sealed record Proceso(SagaEngine<PurchaseSaga> Motor, ISagaStore<PurchaseSaga> Almacen);

    private Proceso Levantar(Contador contador, Func<Task>? mientrasDentro = null, TimeProvider? reloj = null)
    {
        var clock = reloj ?? TimeProvider.System;
        var vocabulario = new SagaVocabulary("tienda", "la compra");
        var almacen = new FileSystemSagaStore<PurchaseSaga>(
            Options.Create(new SagaStorageOptions { Root = _raiz }));

        var motor = new SagaEngine<PurchaseSaga>(
            almacen,
            new Compensator<PurchaseSaga>(new Ejecutor(contador, mientrasDentro), clock,
                NullLogger<Compensator<PurchaseSaga>>.Instance),
            new CompensationAlert(new SinRed(), vocabulario, Options.Create(new AlertOptions())),
            ArriendoDePrueba.Sobre(_raiz),
            vocabulario,
            clock,
            NullLogger<SagaEngine<PurchaseSaga>>.Instance);

        return new Proceso(motor, almacen);
    }

    /// <summary>Una compra que YA falló y tiene una compensación PENDIENTE — no armada.</summary>
    private static PurchaseSaga Deshaciendose(string id, int intentos = 0)
        => new(id, Ref.Create("tienda.comprador", "u-1"), "c-1", SagaStatus.Compensating,
            Array.Empty<StockHold>(), null, null, null, Money.Of(119000, "COP"),
            new[]
            {
                Compensation.For(TiendaCompensations.ReleaseStockHold, "sh-1", "la compra falló")
                    with { Attempts = intentos },
            },
            "algo falló", Ahora.AddMinutes(-10));

    // ── El defecto, en sus dos formas ───────────────────────────────────────

    [Fact]
    public async Task Dos_barridos_a_la_vez_NO_ejecutan_la_misma_compensacion_dos_veces()
    {
        var contador = new Contador();
        var entro = new TaskCompletionSource();
        var suelta = new TaskCompletionSource();

        // El primero se queda DENTRO del ejecutor hasta que el test lo suelte. Es la única forma de
        // elegir el instante que importa: con procesos de verdad, «a la vez» no se puede provocar.
        var uno = Levantar(contador, async () =>
        {
            entro.TrySetResult();
            await suelta.Task;
        });
        var otro = Levantar(contador);

        uno.Almacen.Put(Deshaciendose("compra-1"));

        // El segundo carga su caché con la compensación PENDIENTE, que es lo que ve una réplica
        // que lleva rato levantada.
        Assert.NotNull(otro.Almacen.Find("compra-1"));

        var enCurso = uno.Motor.CompensateAsync("compra-1", "barrido del proceso uno", default);
        await entro.Task;

        var segundo = await otro.Motor.CompensateAsync("compra-1", "barrido del proceso dos", default);

        suelta.SetResult();
        await enCurso;

        Assert.Equal(1, contador.Total);

        // Y el que no entró se entera de POR QUÉ, que es la mitad del arreglo: transitorio y no
        // conflicto, igual que el `retry_in_flight` de Api.Notifications. Quien recibe un
        // transitorio vuelve en la siguiente vuelta; quien recibe un conflicto se da por vencido.
        Assert.False(segundo.IsOk);
        Assert.Equal("tienda.compensation_in_flight", segundo.Rejection!.Code);
        Assert.True(segundo.Rejection!.IsTransient);
    }

    [Fact]
    public async Task El_segundo_barrido_NO_repite_lo_que_el_primero_YA_termino()
    {
        // La otra mitad, y la que no se ve mirando el arriendo: el segundo llega TARDE, cuando ya
        // no hay nadie compensando, y su caché sigue diciendo que la compensación está pendiente.
        // Sin releer bajo arriendo, la hace otra vez un minuto después — el arriendo solo, sin
        // esto, es teatro.
        var contador = new Contador();
        var uno = Levantar(contador);
        var otro = Levantar(contador);

        uno.Almacen.Put(Deshaciendose("compra-2"));

        // ⚠️ ESTE Find ES EL FIXTURE. Si el segundo almacén leyera por primera vez DESPUÉS de que
        // el primero terminó, bajaría al disco, vería la compensación hecha y no la repetiría — o
        // sea, el test pasaría en verde con el defecto puesto y no probaría nada.
        Assert.NotNull(otro.Almacen.Find("compra-2"));
        Assert.Single(otro.Almacen.Find("compra-2")!.Pending());

        var primero = await uno.Motor.CompensateAsync("compra-2", "barrido del proceso uno", default);
        Assert.True(primero.IsOk);
        Assert.Equal(SagaStatus.Compensated, primero.Value.Status);

        var segundo = await otro.Motor.CompensateAsync("compra-2", "barrido del proceso dos", default);

        Assert.Equal(1, contador.Total);
        Assert.True(segundo.IsOk);
        Assert.Equal(SagaStatus.Compensated, segundo.Value.Status);
    }

    // ── El arriendo, por sí mismo ───────────────────────────────────────────

    [Fact]
    public void Un_arriendo_VENCIDO_se_lo_queda_el_siguiente()
    {
        // Es la mitad delicada del ticket: un arriendo sin vencimiento convierte un proceso muerto
        // a media compensación en una compensación que no ejecuta NADIE NUNCA. Cambia «se hace dos
        // veces» por «no se hace», y la primera se nota mientras la segunda no.
        var reloj = new RelojFalso(Ahora);
        var muerto = ArriendoDePrueba.Sobre(_raiz, reloj, segundos: 60);
        var vivo = ArriendoDePrueba.Sobre(_raiz, reloj, segundos: 60);

        // Se toma y NO se suelta: el proceso se cayó con el arriendo en la mano.
        Assert.NotNull(muerto.TryAcquire("compra-3"));

        Assert.Null(vivo.TryAcquire("compra-3"));

        reloj.Avanzar(TimeSpan.FromSeconds(61));

        using var rescatado = vivo.TryAcquire("compra-3");
        Assert.NotNull(rescatado);
    }

    [Fact]
    public void Un_arriendo_no_puede_durar_menos_que_una_vuelta_de_compensacion()
    {
        // Configurarlo en un segundo no acelera nada: sólo hace que el de al lado se lo robe
        // MIENTRAS la primera vuelta todavía está hablando con las capacidades, que es el defecto
        // de este ticket con un fichero de por medio para disimularlo. Por eso el piso es duro.
        var reloj = new RelojFalso(Ahora);
        var uno = ArriendoDePrueba.Sobre(_raiz, reloj, segundos: 1);
        var otro = ArriendoDePrueba.Sobre(_raiz, reloj, segundos: 1);

        Assert.NotNull(uno.TryAcquire("compra-4"));

        reloj.Avanzar(TimeSpan.FromSeconds(5));
        Assert.Null(otro.TryAcquire("compra-4"));

        reloj.Avanzar(TimeSpan.FromSeconds(FileSystemSagaLease.MinimoSegundos));
        using var despues = otro.TryAcquire("compra-4");
        Assert.NotNull(despues);
    }

    [Fact]
    public void Soltar_un_arriendo_lo_deja_libre_para_el_siguiente()
    {
        // El caso normal, y no es ceremonia: si soltar no funcionara, la primera compensación de
        // cada saga bloquearía a la siguiente durante los cinco minutos del arriendo — y el
        // síntoma sería un barrido que «a veces no hace nada», que nadie relaciona con esto.
        var uno = ArriendoDePrueba.Sobre(_raiz);
        var otro = ArriendoDePrueba.Sobre(_raiz);

        var tomado = uno.TryAcquire("compra-5");
        Assert.NotNull(tomado);
        Assert.Null(otro.TryAcquire("compra-5"));

        tomado!.Dispose();

        using var segundo = otro.TryAcquire("compra-5");
        Assert.NotNull(segundo);
    }

    [Fact]
    public void Dos_sagas_distintas_no_se_estorban()
    {
        // El arriendo es POR SAGA. Uno global serializaría el barrido entero: una compensación
        // lenta contra una capacidad caída dejaría a las demás esperando su turno.
        var arriendos = ArriendoDePrueba.Sobre(_raiz);

        using var una = arriendos.TryAcquire("compra-6");
        using var otra = arriendos.TryAcquire("compra-7");

        Assert.NotNull(una);
        Assert.NotNull(otra);
    }

    [Fact]
    public void El_identificador_de_la_saga_no_se_mete_en_una_ruta()
    {
        // El id de una saga ES la llave de idempotencia que manda el llamador: 128 caracteres
        // cualesquiera. Un fichero nombrado con eso es una travesía de directorios servida en
        // bandeja — y además dos llaves distintas podrían acabar en el mismo sitio.
        var arriendos = ArriendoDePrueba.Sobre(_raiz);

        using var fea = arriendos.TryAcquire("../../etc/se-escapo");
        Assert.NotNull(fea);

        using var otra = arriendos.TryAcquire("../../etc/otra-cosa");
        Assert.NotNull(otra);

        // Los dos ficheros están DENTRO de la carpeta de arriendos, son dos —o sea que dos llaves
        // distintas no acabaron en el mismo sitio— y ninguno conserva un trozo de la llave.
        var ficheros = Directory.EnumerateFiles(Path.Combine(_raiz, "leases"), "*.lease")
            .Select(Path.GetFileNameWithoutExtension)
            .ToList();

        Assert.Equal(2, ficheros.Count);
        Assert.All(ficheros, n => Assert.Matches("^[0-9a-f]{64}$", n!));
    }

    [Fact]
    public async Task El_barrido_RELEE_antes_de_mirar_que_hay_que_hacer()
    {
        // La otra cara de la moneda: una réplica que nunca relee barre contra la foto de su
        // arranque. No es que compense de más — es que no compensa NADA de lo que no vio nacer, y
        // eso no se nota: el barrido corre, no falla, y no hace nada.
        var contador = new Contador();
        var uno = Levantar(contador);
        var otro = Levantar(contador);

        // ⚠️ EL FIXTURE, y lo que cambió con el #112. El segundo lee ANTES de que exista la saga:
        // con el almacén anterior eso le dejaba el caché cargado y VACÍO, y ahí estaba el defecto
        // —la réplica barría contra la foto de su arranque y no compensaba nada de lo que no vio
        // nacer—. Hoy el almacén es un fichero por documento y NO cachea, así que la lectura
        // previa ya no envenena nada y la segunda réplica ve la saga en cuanto está en el disco.
        // Se deja la lectura puesta a propósito: es la secuencia que reproducía el defecto, y que
        // ahora salga bien es el resultado, no la ausencia de prueba.
        Assert.Null(otro.Almacen.Find("compra-10"));

        uno.Almacen.Put(Deshaciendose("compra-10"));
        Assert.Single(otro.Almacen.WithPendingCompensations());   // la ve: ya no hay foto vieja

        var barrido = new CompensationSweeper<PurchaseSaga>(
            new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            new MonitorDeBarrido(new SweepOptions()),
            TimeProvider.System,
            NullLogger<CompensationSweeper<PurchaseSaga>>.Instance);

        await barrido.UnaVueltaAsync(otro.Motor, default);

        Assert.Equal(1, contador.Total);
        Assert.Equal(SagaStatus.Compensated, otro.Almacen.Find("compra-10")!.Status);
    }

    private sealed class MonitorDeBarrido : IOptionsMonitor<SweepOptions>
    {
        public MonitorDeBarrido(SweepOptions valor) => CurrentValue = valor;
        public SweepOptions CurrentValue { get; }
        public SweepOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<SweepOptions, string?> listener) => null;
    }

    // ── Y lo que el arriendo NO puede romper ────────────────────────────────

    [Fact]
    public async Task El_reintento_que_pide_una_persona_no_se_bloquea_a_SI_MISMO()
    {
        // El arriendo es un fichero, así que NO es reentrante: si el reintento manual lo toma y
        // después la compensación lo vuelve a pedir, se queda esperándose a sí mismo. El síntoma
        // sería la puerta de la persona contestando «ya lo está haciendo otro» para siempre, sobre
        // una saga que no está tocando nadie.
        var contador = new Contador();
        var uno = Levantar(contador);

        uno.Almacen.Put(Deshaciendose("compra-8", intentos: CompensationLimits.MaxAttempts)
            .WithStatus(SagaStatus.CompensationFailed));

        var r = await uno.Motor.RetryStuckAsync("compra-8", default);

        Assert.True(r.IsOk, r.IsOk ? string.Empty : r.Rejection!.ToString());
        Assert.Equal(SagaStatus.Compensated, r.Value.Status);
        Assert.Equal(1, contador.Total);
    }

    [Fact]
    public async Task Una_saga_VIVA_sigue_sin_ser_trabajo_del_barrido()
    {
        // «Armada no es pendiente» (feedback_compensation_is_data), y el arriendo no cambia eso:
        // lo que se arrienda es lo que hay que deshacer, no lo que está yendo bien. Tomar prestada
        // una compensación armada desharía una compra que va perfectamente, y cada paso
        // «funcionaría».
        var contador = new Contador();
        var uno = Levantar(contador);

        var viva = Deshaciendose("compra-9").WithStatus(SagaStatus.Running);
        uno.Almacen.Put(viva);

        Assert.Empty(uno.Almacen.WithPendingCompensations());
        Assert.NotEmpty(uno.Almacen.Find("compra-9")!.Compensations);
        Assert.Equal(0, contador.Total);
    }
}
