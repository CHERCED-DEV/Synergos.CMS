using System.Net.Http.Json;
using System.Threading.Channels;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// <see cref="ISearchAnalyticsStore"/> respaldado por el servicio de sesión
/// (<c>Synergos.Api.Sessions</c>) — ADR 0130.
/// </summary>
/// <remarks>
/// <para><b>La escritura NO viaja en el request del usuario.</b> <see cref="Record"/> encola y
/// vuelve; un lazo de fondo hace el POST. Si hiciera la llamada en línea, cada búsqueda de un
/// visitante esperaría un salto de red para guardar una métrica — se habría cambiado un
/// problema de acoplamiento por uno de latencia, y encima en el camino del usuario.</para>
///
/// <para><b>La cola es ACOTADA y descarta lo más viejo.</b> Con el servicio caído, una cola sin
/// tope se come la memoria del CMS: la analítica habría tumbado justo lo que venía a
/// descargar. Cuando se llena se tira el evento más antiguo, porque en una serie temporal el
/// dato reciente vale más.</para>
///
/// <para><b>Las lecturas son async de verdad</b>, no <c>.Result</c>. Es la razón por la que la
/// interfaz cambió: bloquear un hilo del pool esperando a otro proceso agota el pool bajo
/// carga, y <c>/api/search/analytics</c> es <c>[AllowAnonymous]</c> con el gate por rol
/// saliendo de configuración, así que el peor caso es una ruta pública bloqueando hilos.</para>
///
/// <para><b>Si el servicio no responde, se devuelve vacío.</b> Un dashboard sin datos es un
/// inconveniente; un dashboard que revienta porque un servicio auxiliar está caído convierte un
/// fallo de analítica en un fallo de operación.</para>
/// </remarks>
public sealed class HttpSearchAnalyticsStore : ISearchAnalyticsStore, IHostedService, IDisposable
{
    /// <summary>Cabecera con la llave compartida. Debe coincidir con la del servicio.</summary>
    public const string ApiKeyHeader = "X-Synergos-Key";

    /// <summary>Tope de la cola de salida. Ver el porqué en las notas de la clase.</summary>
    internal const int QueueCapacity = 2_000;

    private readonly IHttpClientFactory _clients;
    private readonly ILogger<HttpSearchAnalyticsStore> _log;
    private readonly Channel<SearchEventDto> _queue;
    private readonly CancellationTokenSource _shutdown = new();
    private Task? _pump;
    private long _dropped;

    public HttpSearchAnalyticsStore(IHttpClientFactory clients, ILogger<HttpSearchAnalyticsStore> log)
    {
        _clients = clients;
        _log = log;
        // La sobrecarga CON callback, no la simple: con DropOldest el TryWrite SIEMPRE
        // devuelve true —descarta por dentro y no avisa—, así que contar por su retorno daba
        // un contador clavado en cero. Un test lo destapó.
        _queue = Channel.CreateBounded<SearchEventDto>(
            new BoundedChannelOptions(QueueCapacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
            },
            itemDropped: _ => Interlocked.Increment(ref _dropped));
    }

    /// <summary>Eventos descartados por cola llena desde el arranque.</summary>
    internal long Dropped => Interlocked.Read(ref _dropped);

    public void Record(string query, int resultCount, long elapsedMilliseconds)
    {
        // El contrato de la interfaz es explícito: esto no puede lanzar. Una búsqueda que
        // funcionó no puede fallar porque su métrica no se pudo encolar.
        try
        {
            // El descarte por cola llena lo cuenta el callback de arriba. Este `if` cubre el
            // otro caso: la cola cerrada durante el apagado.
            if (!_queue.Writer.TryWrite(new SearchEventDto(query, resultCount, elapsedMilliseconds)))
            {
                Interlocked.Increment(ref _dropped);
            }
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _dropped);
            _log.LogWarning(ex, "No se pudo encolar una búsqueda para el servicio de sesión.");
        }
    }

    public async Task<IReadOnlyList<SearchQueryStat>> GetTopQueriesAsync(
        DateTime fromUtc, DateTime toUtc, int limit, CancellationToken cancellationToken = default)
        => await QueryAsync("v1/search/top", fromUtc, toUtc, limit, cancellationToken);

    public async Task<IReadOnlyList<SearchQueryStat>> GetTopNoResultQueriesAsync(
        DateTime fromUtc, DateTime toUtc, int limit, CancellationToken cancellationToken = default)
        => await QueryAsync("v1/search/no-results", fromUtc, toUtc, limit, cancellationToken);

    private async Task<IReadOnlyList<SearchQueryStat>> QueryAsync(
        string path, DateTime fromUtc, DateTime toUtc, int limit, CancellationToken cancellationToken)
    {
        var url = $"{path}?from={fromUtc:O}&to={toUtc:O}&limit={limit}";
        try
        {
            var stats = await _clients.CreateClient(HttpClientName)
                .GetFromJsonAsync<List<QueryStatDto>>(url, cancellationToken);
            return stats?.Select(s => new SearchQueryStat(s.Query, s.Count, s.LastResultCount, s.LastSeenUtc))
                        .ToList()
                   ?? (IReadOnlyList<SearchQueryStat>)Array.Empty<SearchQueryStat>();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;   // el cliente se fue; no es un fallo del servicio
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "El servicio de sesión no respondió a {Url}; se sirve vacío.", url);
            return Array.Empty<SearchQueryStat>();
        }
    }

    /// <summary>Nombre del cliente HTTP nombrado que registra el composer.</summary>
    public const string HttpClientName = "synergos-sessions";

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _pump = Task.Run(() => PumpAsync(_shutdown.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _queue.Writer.TryComplete();
        _shutdown.Cancel();
        if (_pump is not null)
        {
            // Se espera al lazo pero SIN propagar su cancelación: apagar el CMS no puede
            // fallar porque la analítica estaba a mitad de un envío.
            try { await _pump.WaitAsync(cancellationToken); }
            catch (OperationCanceledException) { /* apagado normal */ }
        }
        if (Dropped > 0)
        {
            _log.LogInformation("Servicio de sesión: {Dropped} evento(s) descartados en esta vida del proceso.", Dropped);
        }
    }

    private async Task PumpAsync(CancellationToken token)
    {
        try
        {
            await foreach (var evt in _queue.Reader.ReadAllAsync(token))
            {
                try
                {
                    var res = await _clients.CreateClient(HttpClientName)
                        .PostAsJsonAsync("v1/search-events", evt, token);
                    if (!res.IsSuccessStatusCode)
                    {
                        // No se reintenta a propósito: reintentar analítica con el servicio
                        // caído convierte una degradación en una tormenta de peticiones.
                        Interlocked.Increment(ref _dropped);
                        _log.LogDebug("El servicio de sesión respondió {Status}; se descarta el evento.", res.StatusCode);
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref _dropped);
                    _log.LogDebug(ex, "Fallo enviando una búsqueda al servicio de sesión; se descarta.");
                }
            }
        }
        catch (OperationCanceledException) { /* apagado normal */ }
    }

    public void Dispose() => _shutdown.Dispose();

    // Los DTO viven aquí y NO en Synergos.CMS.Interfaces: son la forma del contrato HTTP con
    // otro servicio, no vocabulario del dominio del CMS.
    private sealed record SearchEventDto(string Query, int ResultCount, long ElapsedMs);

    private sealed record QueryStatDto(string Query, int Count, int LastResultCount, DateTime LastSeenUtc);
}
