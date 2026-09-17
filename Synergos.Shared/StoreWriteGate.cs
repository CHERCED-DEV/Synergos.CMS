using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Synergos.Core;

namespace Synergos.Shared;

/// <summary>
/// Un solo escritor por capacidad, <b>aunque haya varias réplicas</b>.
/// </summary>
/// <remarks>
/// <para><b>Qué problema cierra, y por qué el almacén no podía cerrarlo solo (#112).</b>
/// <see cref="JsonCollectionStore{T}"/> pasó a un fichero por documento —en el mismo commit que
/// esto, y ése es el orden que importa: mientras el almacén reescribiera la colección entera, el
/// turno habría serializado escrituras que igual se borraban unas a otras—, así que dos réplicas
/// que tocan documentos distintos ya no se pisan. Lo que queda es el mismo documento: leer,
/// decidir y escribir son <b>tres pasos</b> y la regla del negocio va en medio —«¿queda cupo?»,
/// «¿ya se capturó?»—, así que ningún <c>Put</c> puede saber que lo que está escribiendo se
/// decidió sobre una lectura que ya envejeció. Dos devoluciones de existencias leen 10, escriben
/// 12 y 13, y la segunda pisa a la primera: 13 donde tenía que haber 15. Es literalmente el
/// defecto #30 —que se arregló <i>dentro</i> de la capacidad con el ajuste relativo— vuelto a
/// aparecer <i>entre</i> réplicas.</para>
///
/// <para><b>Esto no inventa exclusión: la SUBE.</b> Las veinte capacidades ya serializaban sus
/// mutaciones con un <c>lock (_gate)</c> de capacidad entera —uno por servicio, no uno por
/// documento—, y <c>Api.Payments</c> con un <c>SemaphoreSlim</c> por la misma razón. Lo único que
/// cambia es <b>dónde vive esa exclusión</b>: un <c>lock</c> es de proceso y el caso son dos
/// procesos. El rendimiento no cambia de forma —un escritor a la vez por capacidad ya era la
/// regla—, cambia de alcance.</para>
///
/// <para><b>Va en el borde y no en cada servicio, y eso es una decisión.</b> Hay 59 sitios con
/// <c>lock (_gate)</c> en dieciocho servicios; tocarlos uno por uno habría puesto el rechazo por
/// ocupación en 59 lugares, que es donde se olvida uno. En el borde hay <b>un</b> sitio por
/// capacidad y el rechazo se escribe una vez. El precio de ponerlo ahí está dicho: sólo cubre lo
/// que entra por HTTP. Hoy alcanza porque <b>ningún GET de las veinte escribe</b> —comprobado
/// recorriendo los 49— y ninguna capacidad tiene un proceso de fondo que escriba por
/// <c>JsonCollectionStore</c>. El día que una lo tenga, esto no la cubre y hay que volver acá.</para>
///
/// <para><b>Se cierra con un cerrojo del sistema de ficheros y no con un arriendo con
/// vencimiento</b>, al revés que <see cref="ISagaLease"/> — y la diferencia está en quién muere.
/// Un arriendo necesita vencer porque lo toma un barrido que puede morirse a media compensación y
/// nadie lo soltaría. Un cerrojo de fichero lo suelta <b>el núcleo</b> cuando el proceso muere,
/// así que no hay marca colgada que robar ni reloj que ajustar — y sobre todo no existe el caso
/// feo del arriendo: que la petición dure más que su vencimiento y entre otra <i>mientras la
/// primera todavía está escribiendo</i>.</para>
///
/// <para><b>Un cerrojo de proceso cruzado hace aparecer un estado que las capacidades no sabían
/// nombrar</b>, y se decide acá y no en el primer incidente: esperar más de la cuenta es
/// <see cref="RejectionKind.Unavailable"/> con el código <c>{prefijo}.store_busy</c> — 503 y
/// <c>transient: true</c>. Es el mismo trato que <c>notifications.retry_in_flight</c>, y por la
/// misma razón: no es que la operación no se pueda hacer, es que otro la está haciendo. Que sea
/// transitorio es lo que permite que un orquestador lo reintente solo, en vez de deshacer una
/// saga sana por una espera.</para>
///
/// <para><b>El fichero del cerrojo vive donde los dos lo ven</b> —la raíz del almacén de la
/// capacidad, que es lo que un despliegue monta como volumen compartido—, que es la misma regla
/// de <c>feedback_sweep_lease_where_both_see_it</c>. Un <c>Mutex</c> con nombre <b>no</b> habría
/// servido: en Unix .NET lo resuelve en un temporal del usuario, así que dos contenedores sobre
/// el mismo volumen tendrían un mutex cada uno y los dos se creerían dueños.</para>
/// </remarks>
public sealed class StoreWriteGate : IDisposable
{
    /// <summary>Cuánto se espera al de al lado antes de rendirse.</summary>
    /// <remarks>
    /// Generoso a propósito: dentro del cerrojo hay capacidades que hablan por la red —el cobro de
    /// <c>Api.Payments</c> espera a la pasarela con su <c>SemaphoreSlim</c> tomado—, así que una
    /// espera corta convertiría una pasarela lenta en una lluvia de 503 que nadie pidió.
    /// </remarks>
    public const int SegundosPorDefecto = 30;

    private const string Fichero = ".escrituras.lock";

    private readonly string _ruta;
    private readonly string _codePrefix;
    private readonly TimeSpan _espera;

    // El cerrojo de fichero resuelve entre PROCESOS. Entre hilos del mismo proceso no se apoya en
    // él: si `FileShare.None` se implementara por descriptor y no por fichero, dos hilos de acá
    // pasarían los dos. Este semáforo hace que esa duda no importe.
    private readonly SemaphoreSlim _local = new(1, 1);

    /// <inheritdoc />
    public void Dispose() => _local.Dispose();

    public StoreWriteGate(string root, string codePrefix, int? segundos = null)
    {
        Directory.CreateDirectory(root);
        _ruta = Path.Combine(root, Fichero);
        _codePrefix = codePrefix;
        _espera = TimeSpan.FromSeconds(Math.Max(1, segundos ?? SegundosPorDefecto));
    }

    /// <summary>Lo que se contesta cuando la espera se agotó.</summary>
    public Rejection Ocupado => Rechazo(_codePrefix);

    /// <summary>
    /// El rechazo por ocupación, con el prefijo de quien lo emite.
    /// </summary>
    /// <remarks>
    /// <b>Se construye con el prefijo del llamador</b>, como
    /// <c>{codePrefix}.idempotency_key_required</c>: es un código que sale de <c>Shared</c> y que
    /// una capacidad devuelve <b>sin declararlo</b> en su fichero de reglas, así que va nombrado
    /// en <c>CLAUDE.md</c> §11 — hay gate (<c>ApiMoldTests</c>).
    /// </remarks>
    public static Rejection Rechazo(string codePrefix) => Rejection.Unavailable(
        $"{codePrefix}.store_busy",
        "Otra escritura de esta capacidad está en curso y la espera se agotó. Es transitorio: reintentá.");

    /// <summary>
    /// Toma el turno de escritura, o <c>null</c> si el de al lado no lo soltó a tiempo.
    /// </summary>
    /// <remarks>
    /// <para><b>UN presupuesto para las dos esperas, y es una decisión.</b> Hay dos colas —el
    /// semáforo de hilos de esta instancia y el cerrojo de fichero entre procesos— y darle
    /// <see cref="SegundosPorDefecto"/> a cada una dejaría a quien llama esperando el doble de lo
    /// que el despliegue configuró. Eso no se lee como «ocupado»: se lee como <b>colgado</b>, y
    /// quien llama se va por su propio plazo antes de ver el 503 — o sea que el rechazo
    /// transitorio, que existe justamente para que un orquestador reintente en vez de deshacer una
    /// saga sana, no llega nunca. Acá el reloj arranca al entrar y las dos colas comen del mismo.
    /// </para>
    ///
    /// <para><b>Y aun con el presupuesto gastado se intenta el fichero UNA vez.</b> Si el semáforo
    /// se llevó la espera entera, rendirse sin mirar rechazaría un cerrojo que puede estar libre
    /// —el de al lado soltó mientras hacíamos cola— con un 503 que no le corresponde a nadie.</para>
    /// </remarks>
    public Task<IDisposable?> TryEnterAsync(CancellationToken ct = default)
        => TryEnterAsync(Presupuesto.Desde(DateTime.UtcNow, _espera), ct);

    /// <summary>
    /// El mismo turno, con el presupuesto ya creado.
    /// </summary>
    /// <remarks>
    /// <para><b>Existe para que la propiedad se pueda probar CONTANDO y no cronometrando</b>
    /// (#125). El test que vigilaba «las dos colas comen del mismo presupuesto» medía un reloj de
    /// pared —«esperó menos de 1800 ms con 1000 de presupuesto»— y se caía bajo carga de la
    /// máquina, no por el código que vigila. Un test de reloj que falla intermitente es cómo se
    /// aprende a ignorar un rojo, y este repo ya tiene escrito que «flake» no es una causa raíz:
    /// el día que se cayera por el defecto, nadie lo iba a mirar.</para>
    ///
    /// <para>Con el presupuesto como PARÁMETRO, un test puede entrar con uno <b>ya gastado</b> y
    /// afirmar lo que de verdad importa —que se rinde sin volver a hacer cola— sin depender de
    /// cuánto tarde la máquina. Y la mutación sigue funcionando, que es la mitad que cuenta:
    /// volver a crear el presupuesto después del semáforo deja ese test esperando el presupuesto
    /// entero.</para>
    /// </remarks>
    internal async Task<IDisposable?> TryEnterAsync(Presupuesto presupuesto, CancellationToken ct)
    {
        if (!await _local.WaitAsync(presupuesto.Restante(DateTime.UtcNow), ct).ConfigureAwait(false))
        {
            return null;
        }

        try
        {
            while (true)
            {
                try
                {
                    var fs = new FileStream(
                        _ruta, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None,
                        bufferSize: 1, FileOptions.None);
                    return new Turno(fs, _local);
                }
                catch (IOException)
                {
                    // Lo tiene otro proceso. No se espera "a que avise": no hay a quién
                    // suscribirse en un cerrojo de fichero, así que se vuelve a intentar.
                }
                catch (UnauthorizedAccessException)
                {
                }

                if (presupuesto.Agotado(DateTime.UtcNow))
                {
                    _local.Release();
                    return null;
                }
                await Task.Delay(15, ct).ConfigureAwait(false);
            }
        }
        catch
        {
            _local.Release();
            throw;
        }
    }

    /// <summary>
    /// El plazo de la espera, creado UNA vez al entrar y consultado por las dos colas.
    /// </summary>
    /// <remarks>
    /// <para>Es la regla del <c>&lt;remarks&gt;</c> de <see cref="TryEnterAsync(CancellationToken)"/>
    /// vuelta un TIPO: mientras el límite era una variable local recalculable, «las dos colas
    /// comen del mismo presupuesto» era una propiedad que sólo un cronómetro podía comprobar.
    /// Acá <c>Restante</c> y <c>Agotado</c> son funciones puras del instante que se les pasa, así
    /// que la regla se prueba sin reloj de pared (#125).</para>
    ///
    /// <para><b><c>Restante</c> nunca es negativo</b>: <c>SemaphoreSlim.WaitAsync</c> lanza con un
    /// <c>TimeSpan</c> negativo que no sea <c>-1</c>, así que un presupuesto agotado tiene que
    /// dar cero —«no esperes»— y no «espera para siempre», que es lo que significaría <c>-1</c> y
    /// es exactamente el fallo que nadie vería.</para>
    /// </remarks>
    internal readonly struct Presupuesto
    {
        private readonly DateTime _limite;

        private Presupuesto(DateTime limite) => _limite = limite;

        public static Presupuesto Desde(DateTime ahora, TimeSpan espera) => new(ahora + espera);

        /// <summary>Un presupuesto que ya se gastó. Para los tests de la regla.</summary>
        public static Presupuesto Gastado(DateTime ahora) => new(ahora);

        public TimeSpan Restante(DateTime ahora)
        {
            var resto = _limite - ahora;
            return resto > TimeSpan.Zero ? resto : TimeSpan.Zero;
        }

        public bool Agotado(DateTime ahora) => ahora >= _limite;
    }

    private sealed class Turno : IDisposable
    {
        private readonly FileStream _fs;
        private readonly SemaphoreSlim _local;
        private bool _soltado;

        public Turno(FileStream fs, SemaphoreSlim local)
        {
            _fs = fs;
            _local = local;
        }

        public void Dispose()
        {
            if (_soltado) return;
            _soltado = true;
            try { _fs.Dispose(); } catch (IOException) { /* el núcleo lo suelta igual */ }
            _local.Release();
        }
    }
}

/// <summary>Cablea el turno de escritura en el borde de una capacidad.</summary>
public static class StoreWriteGateExtensions
{
    /// <summary>
    /// Serializa toda mutación de esta capacidad, aunque corran varias réplicas.
    /// </summary>
    /// <remarks>
    /// <para><b>Sólo lo que muta.</b> Las lecturas no entran: cada documento se reemplaza con un
    /// <c>rename</c>, que es atómico, así que un <c>GET</c> ve la versión de antes o la de después
    /// —nunca media— y hacerle esperar su turno serializaría el sitio entero para no ganar nada.
    /// Que ningún <c>GET</c> escriba es una propiedad de estas veinte que hay que sostener; si
    /// alguna empieza a escribir en una lectura, esto deja de cubrirla.</para>
    ///
    /// <para><b>Va después de la llave compartida</b>: quien no se identificó no hace cola. Al
    /// revés, cualquiera que supiera la URL podría dejar a la capacidad sin turnos.</para>
    /// </remarks>
    public static IApplicationBuilder UseStoreWriteGate(
        this IApplicationBuilder app, string root, string codePrefix, int? segundos = null)
    {
        var turno = new StoreWriteGate(root, codePrefix, segundos);

        return app.Use(async (context, next) =>
        {
            if (HttpMethods.IsGet(context.Request.Method)
                || HttpMethods.IsHead(context.Request.Method)
                || HttpMethods.IsOptions(context.Request.Method))
            {
                await next(context).ConfigureAwait(false);
                return;
            }

            using var tomado = await turno.TryEnterAsync(context.RequestAborted).ConfigureAwait(false);
            if (tomado is null)
            {
                await turno.Ocupado.ToProblem().ExecuteAsync(context).ConfigureAwait(false);
                return;
            }

            await next(context).ConfigureAwait(false);
        });
    }
}
