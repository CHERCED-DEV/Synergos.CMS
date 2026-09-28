using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Synergos.Shared;

namespace Synergos.Bff.Core;

/// <summary>Dónde vive el almacén de un orquestador.</summary>
public sealed class SagaStorageOptions
{
    public string Root { get; set; } = Path.Combine(AppContext.BaseDirectory, "data", "bff");
}

/// <summary>
/// La bitácora de sagas y sus compensaciones pendientes.
/// </summary>
/// <remarks>
/// <b>Es el único almacén de un orquestador, y a propósito.</b> Un BFF que guardara su propia
/// copia de citas, pagos, pedidos o cupos tendría dos verdades que se desincronizan con las
/// capacidades. Lo único que le pertenece —y que nadie más puede saber— es <i>qué pasos dio y
/// qué queda por deshacer</i>.
/// </remarks>
public interface ISagaStore<TSaga> where TSaga : class, ISaga
{
    TSaga? Find(string id);

    /// <summary>Las sagas que están deshaciendo algo. <b>No las sanas.</b></summary>
    IReadOnlyList<TSaga> WithPendingCompensations();

    /// <summary>
    /// Las que empezaron antes de <paramref name="limite"/> y <b>siguen sin cerrar</b>.
    /// </summary>
    /// <remarks>
    /// <para>Es la consulta del abandono (HU #29), y es DISTINTA de la de arriba a propósito. Una
    /// saga en <c>Running</c> lleva sus compensaciones <b>armadas</b>, no pendientes: es una
    /// operación sana esperando el paso que cuesta. Meterla en <c>WithPendingCompensations</c>
    /// haría que el barrido deshiciera compras que iban perfectamente
    /// (<c>feedback_compensation_is_data</c>).</para>
    ///
    /// <para>Lo único que la vuelve trabajo es <b>el tiempo</b>. Por eso el filtro es una fecha y
    /// no un estado de la compensación.</para>
    /// </remarks>
    IReadOnlyList<TSaga> StartedBefore(DateTimeOffset limite);

    void Put(TSaga saga);

    /// <summary>
    /// Olvida lo que tenga en memoria: la próxima lectura va al almacén de verdad.
    /// </summary>
    /// <remarks>
    /// <para><b>Existe por el arriendo (#34), y sin esto el arriendo sería teatro.</b> El almacén
    /// cachea la colección mientras el proceso vive, así que la segunda réplica seguiría viendo
    /// una compensación como <i>pendiente</i> después de que la primera la ejecutó y la escribió:
    /// el arriendo evitaría que las dos la hicieran <b>a la vez</b>, y la segunda la volvería a
    /// hacer un minuto después leyendo su propia copia. Es la forma exacta del defecto #82 —el
    /// caché tapando el disco— aplicada a la decisión de si hay trabajo.</para>
    ///
    /// <para><b>Y por eso se llama antes de compensar, no en cada lectura.</b> Releer el fichero
    /// en cada <c>Find</c> pagaría el disco en todas las peticiones para resolver algo que sólo
    /// ocurre en el barrido.</para>
    /// </remarks>
    void Invalidate();
}

/// <summary>El almacén por defecto: un documento por saga, más el índice de las VIVAS.</summary>
/// <remarks>
/// <para><b>El índice existe porque el barrido pagaba por la HISTORIA y no por el trabajo</b>
/// (#130). Desde el #112 hay un fichero por documento, así que cada vuelta del barrido leía y
/// deserializaba <b>cada saga que hubiera existido jamás</b> para encontrar el puñado que tiene
/// algo que deshacer. Medido con las clases reales: <b>41 ms</b> con 100 sagas, <b>341 ms</b> con
/// 10 000 y <b>1 142 ms</b> con 50 000 —lineal, ≈23 µs por saga— y el barrido lo levantan los
/// CUATRO orquestadores cada 60 s. Cada compra que termina bien hace todas las vueltas futuras un
/// poco más lentas, para siempre. Con el índice el coste pasa a ser del tamaño de lo VIVO, que es
/// un puñado.</para>
///
/// <para><b>Lo que NO se hace, y es la mitad que importa: no se poda NADA.</b> El instinto es
/// borrar lo terminal, y duplicaría cobros: este almacén es también el libro de idempotencia
/// —<c>SagaEngine.Abrir</c> resuelve la llave con <c>Find</c>— así que una saga que deja de
/// encontrarse hace que el siguiente reintento abra una nueva y vuelva a cobrar (defecto #41).
/// </para>
///
/// <para><b>Y la salida que el #130 recomendaba —partir el libro de idempotencia de la saga—
/// tiene un modo de fallo que no estaba nombrado: la saga ES el recibo.</b> Los cuatro flujos
/// hacen <c>if (slot.Reusar is not null) return Result.Ok(slot.Reusar)</c>, o sea que un
/// reintento de una compra ya cerrada se contesta <b>con la saga entera</b> —su pedido, su cobro,
/// sus entradas—. Un libro de <c>llave → estado</c> sabría decir «esto ya pasó» y no podría decir
/// QUÉ pasó, así que el duplicado se iría sin su acuse. Por eso el cuerpo se queda, y con él el
/// crecimiento en disco: ≈0,6 KiB por saga es lo que cuesta poder contestar un reintento.</para>
///
/// <para><b>El índice es ADITIVO, y ésa es la propiedad que lo hace seguro.</b> No mueve ni borra
/// un solo documento de <c>sagas/</c>, así que una versión anterior —el despliegue tiene vuelta
/// atrás automática, ADR 0133— lo ignora y se comporta exactamente como hoy. Un índice que
/// archivara lo terminal sería más barato en disco y dejaría a esa versión anterior sin encontrar
/// las llaves: el cobro doble, por la puerta de atrás.</para>
///
/// <para><b>Fallar hacia el lado que no pierde trabajo.</b> Se MARCA antes de escribir una saga
/// viva y se DESMARCA después de escribir una terminal, así que una caída a mitad deja una marca
/// de más —que el barrido descubre y limpia— y nunca una de menos, que sería una compensación
/// que nadie vuelve a mirar. Y si el índice no está, se reconstruye del disco; mientras no se
/// pueda, se recorre todo, que es el comportamiento de hoy. <b>El peor caso es el de antes, nunca
/// uno peor.</b></para>
/// </remarks>
public sealed class FileSystemSagaStore<TSaga> : ISagaStore<TSaga> where TSaga : class, ISaga
{
    /// <summary>
    /// Los estados que el barrido puede tener que atender. El índice lleva exactamente éstos.
    /// </summary>
    /// <remarks>
    /// Se DERIVA de las dos consultas del barrido en vez de escribirse: <c>Running</c> es la del
    /// abandono (#29) y <c>Compensating</c>/<c>CompensationFailed</c> son <c>IsUnwinding()</c>.
    /// <c>Completed</c> y <c>Compensated</c> son finales y el barrido no los mira nunca — y
    /// <c>Compensated</c> además es el único que <c>Abrir</c> deja reintentar, sobre una saga
    /// NUEVA, así que tampoco vuelve a estar viva.
    /// </remarks>
    private static bool EstaViva(TSaga saga)
        => saga.Status is SagaStatus.Running or SagaStatus.Compensating or SagaStatus.CompensationFailed;

    private readonly JsonCollectionStore<TSaga> _store;
    private readonly string _indice;
    private readonly object _gate = new();

    public FileSystemSagaStore(IOptions<SagaStorageOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _store = new JsonCollectionStore<TSaga>(options.Value.Root, "sagas", s => s.Id);
        _indice = Path.Combine(options.Value.Root, "sagas-vivas");
    }

    /// <summary>
    /// Le vacía el caché al almacén, para que la siguiente lectura baje al fichero.
    /// </summary>
    /// <remarks>
    /// <b>Se le pide al almacén y no se cambia el almacén por otro</b>, aunque abrir uno nuevo
    /// sobre el mismo fichero también vaciaría el caché —es lo que hace el gate del defecto #82—.
    /// Con dos instancias vivas a la vez, la que quedó atrás conserva su mapa y el primer
    /// <c>Put</c> suyo lo escribe ENTERO encima: borraría lo que la otra hubiera guardado en
    /// medio. Una sola instancia, y el vaciado dentro de su propio <c>lock</c>.
    /// </remarks>
    public void Invalidate() => _store.Invalidate();

    public TSaga? Find(string id) => _store.Find(id);

    // IsUnwinding y no solo "tiene pendientes": una operación sana en curso lleva sus
    // compensaciones ARMADAS, y contarlas como pendientes llenaría la vista de operación de casos
    // que no tienen ningún problema — y haría que el barrido las ejecutara.
    public IReadOnlyList<TSaga> WithPendingCompensations()
        => Ordenar(Vivas().Where(s => s.IsUnwinding() && s.Compensations.Any(c => c.IsPending)));

    // Solo `Running`: `Compensating` ya lo barre la consulta de arriba, y los estados finales no
    // tienen nada que abandonar. Una saga que ya se está deshaciendo no se "abandona" otra vez.
    public IReadOnlyList<TSaga> StartedBefore(DateTimeOffset limite)
        => Ordenar(Vivas().Where(s => s.Status == SagaStatus.Running && s.StartedAtUtc < limite));

    private static IReadOnlyList<TSaga> Ordenar(IEnumerable<TSaga> sagas)
        => sagas
            .OrderBy(s => s.StartedAtUtc)
            .ThenBy(s => s.Id, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// Guarda la saga y deja el índice diciendo la verdad sobre ella.
    /// </summary>
    /// <remarks>
    /// <b>El ORDEN es la garantía, no un detalle.</b> Marcar antes de escribir una saga viva y
    /// desmarcar después de escribir una terminal hace que toda caída a mitad deje una marca de
    /// MÁS. Al revés —escribir y luego marcar— una caída dejaría viva una saga que el índice no
    /// nombra, o sea una compensación pendiente que el barrido no vuelve a mirar nunca: el
    /// silencio que este repo ya pagó cuatro veces. Una marca de más se limpia sola en la
    /// siguiente vuelta; una de menos no la descubre nadie.
    /// </remarks>
    public void Put(TSaga saga)
    {
        ArgumentNullException.ThrowIfNull(saga);

        if (EstaViva(saga)) Marcar(saga.Id);
        _store.Put(saga);
        if (!EstaViva(saga)) Desmarcar(saga.Id);
    }

    // ── El índice de las vivas ───────────────────────────────────────────────

    /// <summary>
    /// Las sagas que el barrido puede tener que atender, leídas por el índice.
    /// </summary>
    /// <remarks>
    /// <para><b>Se limpia lo que sobra al pasar, y ahí está el auto-sanado.</b> Una marca cuya
    /// saga ya no está viva —o que no existe— es el residuo de una caída entre los dos pasos de
    /// <see cref="Put"/>, y quitarla acá cuesta un <c>Delete</c> sobre un fichero que ya se
    /// había leído. Sin esto, el residuo se queda para siempre y el índice deja de valer.</para>
    ///
    /// <para><b>Y si el índice no está, se recorre TODO y se reconstruye.</b> Ése es el caso del
    /// primer arranque tras este cambio y el de un volumen restaurado: el peor caso es el
    /// comportamiento de antes, nunca uno peor.</para>
    /// </remarks>
    private IReadOnlyList<TSaga> Vivas()
    {
        if (!Directory.Exists(_indice)) return Reconstruir();

        var vivas = new List<TSaga>();
        foreach (var marca in Directory.EnumerateFiles(_indice, "*.id"))
        {
            string id;
            try { id = File.ReadAllText(marca); }
            catch (IOException) { continue; }

            var saga = _store.Find(id);
            if (saga is not null && EstaViva(saga)) { vivas.Add(saga); continue; }

            Borrar(marca);
        }

        return vivas;
    }

    /// <summary>
    /// Recorre el almacén entero, escribe el índice de una vez y devuelve lo vivo.
    /// </summary>
    /// <remarks>
    /// <b>Se construye aparte y se mueve de un golpe</b>, como el arriendo de #34: un índice a
    /// medias —una caída a mitad del recorrido— sería peor que ninguno, porque el siguiente
    /// barrido lo encontraría existiendo y se creería su lista corta. Si el movimiento no sale,
    /// se devuelve lo medido igual y se reintenta en la vuelta siguiente: preferible a fallar una
    /// vuelta del barrido por no poder escribir un caché.
    /// </remarks>
    private IReadOnlyList<TSaga> Reconstruir()
    {
        var vivas = _store.Where(EstaViva);

        var tmp = _indice + "." + Guid.NewGuid().ToString("n") + ".tmp";
        try
        {
            Directory.CreateDirectory(tmp);
            foreach (var saga in vivas)
            {
                File.WriteAllText(Path.Combine(tmp, Huella(saga.Id) + ".id"), saga.Id);
            }

            lock (_gate)
            {
                if (!Directory.Exists(_indice)) Directory.Move(tmp, _indice);
            }
        }
        catch (IOException) { /* se vuelve a intentar en la próxima vuelta */ }
        catch (UnauthorizedAccessException) { /* idem */ }
        finally
        {
            if (Directory.Exists(tmp))
            {
                try { Directory.Delete(tmp, recursive: true); } catch (IOException) { /* temporal */ }
            }
        }

        return vivas;
    }

    /// <summary>
    /// Crea la marca. <b>No es lectura-modificación-escritura</b>, así que dos réplicas que
    /// marquen la misma saga escriben el mismo fichero con el mismo contenido y ninguna pierde lo
    /// de la otra — que es justo lo que un índice de un solo documento no podría prometer sin el
    /// turno de escritura que los orquestadores no tienen (#112).
    /// </summary>
    private void Marcar(string id)
    {
        try
        {
            Directory.CreateDirectory(_indice);
            File.WriteAllText(Path.Combine(_indice, Huella(id) + ".id"), id);
        }
        catch (IOException) { /* el barrido lo reconstruye */ }
        catch (UnauthorizedAccessException) { /* idem */ }
    }

    private void Desmarcar(string id) => Borrar(Path.Combine(_indice, Huella(id) + ".id"));

    private static void Borrar(string ruta)
    {
        try { if (File.Exists(ruta)) File.Delete(ruta); }
        catch (IOException) { /* lo vuelve a intentar la vuelta siguiente */ }
        catch (UnauthorizedAccessException) { /* idem */ }
    }

    /// <summary>
    /// El nombre de la marca es la HUELLA del identificador, por la misma razón que en
    /// <c>JsonCollectionStore</c>: una llave la escribe quien llama y puede traer barras.
    /// </summary>
    private static string Huella(string id)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id))).ToLowerInvariant();
}
