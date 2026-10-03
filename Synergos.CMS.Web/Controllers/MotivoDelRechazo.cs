namespace Synergos.CMS.Web.Controllers;

/// <summary>
/// El motivo de un rechazo tal como lo lee una persona: el mensaje de la excepción del motor, sin
/// el «(Parameter 'items')» que .NET le pega a un <see cref="ArgumentException"/>.
/// </summary>
/// <remarks>
/// <para><b>Una sola pieza para todos los controladores.</b> El defecto lo vio la escala de la ADR
/// 0137 en el checkout de eventos (#195) y se arregló allí, en un método privado; los otros ocho
/// controladores de vertical devolvían <c>ex.Message</c> tal cual en 50 sitios, con el mismo
/// sufijo cuando la excepción trae nombre de parámetro. Un detalle de C# no es para el visitante.</para>
///
/// <para>El sufijo no se escribe a mano: se pide al propio runtime (<c>new ArgumentException("",
/// nombre).Message</c>), así que sigue valiendo si .NET cambia su forma o su idioma.</para>
/// </remarks>
public static class MotivoDelRechazo
{
    /// <summary>El mensaje de <paramref name="excepcion"/> sin el sufijo del parámetro.</summary>
    public static string Motivo(this Exception excepcion)
    {
        if (excepcion is not ArgumentException { ParamName: { Length: > 0 } parametro } argumento)
        {
            return excepcion.Message;
        }

        var sufijo = new ArgumentException(string.Empty, parametro).Message;
        return argumento.Message.EndsWith(sufijo, StringComparison.Ordinal)
            ? argumento.Message[..^sufijo.Length]
            : argumento.Message;
    }
}
