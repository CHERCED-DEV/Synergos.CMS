using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// <see cref="ISearchAnalyticsStore"/> que persiste JSONL append-only en
/// <c>App_Data/syn-search-analytics/{yyyy-MM-dd}.jsonl</c> — un evento por línea, un archivo
/// por día.
/// </summary>
/// <remarks>
/// <para><b>Por qué reemplazó al almacén en memoria.</b> Aquel tenía tres problemas a la vez, y
/// los tres los cierra escribir a disco:</para>
/// <list type="number">
///   <item>la analítica se perdía en cada reinicio, así que el tablero se reseteaba en cada
///     despliegue y no se podía comparar una semana con otra;</item>
///   <item>era un diccionario <b>sin tope</b> indexado por el texto que escribe el visitante:
///     un rastreador —o alguien con mala intención— lo inflaba sin límite;</item>
///   <item>y ese texto no expiraba nunca, porque la única retención que existe
///     (<c>SearchAnalyticsRetentionPolicy</c>) mira disco.</item>
/// </list>
///
/// <para><b>El costo que esto acepta, y hay que decirlo.</b> Lo que se guarda son
/// <b>consultas escritas por visitantes</b>: pueden traer un nombre, una dirección, un número
/// de caso. Antes eso vivía en memoria y moría con el proceso; ahora queda en disco. La
/// contrapartida es que ahora <b>sí</b> caduca —<c>Synergos:Retention:SearchAnalyticsRetentionDays</c>,
/// 90 días por defecto— y esa política pasa de decorativa a ser la que sostiene la promesa.
/// Bajar ese número es la palanca si 90 días es mucho para este sitio.</para>
///
/// <para><b>Cuando llegue la API de sesión</b>, esta clase se reemplaza por un adapter contra
/// ella sin tocar a nadie más: <see cref="ISearchAnalyticsStore"/> ya es la costura, y sus dos
/// consumidores —<c>SearchController</c> y <c>AdminController</c>— hablan solo con la interfaz.
/// Es el caso de uso para el que el seam se declaró.</para>
///
/// <para><b>Escribir NUNCA lanza.</b> <see cref="Record"/> corre en el camino de una búsqueda
/// del visitante: un disco lleno puede costar una métrica, no la búsqueda.</para>
///
/// <para><b>Leer agrega sobre la marcha.</b> Cada consulta al tablero lee los archivos del día
/// que caen en la ventana pedida. Con el tablero pidiendo 7 días son 7 archivos; si el volumen
/// crece hasta que eso pese, el siguiente paso es el adapter contra la API de sesión, no un
/// caché acá.</para>
/// </remarks>
public sealed class FileSystemSearchAnalyticsStore : ISearchAnalyticsStore
{
    /// <summary>
    /// El directorio. <b>Tiene que coincidir</b> con el de
    /// <c>SearchAnalyticsRetentionPolicy</c>: si se separan, se escribe en un sitio y se purga
    /// en otro, y nadie se entera hasta que el disco se llena.
    /// </summary>
    internal const string DirectoryName = "syn-search-analytics";

    /// <summary>El nombre del archivo ES la fecha, que es de donde la retención la lee.</summary>
    internal const string FileDateFormat = "yyyy-MM-dd";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    private readonly IHostEnvironment _hostEnvironment;
    private readonly ILogger<FileSystemSearchAnalyticsStore> _logger;
    private readonly Func<DateTime> _now;
    private readonly object _writeLock = new();

    public FileSystemSearchAnalyticsStore(
        IHostEnvironment hostEnvironment,
        ILogger<FileSystemSearchAnalyticsStore> logger,
        Func<DateTime>? now = null)
    {
        _hostEnvironment = hostEnvironment;
        _logger = logger;
        _now = now ?? (() => DateTime.UtcNow);
    }

    public void Record(string query, int resultCount, long elapsedMilliseconds)
    {
        var normalized = NormalizeQuery(query);
        if (normalized.Length == 0)
        {
            return;
        }

        try
        {
            var at = _now();
            var line = JsonSerializer.Serialize(
                new SearchQueryEvent(normalized, resultCount, elapsedMilliseconds, at),
                JsonOptions) + Environment.NewLine;
            var path = ResolvePath(at);

            lock (_writeLock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.AppendAllText(path, line, Encoding.UTF8);
            }
        }
        catch (Exception ex)
        {
            // Una búsqueda que funcionó no puede fallar porque su métrica no se pudo guardar.
            _logger.LogWarning(ex, "Failed to persist search analytics for a query");
        }
    }

    public IReadOnlyList<SearchQueryStat> GetTopQueries(DateTime fromUtc, DateTime toUtc, int limit)
        => Top(fromUtc, toUtc, limit, onlyNoResults: false);

    public IReadOnlyList<SearchQueryStat> GetTopNoResultQueries(DateTime fromUtc, DateTime toUtc, int limit)
        => Top(fromUtc, toUtc, limit, onlyNoResults: true);

    private IReadOnlyList<SearchQueryStat> Top(DateTime fromUtc, DateTime toUtc, int limit, bool onlyNoResults)
    {
        var aggregates = AggregateWindow(fromUtc, toUtc).Values.AsEnumerable();

        if (onlyNoResults)
        {
            // "Sin resultados" es del ÚLTIMO intento, no de todos: una consulta que no
            // encontraba nada y ahora sí, dejó de ser un hueco de contenido.
            aggregates = aggregates.Where(a => a.LastResultCount == 0);
        }

        return aggregates
            // El desempate alfabético no es cosmético: sin él, dos consultas con el mismo
            // conteo se turnan entre recargas del tablero y parecen datos inestables.
            .OrderByDescending(a => a.Count)
            .ThenBy(a => a.Query, StringComparer.Ordinal)
            .Take(Math.Max(1, limit))
            .Select(a => new SearchQueryStat(a.Query, a.Count, a.LastResultCount, a.LastSeenUtc))
            .ToList();
    }

    private Dictionary<string, QueryAggregate> AggregateWindow(DateTime fromUtc, DateTime toUtc)
    {
        var result = new Dictionary<string, QueryAggregate>(StringComparer.Ordinal);
        foreach (var evt in ReadWindow(fromUtc, toUtc))
        {
            if (!result.TryGetValue(evt.Query, out var acc))
            {
                acc = new QueryAggregate(evt.Query);
                result[evt.Query] = acc;
            }

            acc.Count++;
            if (evt.AtUtc >= acc.LastSeenUtc)
            {
                acc.LastSeenUtc = evt.AtUtc;
                acc.LastResultCount = evt.ResultCount;
            }
        }

        return result;
    }

    /// <summary>
    /// Los eventos de la ventana. Se filtran los <b>archivos</b> por su fecha antes de abrirlos
    /// —recorrer día por día un rango que alguien pidió como «desde el año 1» abriría miles— y
    /// después cada evento por su marca de tiempo, que es la que da el corte fino.
    /// </summary>
    private IEnumerable<SearchQueryEvent> ReadWindow(DateTime fromUtc, DateTime toUtc)
    {
        var dir = Path.Combine(_hostEnvironment.ContentRootPath, "App_Data", DirectoryName);
        if (!Directory.Exists(dir))
        {
            yield break;
        }

        var firstDay = fromUtc.Date;
        var lastDay = toUtc.Date;

        foreach (var file in Directory.EnumerateFiles(dir, "*.jsonl").OrderBy(f => f, StringComparer.Ordinal))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (!DateTime.TryParseExact(
                    name, FileDateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
                || day < firstDay
                || day > lastDay)
            {
                continue;
            }

            string[] lines;
            try
            {
                lines = File.ReadAllLines(file, Encoding.UTF8);
            }
            catch (IOException ex)
            {
                // Un archivo que se está escribiendo justo ahora no puede tumbar el tablero.
                _logger.LogWarning(ex, "Failed to read search-analytics file {File}", file);
                continue;
            }

            foreach (var line in lines)
            {
                var evt = Parse(line);
                if (evt is not null && evt.AtUtc >= fromUtc && evt.AtUtc <= toUtc)
                {
                    yield return evt;
                }
            }
        }
    }

    /// <summary>
    /// Una línea ilegible se SALTA. Es un log append-only escrito por un proceso que puede
    /// morir a mitad de línea; que un renglón trunco borre el tablero entero sería peor que
    /// perder ese renglón.
    /// </summary>
    private static SearchQueryEvent? Parse(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        try
        {
            var evt = JsonSerializer.Deserialize<SearchQueryEvent>(line, JsonOptions);
            return string.IsNullOrEmpty(evt?.Query) ? null : evt;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private string ResolvePath(DateTime at) => Path.Combine(
        _hostEnvironment.ContentRootPath,
        "App_Data",
        DirectoryName,
        at.ToString(FileDateFormat, CultureInfo.InvariantCulture) + ".jsonl");

    /// <summary>Misma normalización que tenía el almacén en memoria: recortada y en minúsculas.</summary>
    private static string NormalizeQuery(string raw) =>
        string.IsNullOrWhiteSpace(raw) ? string.Empty : raw.Trim().ToLowerInvariant();

    private sealed class QueryAggregate
    {
        public QueryAggregate(string query) => Query = query;

        public string Query { get; }

        public int Count;
        public int LastResultCount;
        public DateTime LastSeenUtc = DateTime.MinValue;
    }
}

/// <summary>
/// Una búsqueda, tal como queda escrita en el JSONL.
/// </summary>
/// <remarks>
/// Las claves se fijan con <see cref="JsonPropertyNameAttribute"/> porque estos archivos
/// sobreviven a los despliegues: un rename de propiedad en C# volvería ilegible todo lo ya
/// escrito, y como las líneas ilegibles se saltan en silencio, el tablero quedaría en blanco
/// sin un solo error.
/// </remarks>
public sealed record SearchQueryEvent(
    [property: JsonPropertyName("query")] string Query,
    [property: JsonPropertyName("resultCount")] int ResultCount,
    [property: JsonPropertyName("elapsedMs")] long ElapsedMs,
    [property: JsonPropertyName("atUtc")] DateTime AtUtc);
