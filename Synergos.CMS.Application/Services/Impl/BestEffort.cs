namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Ejecuta un side-effect que NO debe tumbar la transacción que ya se cometió.
/// </summary>
/// <remarks>
/// Los motores hacen cosas DESPUÉS de persistir el hecho de negocio: avanzar el timeline
/// de tracking, escribir el rastro de auditoría, notificar. Ninguna de esas puede
/// convertir una orden ya pagada (y ya en disco) en un error para el cliente. Sin esta
/// guarda, un disco lleno al escribir el audit devolvía 500 sobre una compra exitosa.
///
/// <para>Vive en UN solo lugar en vez de copiarse en cada motor — es la misma razón por la
/// que el proyecto colapsó sus 4 stores en <c>IJsonEntityStore</c> (regla de oro doc 25).
/// El patrón ya existía bien resuelto en Healthcare (<c>AuditPhiAsync</c>, try/catch
/// privado); esto lo generaliza en vez de clonarlo 8 veces.</para>
///
/// <para><b>La cancelación NO se traga:</b> si el request se canceló, el motor debe
/// enterarse — tragarla convertiría una cancelación en un éxito silencioso.</para>
///
/// <para><b>Solo para POST-commit.</b> Un side-effect del que dependa la validez del hecho
/// (o un audit que actúe como GATE de acceso, p.ej. regulatorio "sin auditoría no hay
/// acceso") NO va aquí: ahí el fallo debe propagar.</para>
/// </remarks>
internal static class BestEffort
{
    internal static async Task RunAsync(Func<Task> action, CancellationToken cancellationToken)
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // El hecho de negocio ya ocurrió y está persistido: degradar es correcto,
            // tumbarlo sería un bug.
        }
    }
}
