using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;

namespace Synergos.Api.Sessions;

/// <summary>Una búsqueda ejecutada, tal como la reporta un origen.</summary>
/// <param name="Query">Texto buscado. Se normaliza a minúsculas y sin espacios sobrantes.</param>
/// <param name="ResultCount">Cuántos resultados devolvió.</param>
/// <param name="ElapsedMs">Cuánto tardó, en milisegundos.</param>
/// <param name="AtUtc">Cuándo ocurrió.</param>
public sealed record SearchEvent(string Query, int ResultCount, long ElapsedMs, DateTime AtUtc);

/// <summary>Un query agregado dentro de una ventana temporal.</summary>
public sealed record QueryStat(string Query, int Count, int LastResultCount, DateTime LastSeenUtc);

/// <summary>
/// El almacén de búsquedas del servicio de sesión: JSONL append-only, un fichero por día.
/// </summary>
/// <remarks>
/// <para><b>Por qué JSONL y no una base.</b> El patrón de acceso es escribir mucho y leer poco,
/// siempre por ventana de fechas. Un fichero por día hace que consultar "los últimos 7 días"
/// abra siete ficheros y ni mire el resto, y que la retención sea borrar ficheros viejos en vez
/// de un DELETE con índice. Una base se justifica cuando haya consultas que esto no pueda
/// contestar —por sesión, por usuario— y ese día se cambia esta clase y nada más: el contrato
/// del servicio es HTTP.</para>
///
/// <para><b>Escribir nunca puede reventar.</b> Este servicio existe para que el CMS no cargue
/// con la analítica; si además pudiera tumbarlo, habría empeorado el problema. Un fallo de
/// escritura se traga y se cuenta — nunca sube.</para>
///
/// <para><b>Una línea corrupta no invalida el día.</b> Se saltan las que no parsean. Un proceso
/// muerto a media escritura deja una línea truncada, y perder una búsqueda es aceptable;
/// perder el fichero entero, no.</para>
/// </remarks>
public sealed class SearchEventStore
{
    private readonly string _root;
    private readonly ILogger<SearchEventStore> _log;
    private readonly ConcurrentDictionary<string, object> _dayLocks = new(StringComparer.Ordinal);

    private long _written;
    private long _dropped;

    public SearchEventStore(IConfiguration config, ILogger<SearchEventStore> log)
    {
        _log = log;
        var configured = config["Sessions:Storage:Root"];
        _root = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(AppContext.BaseDirectory, "data", "search-events")
            : configured;
        Directory.CreateDirectory(_root);
        _log.LogInformation("SearchEventStore en {Root}", _root);
    }

    /// <summary>Eventos escritos y descartados desde el arranque — los expone /health.</summary>
    public (long Written, long Dropped) Counters => (Interlocked.Read(ref _written), Interlocked.Read(ref _dropped));

    /// <summary>
    /// Persiste un evento. No lanza NUNCA: devuelve si se pudo o no.
    /// </summary>
    public bool Record(SearchEvent evt)
    {
        var query = Normalize(evt.Query);
        if (query.Length == 0)
        {
            // Una búsqueda vacía no es una búsqueda. No es un error del llamador: es ruido
            // que no debe ensuciar el "top queries".
            Interlocked.Increment(ref _dropped);
            return false;
        }

        try
        {
            var line = JsonSerializer.Serialize(evt with { Query = query });
            var path = PathFor(evt.AtUtc);
            // Un lock por DÍA y no uno global: dos días distintos no compiten, y el caso
            // normal —todo el tráfico en el día de hoy— sigue serializado, que es lo que
            // mantiene íntegra cada línea.
            lock (_dayLocks.GetOrAdd(path, _ => new object()))
            {
                File.AppendAllText(path, line + Environment.NewLine);
            }
            Interlocked.Increment(ref _written);
            return true;
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _dropped);
            _log.LogWarning(ex, "No se pudo persistir una búsqueda; se descarta.");
            return false;
        }
    }

    /// <summary>Top de queries por número de veces buscados en la ventana.</summary>
    public IReadOnlyList<QueryStat> TopQueries(DateTime fromUtc, DateTime toUtc, int limit)
        => Aggregate(fromUtc, toUtc, limit, noResultsOnly: false);

    /// <summary>
    /// Top de queries que terminaron SIN resultados.
    /// </summary>
    /// <remarks>
    /// "Sin resultados" se decide por el ÚLTIMO intento, no por el primero: si alguien buscó
    /// algo, no lo encontró, y después se publicó el contenido, ese query ya no es un hueco
    /// del catálogo. Lo contrario dejaría en la lista cosas ya resueltas.
    /// </remarks>
    public IReadOnlyList<QueryStat> TopNoResultQueries(DateTime fromUtc, DateTime toUtc, int limit)
        => Aggregate(fromUtc, toUtc, limit, noResultsOnly: true);

    private IReadOnlyList<QueryStat> Aggregate(DateTime fromUtc, DateTime toUtc, int limit, bool noResultsOnly)
    {
        if (limit <= 0 || toUtc < fromUtc) return Array.Empty<QueryStat>();

        var acc = new Dictionary<string, (int Count, int LastResultCount, DateTime LastSeen)>(StringComparer.Ordinal);

        for (var day = fromUtc.Date; day <= toUtc.Date; day = day.AddDays(1))
        {
            var path = PathFor(day);
            if (!File.Exists(path)) continue;

            IEnumerable<string> lines;
            try { lines = File.ReadLines(path); }
            catch (Exception ex) { _log.LogWarning(ex, "No se pudo leer {Path}; se omite.", path); continue; }

            foreach (var line in lines)
            {
                SearchEvent? evt;
                try { evt = JsonSerializer.Deserialize<SearchEvent>(line); }
                catch (JsonException) { continue; }   // línea truncada: se salta, no invalida el día
                if (evt is null || evt.AtUtc < fromUtc || evt.AtUtc > toUtc) continue;

                var key = Normalize(evt.Query);
                if (key.Length == 0) continue;

                if (acc.TryGetValue(key, out var cur))
                {
                    // LastResultCount y LastSeen son del intento MÁS RECIENTE, no del último
                    // leído: el orden dentro del fichero es cronológico, pero entre ficheros y
                    // con relojes de varios orígenes no está garantizado.
                    acc[key] = evt.AtUtc >= cur.LastSeen
                        ? (cur.Count + 1, evt.ResultCount, evt.AtUtc)
                        : (cur.Count + 1, cur.LastResultCount, cur.LastSeen);
                }
                else
                {
                    acc[key] = (1, evt.ResultCount, evt.AtUtc);
                }
            }
        }

        return acc
            .Where(kv => !noResultsOnly || kv.Value.LastResultCount == 0)
            .OrderByDescending(kv => kv.Value.Count)
            // Desempate alfabético: sin él, dos peticiones idénticas pueden devolver listas
            // distintas y el dashboard "parpadea" sin que nada haya cambiado.
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Take(limit)
            .Select(kv => new QueryStat(kv.Key, kv.Value.Count, kv.Value.LastResultCount, kv.Value.LastSeen))
            .ToList();
    }

    /// <summary>Borra los ficheros anteriores a la retención. Devuelve cuántos borró.</summary>
    public int Purge(DateTime olderThanUtc)
    {
        var purged = 0;
        foreach (var file in Directory.EnumerateFiles(_root, "*.jsonl"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (!DateTime.TryParseExact(name, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var day))
            {
                continue;   // un fichero que no es del store no se toca
            }
            if (day.Date >= olderThanUtc.Date) continue;
            try { File.Delete(file); purged++; }
            catch (Exception ex) { _log.LogWarning(ex, "No se pudo purgar {Path}.", file); }
        }
        return purged;
    }

    private string PathFor(DateTime utc)
        => Path.Combine(_root, utc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".jsonl");

    private static string Normalize(string? raw)
        => raw?.Trim().ToLowerInvariant() ?? string.Empty;
}
