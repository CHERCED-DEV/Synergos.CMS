using Microsoft.Extensions.Logging;
using Synergos.Bff.Core.Flow;

namespace Synergos.Bff.Core;

/// <summary>
/// El ámbito del registro con la saga y su número (núcleo, NB-9 de 011): lo lleva cada línea que se escribe
/// mientras una fase corre o una saga se deshace.
/// </summary>
/// <remarks>
/// <para><b>Para seguir una compra por el número que ve la persona.</b> La correlación (<c>Correlacion:…</c>)
/// ata los procesos de UNA petición; el número ata las peticiones de UNA saga —abrir, cerrar, el barrido que
/// la deshace una hora después—. Medido en vivo antes de esto: el número de una reserva aparecía cero veces en
/// el registro del orquestador, y la saga sólo salía en <c>cerrar</c>.</para>
///
/// <para><b>Lo abre el intérprete y lo abre el motor</b>, y nadie más: alrededor de cada fase y de cada
/// compensación. Todo lo que pasa dentro —cada paso, cada llamada a una capacidad, cada deshacer— lo lleva sin
/// que lo escriba nadie, porque el ámbito es del <see cref="ILoggerFactory"/>, no de un registrador.</para>
///
/// <para><b>Una cadena de formato y no un diccionario</b>, por lo mismo que la correlación
/// (<c>Correlation.ScopeFormat</c>): el formateador de consola imprime un diccionario con el nombre de su
/// tipo, y el <c>grep</c> devolvería cero líneas con todo en verde.</para>
/// </remarks>
public static class AmbitoDeLaSaga
{
    /// <summary>Cómo sale en cada línea: <c>Saga:cita-1#2 Numero:CITA12</c>.</summary>
    public const string Formato = "Saga:{Saga} Numero:{NumeroDeSaga}";

    /// <summary>Abre el ámbito de <paramref name="sagaId"/> en <paramref name="log"/>; se cierra al desecharlo.</summary>
    public static IDisposable? Abrir(ILogger log, string sagaId)
    {
        ArgumentNullException.ThrowIfNull(log);
        return log.BeginScope(Formato, sagaId, SagaDeFlujo.NumeroDe(sagaId));
    }
}
