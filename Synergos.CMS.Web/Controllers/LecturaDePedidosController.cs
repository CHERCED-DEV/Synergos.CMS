using Microsoft.AspNetCore.Mvc;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Interfaces.Lecturas;

namespace Synergos.CMS.Web.Controllers;

/// <summary>
/// Los pedidos de quien compra y el seguimiento de uno, con forma neutra de vertical (014-despiece-tienda, I4). La
/// tienda los leía de <c>GET api/shop/orders</c> y <c>GET api/shop/order/{ref}/tracking</c>, que siguen como interinas.
/// </summary>
/// <remarks>
/// La identidad es de la sesión, nunca de la query (T2, doc 25). El seguimiento se lee con la referencia, que es la
/// credencial de quien compró sin cuenta; un pedido con dueño no lo lee otro miembro (403), salvo el personal.
/// </remarks>
[ApiController]
[Route("api/lecturas/{vertical}")]
public sealed class LecturaDePedidosController : ControllerBase
{
    private readonly IEnumerable<ILecturaDePedidos> _lecturas;
    private readonly IMemberAccessGate _sesion;

    /// <summary>Con las lecturas de pedidos que declaran los verticales y la sesión de quien lee.</summary>
    public LecturaDePedidosController(IEnumerable<ILecturaDePedidos> lecturas, IMemberAccessGate sesion)
    {
        _lecturas = lecturas ?? throw new ArgumentNullException(nameof(lecturas));
        _sesion = sesion ?? throw new ArgumentNullException(nameof(sesion));
    }

    /// <summary>Los pedidos del miembro de la sesión.</summary>
    [HttpGet("pedidos")]
    public async Task<IActionResult> DelMiembro(string vertical, CancellationToken cancellationToken)
    {
        if (_lecturas.DelVertical(vertical) is not { } lectura)
        {
            return this.SinLectura();
        }

        if (!_sesion.IsAuthenticated || _sesion.CurrentMemberKey is not Guid miembro)
        {
            return this.SinSesion();
        }

        return Ok(new PedidosDelMiembro(await lectura.DelMiembroAsync(miembro, cancellationToken)));
    }

    /// <summary>El seguimiento de un pedido por su referencia.</summary>
    [HttpGet("pedidos/{referencia}/seguimiento")]
    public async Task<IActionResult> Seguimiento(string vertical, string referencia, CancellationToken cancellationToken)
    {
        if (_lecturas.DelVertical(vertical) is not { } lectura)
        {
            return this.SinLectura();
        }

        return this.ComoRespuesta(await lectura.SeguimientoAsync(referencia, _sesion, cancellationToken));
    }
}
