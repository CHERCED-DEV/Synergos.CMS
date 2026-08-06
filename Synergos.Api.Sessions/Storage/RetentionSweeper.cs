using System.Globalization;

namespace Synergos.Api.Sessions.Storage;

/// <summary>
/// Borra los ficheros de búsquedas más viejos que la retención configurada.
/// </summary>
/// <remarks>
/// <para><b>Por qué el servicio lo hace y no un cron.</b> Un almacén append-only sin poda
/// crece hasta llenar el disco, y el día que eso pasa se cae el servicio que existe para no
/// molestar al CMS. La retención es parte del almacén, no una tarea de operación que alguien
/// tiene que acordarse de montar.</para>
///
/// <para>Barre al arrancar y luego cada 24 h. Al arrancar porque un servicio que estuvo
/// apagado un mes despierta con un mes de ficheros de más, y esperar al primer intervalo
/// dejaría ese pico sin tocar justo cuando el disco está más ajustado.</para>
/// </remarks>
public sealed class RetentionSweeper : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    private readonly SearchEventStore _store;
    private readonly ILogger<RetentionSweeper> _log;
    private readonly TimeProvider _clock;
    private readonly int _retentionDays;

    public RetentionSweeper(SearchEventStore store, IConfiguration config, TimeProvider clock, ILogger<RetentionSweeper> log)
    {
        _store = store;
        _log = log;
        // El reloj se inyecta: probar que la retención borra lo viejo no puede exigir
        // esperar un mes. Es la misma regla que rige en Booking (doc 08 §4.4).
        _clock = clock;
        _retentionDays = int.TryParse(config["Sessions:Storage:RetentionDays"],
            NumberStyles.Integer, CultureInfo.InvariantCulture, out var days) && days > 0
            ? days
            : 30;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var purged = _store.Purge(_clock.GetUtcNow().UtcDateTime.AddDays(-_retentionDays));
                if (purged > 0)
                {
                    _log.LogInformation("Retención: {Purged} día(s) purgado(s) (>{Days}d).", purged, _retentionDays);
                }
            }
            catch (Exception ex)
            {
                // Que la poda falle no puede tumbar el servicio: se pierde espacio, no datos.
                _log.LogWarning(ex, "El barrido de retención falló; se reintenta al siguiente ciclo.");
            }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { return; }   // apagado normal, no es un fallo
        }
    }
}
