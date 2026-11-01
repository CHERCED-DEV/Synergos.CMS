using Microsoft.AspNetCore.Mvc;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Interfaces.Lecturas;

namespace Synergos.CMS.Web.Controllers;

/// <summary>
/// Las devoluciones de un pedido, con forma neutra de vertical (014-despiece-tienda, I4). La tienda las leía de
/// <c>GET api/shop/order/{ref}/return</c>, que sigue como interina. Pedirlas y moverlas siguen siendo escrituras de
/// cada vertical.
/// </summary>
[ApiController]
[Route("api/lecturas/{vertical}")]
public sealed class LecturaDeDevolucionesController : ControllerBase
{
    private readonly IEnumerable<ILecturaDeDevoluciones> _lecturas;
    private readonly IMemberAccessGate _sesion;

    /// <summary>Con las lecturas de devoluciones que declaran los verticales y la sesión de quien lee.</summary>
    public LecturaDeDevolucionesController(IEnumerable<ILecturaDeDevoluciones> lecturas, IMemberAccessGate sesion)
    {
        _lecturas = lecturas ?? throw new ArgumentNullException(nameof(lecturas));
        _sesion = sesion ?? throw new ArgumentNullException(nameof(sesion));
    }

    /// <summary>Las devoluciones del pedido, por su referencia.</summary>
    [HttpGet("pedidos/{referencia}/devoluciones")]
    public async Task<IActionResult> DelPedido(string vertical, string referencia, CancellationToken cancellationToken)
    {
        if (_lecturas.DelVertical(vertical) is not { } lectura)
        {
            return this.SinLectura();
        }

        return this.ComoRespuesta(await lectura.DelPedidoAsync(referencia, _sesion, cancellationToken));
    }
}
