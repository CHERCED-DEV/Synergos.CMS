using Microsoft.AspNetCore.Mvc;
using Synergos.CMS.Interfaces.Lecturas;

namespace Synergos.CMS.Web.Controllers;

/// <summary>
/// Lo que contestan las lecturas genéricas <c>api/lecturas/{vertical}/…</c> (014-despiece-tienda, I4) cuando el
/// vertical no tiene la lectura, cuando falta la sesión y cuando lo pedido por referencia no existe o es ajeno.
/// </summary>
internal static class RespuestasDeLasLecturas
{
    /// <summary>404: el vertical de la ruta no declara esta lectura.</summary>
    internal static IActionResult SinLectura(this ControllerBase controller)
        => controller.NotFound(new { error = "Este vertical no tiene esta lectura." });

    /// <summary>401: hace falta iniciar sesión.</summary>
    internal static IActionResult SinSesion(this ControllerBase controller)
        => controller.Unauthorized(new { error = "Se requiere iniciar sesión." });

    /// <summary>Lo leído por referencia como respuesta: 200, 404 o 403.</summary>
    internal static IActionResult ComoRespuesta<T>(this ControllerBase controller, LecturaPorReferencia<T> lectura)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(lectura);
        return lectura.Resultado switch
        {
            ResultadoDeLaLectura.Leida => controller.Ok(lectura.Valor),
            // 403 directo y no Forbid(): con la autenticación de miembros, Forbid() redirige al login (302).
            ResultadoDeLaLectura.Ajena => controller.StatusCode(StatusCodes.Status403Forbidden),
            _ => controller.NotFound(new { error = "No existe." }),
        };
    }
}
