using Microsoft.AspNetCore.Mvc;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Interfaces.Lecturas;

namespace Synergos.CMS.Web.Controllers;

/// <summary>
/// Las reseñas de un elemento del catálogo, con forma neutra de vertical (014-despiece-tienda, I4). La tienda las leía
/// dentro de <c>GET api/shop/product/{id}</c>, que sigue como interina.
/// </summary>
[ApiController]
[Route("api/lecturas/{vertical}")]
public sealed class LecturaDeResenasController : ControllerBase
{
    private readonly IEnumerable<ILecturaDeResenas> _lecturas;
    private readonly IMemberAccessGate _sesion;

    /// <summary>Con las lecturas de reseñas que declaran los verticales y la sesión de quien lee.</summary>
    public LecturaDeResenasController(IEnumerable<ILecturaDeResenas> lecturas, IMemberAccessGate sesion)
    {
        _lecturas = lecturas ?? throw new ArgumentNullException(nameof(lecturas));
        _sesion = sesion ?? throw new ArgumentNullException(nameof(sesion));
    }

    /// <summary>Las reseñas del elemento y si quien lee puede reseñarlo.</summary>
    [HttpGet("catalogo/{id}/resenas")]
    public async Task<IActionResult> DelElemento(string vertical, string id, CancellationToken cancellationToken)
    {
        if (_lecturas.DelVertical(vertical) is not { } lectura)
        {
            return this.SinLectura();
        }

        var miembro = _sesion.IsAuthenticated ? _sesion.CurrentMemberKey : null;
        var resenas = await lectura.DelElementoAsync(id, miembro, cancellationToken);
        return resenas is null ? NotFound(new { error = "No está en el catálogo." }) : Ok(resenas);
    }
}
