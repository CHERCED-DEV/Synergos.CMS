using System.Security.Cryptography;
using System.Text;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Cómo ve una capacidad a una persona: una huella estable de su correo, <b>nunca el correo</b>.
/// </summary>
/// <remarks>
/// <para><b>Por qué vive acá y no en seis sitios</b> (#120). Estaba escrito seis veces con tres
/// nombres —<c>Seudonimo</c> en Auditoría, Realty y Pagos; <c>BuyerId</c> en Eventos y Tienda;
/// <c>TravellerId</c> en Viajes— y la regla del repo promueve al SEGUNDO consumidor
/// (<c>CLAUDE.md</c> §0.B.17). Con seis llevaba cinco de retraso.</para>
///
/// <para><b>Lo que de verdad arreglaba la promoción no era la duplicación: eran las VARIACIONES.</b>
/// Dos de las seis copias no hacían lo mismo que las otras cuatro —la bitácora devuelve el actor
/// del sistema cuando no hay correo, y la tienda prefiere el <c>MemberKey</c> sobre la huella— y
/// eso no se ve leyendo una copia. La séptima que alguien escriba va a copiar la que tenga más
/// cerca: quien copie la de Auditoría se lleva el actor del sistema a un flujo donde el correo
/// vacío tendría que ser una huella, y quien copie cualquier otra y la use para la tienda pierde
/// el <c>MemberKey</c>.</para>
///
/// <para><b>Y por eso las dos variaciones NO están acá dentro.</b> Esta función hace una cosa: el
/// correo a su huella. Quién es el actor cuando no hay correo lo decide la bitácora, y preferir el
/// <c>MemberKey</c> lo decide la tienda, cada una en su sitio. Un helper con dos banderas es seis
/// copias con más pasos, y encima esconde las políticas donde nadie las lee.</para>
///
/// <para><b>Lo que se le parece y NO es esto</b>, porque el algoritmo no es el criterio:
/// <c>HttpGovActNotificationService.Huella</c> resume el <b>cuerpo de un acto administrativo</b>
/// para su llave de idempotencia, y <c>HttpHotelBookingService.IdempotencyKeyFor</c> resume
/// <b>qué se reserva</b>. Mismo SHA256 y otro significado: meterlos acá dejaría un helper llamado
/// «seudónimo» que en un sitio identifica a alguien y en otro resume un texto, y el día que una de
/// las dos necesite cambiar, cambiaría la otra.</para>
///
/// <para><c>string.GetHashCode()</c> NO sirve, y es la razón por la que esto existe como función y
/// no como una línea suelta: .NET lo <b>aleatoriza por proceso</b>, así que la misma persona sería
/// un sujeto distinto tras cada reinicio y la capacidad dejaría de agrupar sus registros sin que
/// nada fallara (<c>feedback_gethashcode_is_not_a_seed</c>).</para>
///
/// <para>No pretende ser anonimato: es no esparcir lo que no hace falta esparcir. El nombre
/// legible se queda de este lado. Y nunca el nombre como semilla: dos personas se llaman igual.</para>
/// </remarks>
internal static class SeudonimoDePersona
{
    /// <summary>La huella estable de un correo. Un correo vacío da la huella de la cadena vacía.</summary>
    /// <remarks>
    /// Que el vacío tenga huella —en vez de lanzar o devolver algo especial— es lo que deja que
    /// cada llamador decida qué significa no tener correo. La bitácora lo trata como el actor del
    /// sistema ANTES de llamar acá; los demás guardan la huella del vacío, que agrupa a todos los
    /// anónimos bajo un mismo sujeto opaco y es lo correcto para contar cupo o plata.
    /// </remarks>
    internal static string De(string? correo)
    {
        var limpio = (correo ?? string.Empty).Trim().ToLowerInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(limpio));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }
}
