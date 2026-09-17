using Synergos.Core;

namespace Synergos.Api.Sessions.Domain;

/// <summary>Lo que las señales de sesión rechazan <b>solas</b>.</summary>
/// <remarks>
/// <para><b>Por qué existe este fichero</b> (#58). Era la única de las veinte capacidades sin
/// <c>Domain/*Rules.cs</c>: su regla vivía dentro del método del endpoint, que es donde no se
/// puede probar sin levantar el host. Ese defecto ya costó dos vueltas en este repo —el flujo de
/// reserva dentro de <c>BookingController</c> (#36) y las reglas de emisión dentro del lambda de
/// <c>Api.Identity</c> (#14, rebanada 2), donde una mutación sobrevivió en verde por no tener
/// dónde ponerle un test—. Acá era una sola regla, que es justo lo que la hacía barata de mover.
/// </para>
///
/// <para><b>Y es poco, a propósito.</b> Esta capacidad ingiere señales agregadas y consulta
/// ventanas; no tiene un negocio que decir que no. Un fichero de reglas con una sola guarda no es
/// un fichero de reglas escaso: es el que corresponde a lo que esta capacidad decide.</para>
/// </remarks>
public static class SessionsRules
{
    public const string CodePrefix = "sessions";

    /// <summary>Si la ventana consultada avanza en el tiempo.</summary>
    /// <remarks>
    /// <c>Invalid</c> y no <c>Conflict</c>: una ventana al revés es un error de quien pregunta, no
    /// un choque con el estado. Y se rechaza en vez de devolver una lista vacía, porque vacío es
    /// una respuesta legítima —no hubo búsquedas en ese rango— y confundir las dos haría que un
    /// tablero con las fechas invertidas dijera «no pasó nada» en lugar de «preguntaste mal».
    /// </remarks>
    public static Rejection? CheckWindow(DateTime desdeUtc, DateTime hastaUtc)
        => hastaUtc > desdeUtc
            ? null
            : Rejection.Invalid($"{CodePrefix}.bad_window",
                $"La ventana no avanza: {desdeUtc:O} → {hastaUtc:O}.");
}
