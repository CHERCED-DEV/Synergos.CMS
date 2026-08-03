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
/// intentos la compensación queda marcada como colgada, la saga pasa a <c>CompensationFailed</c>,
/// y <see cref="Domain.CompensationAlert"/> le avisa a una persona. Una compensación que se da
/// por buena sin ejecutarse es plata cobrada sin servicio; una que se rinde sin avisar es lo
/// mismo con un log de testigo.</para>
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

                // NeedsSweep y no "tiene pendientes": una saga rendida y ya avisada sigue siendo
                // una fila de /v1/compensations, pero no hay nada que el barrido pueda hacer por
                // ella hasta que una persona pida el reintento.
                foreach (var saga in flow.PendingCompensations().Where(s => s.NeedsSweep))
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
