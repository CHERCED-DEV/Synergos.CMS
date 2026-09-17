using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Synergos.Shared;

/// <summary>
/// Una colección de entidades por identificador, persistida como <b>un fichero por documento</b>.
/// </summary>
/// <remarks>
/// <para><b>Por qué vive en Shared.</b> Nació dentro de <c>Api.Booking</c> con la nota de que se
/// promovería cuando una segunda capacidad lo necesitara. Con seis necesitándolo, copiarlo seis
/// veces es exactamente lo que este proyecto existe para evitar — y las decisiones sutiles de
/// abajo (escritura atómica, huella del identificador, migración) se pierden una por copia. No
/// menciona ningún sustantivo del negocio: es un diccionario persistido.</para>
///
/// <para><b>Por qué un fichero y no una base.</b> El volumen de estas capacidades en v1 no la
/// justifica, y el contrato de cada servicio es HTTP: el día que haga falta una base se cambia
/// esta clase y nadie afuera se entera. Lo que <b>no</b> se negocia es que cada almacén es de
/// UNA capacidad y <b>nadie más lo lee</b> — ni un <c>JOIN</c>, ni un fichero compartido
/// (doc 07 §6).</para>
///
/// <para><b>Por qué un fichero POR DOCUMENTO, y qué cerraba eso (#112).</b> Antes era un JSON por
/// colección y <c>Put</c> lo reescribía ENTERO desde un caché de proceso. Dos réplicas no se
/// pisaban «un documento»: la que escribía segunda <b>borraba la colección de la primera</b> — sin
/// excepción y sin log. Y el caché es lo que lo hacía invisible: mientras el proceso vivía, todas
/// sus lecturas salían de memoria, así que una réplica nunca veía lo que la otra escribió ni
/// descubría que se lo había comido. La granularidad de la escritura <i>es</i> la granularidad de
/// la protección; el árbol del CMS ya lo había aprendido
/// (<c>FileSystemJsonEntityStore</c>: «1 archivo por entidad»).</para>
///
/// <para><b>Lo que esto NO cierra, y por eso existe <see cref="StoreWriteGate"/>.</b> Leer,
/// decidir y escribir son tres pasos con la regla del negocio en medio —«¿queda cupo?», «¿ya se
/// capturó?»—, así que ningún <c>Put</c> puede saber que lo que escribe se decidió sobre una
/// lectura que ya envejeció. Dos devoluciones de existencias leen 10, escriben 12 y 13, y la
/// segunda pisa a la primera. Eso es exclusión, no granularidad, y vive en el borde.</para>
///
/// <para><b>El precio, dicho de frente:</b> listar cuesta N lecturas —no hay índice, y
/// <see cref="Where"/> recorre el directorio entero—, y no hay caché, así que cada
/// <see cref="Find"/> baja al disco. Es el intercambio correcto mientras las colecciones sean
/// chicas y las escrituras frecuentes (la bitácora AÑADE y consulta por ventana), y es
/// <b>medible</b>: el día que una colección crezca hasta que listar duela, ése es el disparador
/// para cambiar de almacén. No antes.</para>
///
/// <para>Cada documento se escribe a un temporal y se mueve encima: un proceso muerto a media
/// escritura deja el fichero anterior intacto en vez de uno truncado, y quien lee ve la versión de
/// antes o la de después, nunca media.</para>
///
/// <para><b>Una lectura rota NO se traga.</b> Un JSON que no deserializa propaga, que es lo que
/// convierte una restauración inservible en un 500 ruidoso en vez de en una capacidad con menos
/// registros — la lección del #82 y del ensayo de restauración del #31.</para>
/// </remarks>
public sealed class JsonCollectionStore<T> where T : class
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        // Un Actor se ESCRIBE bien de serie y no se puede LEER: sus roles son un
        // IReadOnlySet<string> y System.Text.Json no sabe instanciar una interfaz de conjunto.
        // Sin esto, la bitácora guardaba sus asientos y devolvía 500 en toda lectura en cuanto el
        // proceso se reiniciaba — y no antes, porque el caché de acá tapaba el viaje al disco
        // mientras el proceso vivía (defecto #82).
        Converters = { new ActorJsonConverter() },
    };

    /// <summary>Marca de que el array anterior ya se repartió en documentos.</summary>
    /// <remarks>
    /// Sin extensión <c>.json</c> a propósito: comparte directorio con los documentos y el
    /// recorrido de <see cref="All"/> filtra por extensión.
    /// </remarks>
    private const string Marca = ".migrado";

    private readonly string _legado;
    private readonly string _dir;
    private readonly Func<T, string> _key;
    private readonly object _gate = new();
    private bool _migrado;

    public JsonCollectionStore(string root, string name, Func<T, string> key)
    {
        Directory.CreateDirectory(root);
        _legado = Path.Combine(root, $"{name}.json");
        _dir = Path.Combine(root, name);
        _key = key;
    }

    public T? Find(string id)
    {
        Migrar();
        return Leer(Ruta(id));
    }

    public IReadOnlyList<T> All()
    {
        Migrar();
        if (!Directory.Exists(_dir)) return Array.Empty<T>();

        var leidos = new List<(string Clave, T Entidad)>();
        foreach (var fichero in Directory.EnumerateFiles(_dir, "*.json"))
        {
            if (Leer(fichero) is { } entidad) leidos.Add((_key(entidad), entidad));
        }

        // Un directorio NO tiene orden, así que el almacén deja de fingir que lo tiene y devuelve
        // por identificador. Da igual cuál sea mientras sea el MISMO en las dos réplicas: sin
        // esto, dos consultas iguales contra réplicas distintas devuelven órdenes distintos y una
        // página 2 se salta filas. Quien necesita orden de negocio ya lo dice (por fecha, por
        // puntaje); esto es sólo el desempate de abajo.
        return leidos.OrderBy(x => x.Clave, StringComparer.Ordinal).Select(x => x.Entidad).ToList();
    }

    public IReadOnlyList<T> Where(Func<T, bool> predicate)
        => All().Where(predicate).ToList();

    /// <summary>Inserta o reemplaza por identificador.</summary>
    /// <remarks>
    /// Sólo toca SU fichero: dos réplicas que guardan documentos distintos ya no se ven. Dos que
    /// guardan el MISMO se serializan en el borde (<see cref="StoreWriteGate"/>) — acá lo único
    /// que se garantiza es que el que llegue último gane entero, no a medias.
    /// </remarks>
    public void Put(T entity)
    {
        Migrar();
        Escribir(_key(entity), entity);
    }

    /// <summary>
    /// No hace nada: ya no hay foto que tirar.
    /// </summary>
    /// <remarks>
    /// <para>Existía porque el almacén cacheaba la colección entera y quien iba a decidir algo
    /// tenía que desconfiar de su copia — hoy toda lectura baja al disco, así que llamarla sigue
    /// siendo correcto y ya no hace falta (#112).</para>
    ///
    /// <para><b>Se conserva porque la declara la costura de arriba</b> (<c>ISagaStore</c>, que
    /// tiene implementaciones de prueba), no porque haga algo. Quitarla es un cambio de esa
    /// costura y de sus dobles, y ése es su disparador.</para>
    /// </remarks>
    public void Invalidate()
    {
    }

    // ── Disco ────────────────────────────────────────────────────────────────

    /// <summary>
    /// El nombre del fichero es la HUELLA del identificador, no el identificador.
    /// </summary>
    /// <remarks>
    /// La clave del libro de idempotencia es <c>{ámbito}|{llave del llamador}</c> y la llave la
    /// escribe quien llama — hasta 128 caracteres cualesquiera, barras incluidas. Cambiar los
    /// caracteres prohibidos por un guión —que es lo que hace el almacén del árbol del CMS, donde
    /// las claves las genera el servidor— colisionaría <c>a/b</c> con <c>a-b</c>: la segunda llave
    /// de idempotencia devolvería el resultado de la primera, que es exactamente lo que la llave
    /// existe para evitar. Una huella no colisiona y no puede salirse del directorio.
    /// </remarks>
    private string Ruta(string id) => Path.Combine(_dir, Huella(id) + ".json");

    private static string Huella(string clave)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(clave))).ToLowerInvariant();

    private static T? Leer(string ruta)
    {
        if (!File.Exists(ruta)) return null;
        var raw = File.ReadAllText(ruta);
        return string.IsNullOrWhiteSpace(raw) ? null : JsonSerializer.Deserialize<T>(raw, Json);
    }

    private void Escribir(string clave, T entidad)
    {
        var ruta = Ruta(clave);
        Directory.CreateDirectory(_dir);

        // Temporal ÚNICO: dos réplicas escribiendo el mismo documento no se pisan el temporal, que
        // sería la forma de dejar en el disco un fichero mitad de una y mitad de la otra.
        var tmp = ruta + "." + Guid.NewGuid().ToString("n") + ".tmp";
        try
        {
            File.WriteAllText(tmp, JsonSerializer.Serialize(entidad, Json));
            lock (_gate) { File.Move(tmp, ruta, overwrite: true); }
        }
        finally
        {
            if (File.Exists(tmp))
            {
                try { File.Delete(tmp); } catch (IOException) { /* el barrido del volumen lo alcanza */ }
            }
        }
    }

    // ── Lo anterior ──────────────────────────────────────────────────────────

    /// <summary>
    /// Reparte el array anterior en documentos, una sola vez.
    /// </summary>
    /// <remarks>
    /// <para><b>Se migra al primer uso y no en el arranque</b>: la capacidad que no toque una
    /// colección no paga por ella, y una migración en boot es I/O en el arranque (ADR 0013).</para>
    ///
    /// <para><b>El fichero anterior NO se borra.</b> El despliegue tiene vuelta atrás automática
    /// (ADR 0133): si la migración se llevara el <c>{nombre}.json</c>, la versión anterior
    /// arrancaría sobre un directorio que no conoce y serviría la capacidad <b>vacía</b>, en
    /// silencio. El precio es un fichero muerto por colección, que es el barato de los dos.</para>
    ///
    /// <para><b>Y por eso hace falta la marca.</b> Sin ella, el segundo arranque volcaría el array
    /// antiguo encima de documentos ya editados y dejaría la capacidad <i>coherente y
    /// equivocada</i> — el fallo silencioso de toda migración que se repite. La marca vive en el
    /// directorio compartido, así que la réplica que no migró también la ve; y aun así no se
    /// sobreescribe un documento que ya exista, que es el cinturón además del tirante.</para>
    ///
    /// <para><b>Lo que se lee al revés</b>: una versión anterior lee el <c>{nombre}.json</c> tal
    /// como lo dejó, o sea el estado del momento de migrar. Lo escrito después NO viaja hacia
    /// atrás — la vuelta atrás recupera el servicio, no los datos de mientras.</para>
    /// </remarks>
    private void Migrar()
    {
        if (_migrado) return;
        lock (_gate)
        {
            if (_migrado) return;
            _migrado = true;

            if (!File.Exists(_legado)) return;
            if (File.Exists(Path.Combine(_dir, Marca))) return;

            var raw = File.ReadAllText(_legado);
            var items = string.IsNullOrWhiteSpace(raw)
                ? new List<T>()
                : JsonSerializer.Deserialize<List<T>>(raw, Json) ?? new List<T>();

            foreach (var item in items)
            {
                var clave = _key(item);
                if (File.Exists(Ruta(clave))) continue;
                Escribir(clave, item);
            }

            Directory.CreateDirectory(_dir);
            File.WriteAllText(
                Path.Combine(_dir, Marca),
                $"{DateTimeOffset.UtcNow:O} — repartido desde {Path.GetFileName(_legado)} (#112).\n");
        }
    }
}
