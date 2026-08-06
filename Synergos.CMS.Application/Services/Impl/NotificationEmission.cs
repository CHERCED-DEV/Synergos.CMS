using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// El único punto por el que los motores emiten notificaciones. Existe para que la regla
/// "un email que falla JAMÁS tumba una transacción ya cometida" sea ESTRUCTURAL y no
/// dependa de la disciplina del dispatcher ni se copie en cada motor.
/// </summary>
/// <remarks>
/// <b>Por qué no basta el contrato "el composite nunca lanza":</b> eso es disciplina, no
/// compilador. Cualquier canal, decorator o test double inyectado directo en Application
/// propagaría la excepción sobre una orden YA pagada. Aquí la guarda vive en UN solo lugar
/// (regla de oro: no se copia en los 7 puntos de emisión) y el call-site es una línea.
///
/// <para><b>La cancelación NO se traga:</b> si el request se canceló, el motor debe
/// enterarse — tragarla convertiría una cancelación en un éxito silencioso.</para>
///
/// <para>El swallow es silencioso a propósito: el dispatcher (Web) ya loguea y audita sus
/// propios fallos; duplicar el log aquí sin un ILogger en Application no aporta.</para>
/// </remarks>
internal static class NotificationEmission
{
    internal static async Task SafeDispatchAsync(
        ITransactionalNotifier? notifier,
        NotificationEvent notification,
        CancellationToken cancellationToken)
    {
        if (notifier is null)
        {
            return;   // seam opcional no inyectado → no notificar (comportamiento pre-T4)
        }

        // La guarda vive en BestEffort (una sola impl para tracking/audit/notificación).
        await BestEffort.RunAsync(
            () => notifier.DispatchAsync(notification, cancellationToken),
            cancellationToken);
    }
}
