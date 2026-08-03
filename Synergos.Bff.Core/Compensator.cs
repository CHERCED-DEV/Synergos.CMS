using Microsoft.Extensions.Logging;
using Synergos.Core;

namespace Synergos.Bff.Core;

/// <summary>
/// Cómo el dominio deshace lo que el dominio hizo.
/// </summary>
/// <remarks>
/// <para><b>Es LA costura de la promoción.</b> El motor sabe cuándo reintentar, cuántas veces y
/// cuándo rendirse; lo que no puede saber es que soltar un cupo es un <c>POST</c> a
/// <c>booking/holds/{id}/release</c> y devolver plata es otro a <c>payments</c>. Eso es negocio,
/// y por eso lo implementa cada BFF.</para>
///
/// <para><b>No lanza.</b> Devuelve <c>null</c> si la compensación quedó hecha, o el rechazo si no
/// pudo. Una excepción escapando de aquí tumbaría el barrido entero, y con él las compensaciones
/// de todas las demás sagas.</para>
///
/// <para><b>Recibe la saga del dominio, no la interfaz.</b> Deshacer casi nunca se explica solo
/// con un identificador: devolver existencias necesita saber <i>cuántas</i>, y eso vive en la
/// saga concreta. La alternativa —un campo de carga libre en <see cref="Compensation"/>— sería
/// una cadena sin tipo que cada dominio interpreta a su manera.</para>
/// </remarks>
public interface ICompensationExecutor<in TSaga> where TSaga : ISaga
{
    Task<Rejection?> UndoAsync(TSaga saga, Compensation pending, CancellationToken ct);
}

/// <summary>
/// Ejecuta una compensación pendiente y decide cómo queda.
/// </summary>
/// <remarks>
/// <b>Separado del flujo a propósito.</b> Lo mismo tiene que poder correr en dos momentos muy
/// distintos: en línea, cuando el paso falla y todavía hay un usuario esperando; y horas después,
/// desde un barrido de fondo, cuando la capacidad que estaba caída volvió. Un solo camino para
/// las dos evita que la segunda vez se haga distinto de la primera.
/// </remarks>
public sealed class Compensator<TSaga> where TSaga : ISaga
{
    /// <summary>Cuántos intentos antes de rendirse y pedir una persona.</summary>
    /// <remarks>Reexpuesto acá por comodidad; el número vive en <see cref="CompensationLimits"/>
    /// porque <see cref="Compensation.IsStuck"/> lo necesita y no conoce el tipo de la saga.</remarks>
    public const int MaxAttempts = CompensationLimits.MaxAttempts;

    private readonly ICompensationExecutor<TSaga> _executor;
    private readonly TimeProvider _clock;
    private readonly ILogger<Compensator<TSaga>> _log;

    public Compensator(ICompensationExecutor<TSaga> executor, TimeProvider clock, ILogger<Compensator<TSaga>> log)
    {
        _executor = executor;
        _clock = clock;
        _log = log;
    }

    /// <summary>
    /// Espera antes del intento <paramref name="attempts"/>: 1, 2, 4… minutos, con techo.
    /// </summary>
    /// <remarks>
    /// Retroceso exponencial y no reintento inmediato: la causa habitual de que una compensación
    /// falle es que la capacidad está caída, y martillearla no la levanta — solo alarga la caída.
    /// </remarks>
    public static TimeSpan Backoff(int attempts)
        => TimeSpan.FromMinutes(Math.Min(60, Math.Pow(2, Math.Min(attempts, 6))));

    /// <summary>Intenta una compensación. Devuelve cómo queda.</summary>
    public async Task<Compensation> TryAsync(TSaga saga, Compensation pendiente, CancellationToken ct)
    {
        var ahora = _clock.GetUtcNow();
        var intentos = pendiente.Attempts + 1;

        Rejection? resultado;
        try
        {
            resultado = await _executor.UndoAsync(saga, pendiente, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Un ejecutor que lanza es un defecto del dominio, no de la saga. Se trata como un
            // fallo reintentable y se anota: dejarlo escapar tumbaría el barrido y con él las
            // compensaciones de todas las demás sagas.
            resultado = Rejection.Unavailable("saga.executor_threw",
                $"El ejecutor de {pendiente.Kind} lanzó: {ex.Message}");
        }

        if (resultado is null)
        {
            _log.LogInformation("Compensación {Kind} de la saga {Saga} completada en el intento {N}.",
                pendiente.Kind, saga.Id, intentos);
            return pendiente with { Attempts = intentos, DoneAtUtc = ahora, LastError = null, NextAttemptUtc = null };
        }

        // Se rinde a gritos y NO se marca como hecha: una compensación que se da por buena sin
        // haberse ejecutado es plata que se quedó cobrada sin servicio, y nadie se entera.
        if (intentos >= MaxAttempts)
        {
            _log.LogError(
                "COMPENSACIÓN COLGADA: {Kind} sobre {Target} de la saga {Saga} falló {N} veces ({Error}). Necesita una persona.",
                pendiente.Kind, pendiente.TargetId, saga.Id, intentos, resultado);
            return pendiente with { Attempts = intentos, LastError = resultado.ToString(), NextAttemptUtc = null };
        }

        _log.LogWarning("Compensación {Kind} de la saga {Saga} falló ({Error}); reintento {N}.",
            pendiente.Kind, saga.Id, resultado, intentos);
        return pendiente with { Attempts = intentos, LastError = resultado.ToString(), NextAttemptUtc = ahora + Backoff(intentos) };
    }
}
