using Microsoft.AspNetCore.Mvc;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Interfaces.Lecturas;

namespace Synergos.CMS.Web.Controllers;

/// <summary>
/// Una colección del miembro (la lista de deseos, los favoritos), con forma neutra de vertical
/// (014-despiece-tienda, I4). La tienda la leía de <c>GET api/shop/wishlist</c>, que sigue como interina.
/// </summary>
/// <remarks>El dueño es el correo de la sesión, nunca uno de la query (el IDOR que cerró T2).</remarks>
[ApiController]
[Route("api/lecturas/{vertical}")]
public sealed class LecturaDeColeccionesController : ControllerBase
{
    private readonly IEnumerable<ILecturaDeColecciones> _lecturas;
    private readonly IMemberAccessGate _sesion;

    /// <summary>Con las lecturas de colecciones que declaran los verticales y la sesión de quien lee.</summary>
    public LecturaDeColeccionesController(IEnumerable<ILecturaDeColecciones> lecturas, IMemberAccessGate sesion)
    {
        _lecturas = lecturas ?? throw new ArgumentNullException(nameof(lecturas));
        _sesion = sesion ?? throw new ArgumentNullException(nameof(sesion));
    }

    /// <summary>La colección del miembro de la sesión, con cada elemento leído del catálogo del vertical.</summary>
    [HttpGet("colecciones/{coleccion}")]
    public async Task<IActionResult> Coleccion(string vertical, string coleccion, CancellationToken cancellationToken)
    {
        if (_lecturas.DelVertical(vertical) is not { } lectura)
        {
            return this.SinLectura();
        }

        if (!_sesion.IsAuthenticated || string.IsNullOrWhiteSpace(_sesion.CurrentMemberEmail))
        {
            return this.SinSesion();
        }

        if (string.IsNullOrWhiteSpace(coleccion))
        {
            return BadRequest(new { error = "Falta la colección." });
        }

        return Ok(await lectura.ColeccionAsync(_sesion.CurrentMemberEmail.Trim(), coleccion.Trim(), cancellationToken));
    }
}
