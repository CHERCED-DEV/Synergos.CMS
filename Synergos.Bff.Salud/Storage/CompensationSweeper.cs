using Synergos.Bff.Salud.Domain;

namespace Synergos.Bff.Salud.Storage;

/// <summary>
/// Reintenta las compensaciones que quedaron pendientes.
/// </summary>
/// <remarks>
/// <para><b>Es la pieza que hace que la compensación no sea una promesa.</b> Compensar en línea
/// funciona mientras la capacidad esté viva; cuando está caída —que es la causa habitual de que
/// el flujo fallara— el intento en línea falla también. Sin este barrido, ahí terminaría la
/// historia: plata cobrada, sin cita, y nadie mirando.</para>
///
/// <para>Barre cada minuto, y cada compensación decide sola si le llegó el turno según su
/// retroceso exponencial. Al arrancar barre de inmediato: un proceso que estuvo caído una hora
/// despierta con una hora de compensaciones atrasadas, y esperar al primer intervalo las dejaría
/// esperando más.</para>
///
/// <para><b>Lo que NO hace: rendirse en silencio.</b> Tras <see cref="Compensator.MaxAttempts"/>
/// intentos la compensación queda marcada como colgada y la saga pasa a
/// <c>CompensationFailed</c> — visible en <c>/v1/compensations</c> y gritada en el log. Una
/// compensación que se da por buena sin ejecutarse es plata cobrada sin servicio.</para>
/// </remarks>
public sealed class CompensationSweeper : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<CompensationSweeper> _log;

    public CompensationSweeper(IServiceScopeFactory scopes, ILogger<CompensationSweeper> log)
    {
        _scopes = scopes;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var flow = scope.ServiceProvider.GetRequiredService<AppointmentFlow>();

                foreach (var saga in flow.PendingCompensations())
                {
                    await flow.CompensateAsync(saga.Id, "reintento del barrido", stoppingToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Que el barrido falle no puede tumbarlo: se pierde una vuelta, no la bitácora.
                _log.LogWarning(ex, "El barrido de compensaciones falló; se reintenta al siguiente ciclo.");
            }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }
}
