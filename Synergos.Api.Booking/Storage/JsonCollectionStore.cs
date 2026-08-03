using System.Text.Json;

namespace Synergos.Api.Booking.Storage;

/// <summary>
/// Una colección de entidades por identificador, persistida como un JSON por colección.
/// </summary>
/// <remarks>
/// <para><b>Por qué un fichero y no una base.</b> El volumen de esta capacidad en v1 no la
/// justifica, y el contrato del servicio es HTTP: el día que haga falta una base se cambia esta
/// clase y nadie afuera se entera. Lo que <b>no</b> se negocia es que este almacén es de Booking
/// y <b>nadie más lo lee</b> — ni un <c>JOIN</c>, ni un fichero compartido (doc 07 §6).</para>
///
/// <para><b>La limitación, dicha de frente:</b> el <c>lock</c> es de proceso. Dos instancias de
/// esta API sobre el mismo directorio pueden pisarse. Es aceptable mientras se despliegue una
/// sola —que es el caso hoy— y es la primera razón por la que habría que cambiar de almacén, no
/// un detalle que se descubra en producción.</para>
///
/// <para>Se escribe a un temporal y se mueve encima: un proceso muerto a media escritura deja el
/// fichero anterior intacto en vez de uno truncado. Con escritura directa, una caída en el
/// momento justo pierde la colección entera.</para>
/// </remarks>
internal sealed class JsonCollectionStore<T> where T : class
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };

    private readonly string _path;
    private readonly Func<T, string> _key;
    private readonly object _gate = new();
    private Dictionary<string, T>? _cache;

    public JsonCollectionStore(string root, string name, Func<T, string> key)
    {
        Directory.CreateDirectory(root);
        _path = Path.Combine(root, $"{name}.json");
        _key = key;
    }

    public T? Find(string id)
    {
        lock (_gate) { return Load().TryGetValue(id, out var found) ? found : null; }
    }

    public IReadOnlyList<T> All()
    {
        lock (_gate) { return Load().Values.ToList(); }
    }

    public IReadOnlyList<T> Where(Func<T, bool> predicate)
    {
        lock (_gate) { return Load().Values.Where(predicate).ToList(); }
    }

    /// <summary>Inserta o reemplaza por identificador.</summary>
    public void Put(T entity)
    {
        lock (_gate)
        {
            var map = Load();
            map[_key(entity)] = entity;
            Flush(map);
        }
    }

    private Dictionary<string, T> Load()
    {
        if (_cache is not null) return _cache;

        if (!File.Exists(_path))
        {
            return _cache = new Dictionary<string, T>(StringComparer.Ordinal);
        }

        var raw = File.ReadAllText(_path);
        var items = string.IsNullOrWhiteSpace(raw)
            ? new List<T>()
            : JsonSerializer.Deserialize<List<T>>(raw, Json) ?? new List<T>();

        return _cache = items.ToDictionary(_key, StringComparer.Ordinal);
    }

    private void Flush(Dictionary<string, T> map)
    {
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(map.Values.ToList(), Json));
        File.Move(tmp, _path, overwrite: true);
        _cache = map;
    }
}
