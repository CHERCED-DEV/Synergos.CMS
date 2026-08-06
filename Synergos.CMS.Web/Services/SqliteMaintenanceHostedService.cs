using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Background service que ejecuta SQLite maintenance pragmas
/// periódicamente — <c>wal_checkpoint(TRUNCATE)</c> + <c>optimize</c>.
/// Cap-270 Batch D (Olas 278-279). Cierra finding del audit: WAL
/// file de 4MB sobre DB de 4MB sin checkpoint reciente.
/// </summary>
/// <remarks>
/// Activo solo si la connection string Umbraco apunta a SQLite. Si
/// el operador eventualmente migra a SQL Server (decision out of
/// scope hoy), el service auto-detecta y NO-OP silent.
///
/// Lee la connection string vía <see cref="IConfiguration"/> usando
/// la convention Umbraco <c>ConnectionStrings:umbracoDbDSN</c>.
/// </remarks>
public sealed class SqliteMaintenanceHostedService : BackgroundService
{
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _hostEnvironment;
    private readonly IOptionsMonitor<SqliteMaintenanceSettings> _settings;
    private readonly ILogger<SqliteMaintenanceHostedService> _logger;

    public SqliteMaintenanceHostedService(
        IConfiguration configuration,
        IHostEnvironment hostEnvironment,
        IOptionsMonitor<SqliteMaintenanceSettings> settings,
        ILogger<SqliteMaintenanceHostedService> logger)
    {
        _configuration = configuration;
        _hostEnvironment = hostEnvironment;
        _settings = settings;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var s = _settings.CurrentValue;
        if (!s.Enabled) return;

        var connectionString = ResolveSqliteConnectionString();
        if (connectionString is null)
        {
            _logger.LogInformation(
                "SQLite maintenance: provider no es Sqlite o connection string ausente — no-op");
            return;
        }

        try { await Task.Delay(TimeSpan.FromSeconds(Math.Max(0, s.InitialDelaySeconds)), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunPragmasAsync(connectionString, stoppingToken); }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { _logger.LogError(ex, "SQLite maintenance run failed"); }

            var interval = TimeSpan.FromHours(Math.Max(1, _settings.CurrentValue.IntervalHours));
            try { await Task.Delay(interval, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    private string? ResolveSqliteConnectionString()
    {
        var providerName = _configuration["ConnectionStrings:umbracoDbDSN_ProviderName"];
        if (!string.Equals(providerName, "Microsoft.Data.Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var raw = _configuration["ConnectionStrings:umbracoDbDSN"];
        if (string.IsNullOrWhiteSpace(raw)) return null;

        // Umbraco usa el placeholder |DataDirectory| que ASP.NET Core
        // no expande en runtime — lo sustituimos al ContentRoot/umbraco/Data
        // (convention Umbraco 13 default).
        if (raw.Contains("|DataDirectory|", StringComparison.OrdinalIgnoreCase))
        {
            var dataDir = Path.Combine(_hostEnvironment.ContentRootPath, "umbraco", "Data");
            raw = raw.Replace("|DataDirectory|", dataDir, StringComparison.OrdinalIgnoreCase);
        }
        return raw;
    }

    public static async Task<(int CheckpointResult, bool OptimizeRan)> RunPragmasAsync(
        string connectionString, CancellationToken cancellationToken)
    {
        await using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync(cancellationToken);

        // wal_checkpoint(TRUNCATE) retorna (busy, log_pages, checkpointed_pages).
        // busy=0 indica todos los frames se procesaron OK.
        await using var checkpoint = conn.CreateCommand();
        checkpoint.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
        await using var reader = await checkpoint.ExecuteReaderAsync(cancellationToken);
        var checkpointBusy = -1;
        if (await reader.ReadAsync(cancellationToken))
        {
            checkpointBusy = reader.GetInt32(0);
        }
        await reader.CloseAsync();

        await using var optimize = conn.CreateCommand();
        optimize.CommandText = "PRAGMA optimize;";
        await optimize.ExecuteNonQueryAsync(cancellationToken);

        return (checkpointBusy, true);
    }
}
