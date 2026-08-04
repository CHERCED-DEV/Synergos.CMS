using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Synergos.Bff.Core;

/// <summary>
/// Reintenta las compensaciones que quedaron pendientes.
/// </summary>
/// <remarks>
/// <para><b>Es la pieza que hace que la compensación no sea una promesa.</b> Compensar en línea
/// funciona mientras la capacidad esté viva; cuando está caída —que es la causa habitual de que
/// el flujo fallara— el intento en línea falla también. Sin este barrido, ahí terminaría la
/// historia: plata cobrada, sin servicio, y nadie mirando.</para>
///
/// <para>Barre cada minuto, y cada compensación decide sola si le llegó el turno según su
/// retroceso exponencial. Al arrancar barre de inmediato: un proceso que estuvo caído una hora
/// despierta con una hora de compensaciones atrasadas, y esperar al primer intervalo las dejaría
/// esperando más.</para>
///
/// <para><b>Lo que NO hace: rendirse en silencio.</b> Tras <see cref="Compensator.MaxAttempts"/>
/// intentos la compensación queda marcada como colgada, la saga pasa a <c>CompensationFailed</c>,
/// y <see cref="CompensationAlert"/> le avisa a una persona. Una compensación que se da por buena
/// sin ejecutarse es plata cobrada sin servicio; una que se rinde sin avisar es lo mismo con un
/// log de testigo.</para>
/// </remarks>
public sealed class CompensationSweeper<TSaga> : BackgroundService where TSaga : class, ISaga<TSaga>
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IOptionsMonitor<SweepOptions> _opciones;
    private readonly TimeProvider _reloj;
    private readonly ILogger<CompensationSweeper<TSaga>> _log;

    public CompensationSweeper(
        IServiceScopeFactory scopes,
        IOptionsMonitor<SweepOptions> opciones,
        TimeProvider reloj,
        ILogger<CompensationSweeper<TSaga>> log)
    {
        _scopes = scopes;
        _opciones = opciones;
        _reloj = reloj;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var engine = scope.ServiceProvider.GetRequiredService<SagaEngine<TSaga>>();

                // NeedsSweep y no "tiene pendientes": una saga rendida y ya avisada sigue siendo
                // una fila de la vista de operación, pero no hay nada que el barrido pueda hacer
                // por ella hasta que una persona pida el reintento.
                foreach (var saga in engine.PendingCompensations().Where(s => s.NeedsSweep()))
                {
                    await engine.CompensateAsync(saga.Id, "reintento del barrido", stoppingToken);
                }

                await AbandonarLasQueNuncaConfirmaronAsync(engine, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Que el barrido falle no puede tumbarlo: se pierde una vuelta, no la bitácora.
                _log.LogWarning(ex, "El barrido de compensaciones falló; se reintenta al siguiente ciclo.");
            }

            try { await Task.Delay(Cadencia, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    private TimeSpan Cadencia => TimeSpan.FromSeconds(Math.Max(5, _opciones.CurrentValue.IntervalSeconds));

    /// <summary>
    /// Da por muerta la saga que empezó y nunca confirmó, y deshace lo que había hecho.
    /// </summary>
    /// <remarks>
    /// <para><b>El caso que esto cierra:</b> una compra que apartó existencias y autorizó el
    /// cobro, y cuyo comprador cerró la pestaña. Sin nadie que vuelva por ella, la autorización
    /// se queda reservada en la tarjeta de una persona indefinidamente.</para>
    ///
    /// <para><b>Lo que NO hace, y es la mitad del cuidado:</b> tocar una saga viva. Sus
    /// compensaciones están ARMADAS, no pendientes — armada no es pendiente
    /// (<c>feedback_compensation_is_data</c>)—, y ejecutarlas desharía compras que iban
    /// perfectamente. Lo único que convierte una saga sana en trabajo es <b>el tiempo</b>, y por
    /// eso el único filtro es la fecha de arranque.</para>
    ///
    /// <para><b>El plazo es un compromiso de negocio, no una constante.</b> Muy corto cancela
    /// compras que iban bien —un pago que tardó—; muy largo deja plata reservada en tarjetas
    /// ajenas. Sale de configuración y en cero se apaga: un despliegue puede decidir que prefiere
    /// no abandonar nada.</para>
    ///
    /// <para><b>Ojo con lo que este barrido NO es:</b> no es lo que devuelve el stock. Los
    /// apartados de <c>Api.Inventory</c> vencen solos a los 15 minutos por su propio TTL, mucho
    /// antes que este plazo. Lo que esto rescata es lo que no vence solo — la autorización del
    /// cobro— y deja la saga en un estado final en vez de <c>Running</c> para siempre.</para>
    /// </remarks>
    private async Task AbandonarLasQueNuncaConfirmaronAsync(SagaEngine<TSaga> engine, CancellationToken ct)
    {
        var opciones = _opciones.CurrentValue;
        if (opciones.FechaDeAbandono(_reloj.GetUtcNow()) is not { } limite) return;   // apagado
        var minutos = opciones.AbandonAfterMinutes;

        foreach (var saga in engine.StartedBefore(limite))
        {
            _log.LogWarning(
                "Saga {Id} lleva más de {Minutos} min sin confirmar: se abandona y se deshace lo hecho.",
                saga.Id, minutos);

            await engine.CompensateAsync(saga.Id, $"abandonada: {minutos} min sin confirmar", ct);
        }
    }
}

/// <summary>
/// Cada cuánto barre el motor, y cuándo da por muerta una saga (HU #29).
/// </summary>
/// <remarks>
/// Las dos van en configuración porque las dos son compromisos, no verdades: la cadencia se paga
/// en peticiones contra las capacidades, y el plazo de abandono se paga en compras canceladas si
/// se queda corto. Cablearlas sería tomar la decisión de negocio de quien despliega.
/// </remarks>
public sealed class SweepOptions
{
    /// <summary>Cada cuántos segundos corre el barrido. Piso de 5 para que nadie lo ponga en cero.</summary>
    public int IntervalSeconds { get; set; } = 60;

    /// <summary>
    /// A los cuántos minutos sin confirmar se abandona una saga. <b>Cero lo apaga.</b>
    /// </summary>
    /// <remarks>
    /// El default es generoso a propósito: una hora deja pasar un pago lento sin cancelar una
    /// compra buena, y el stock ya volvió por su cuenta mucho antes (el apartado de
    /// <c>Api.Inventory</c> vence a los 15 minutos).
    /// </remarks>
    public int AbandonAfterMinutes { get; set; } = 60;

    /// <summary>
    /// Antes de qué instante una saga sin confirmar se da por muerta — o <c>null</c> si el
    /// abandono está apagado.
    /// </summary>
    /// <remarks>
    /// Es una función y no un <c>if</c> dentro del barrido para que el interruptor se pueda
    /// probar sin levantar un servicio de fondo. Un apagado que nadie verifica es un apagado que
    /// alguien rompe al refactorizar, y el síntoma sería compras canceladas en un despliegue que
    /// pidió explícitamente que no se cancelara ninguna.
    /// </remarks>
    public DateTimeOffset? FechaDeAbandono(DateTimeOffset ahora)
        => AbandonAfterMinutes <= 0 ? null : ahora - TimeSpan.FromMinutes(AbandonAfterMinutes);
}
