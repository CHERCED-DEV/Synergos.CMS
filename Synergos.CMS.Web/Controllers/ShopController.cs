using Microsoft.AspNetCore.Mvc;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Controllers;

/// <summary>
/// Endpoints públicos del cart de compra. Mutators escriben la cookie
/// firmada vía <see cref="ICartService"/>; el reader devuelve el estado
/// hidratado actual.
/// </summary>
/// <remarks>
/// Routing: <c>/api/shop/cart/*</c>. Sin autorización — el cart es del
/// visitante anónimo (cookie HMAC firmada). Si en el futuro se quiere
/// asociar a un member autenticado, una variante de <see cref="ICartService"/>
/// puede mover el storage al member tree.
///
/// Sin CSRF token — los endpoints son idempotentes (set/remove qty).
/// El HMAC de la cookie previene tampering. Para sitios con
/// requirements estrictos de CSRF, agregar [ValidateAntiForgeryToken]
/// y configurar el design-system frontend para enviar el token.
/// </remarks>
[ApiController]
[Route("api/shop/cart")]
public sealed class ShopController : ControllerBase
{
    private readonly ICartService _cartService;

    public ShopController(ICartService cartService) => _cartService = cartService;

    /// <summary>GET /api/shop/cart — devuelve el cart actual hidratado.</summary>
    [HttpGet]
    public ActionResult<Cart> GetCart() => _cartService.GetCart();

    /// <summary>POST /api/shop/cart/add — agrega un item al cart.</summary>
    [HttpPost("add")]
    public ActionResult<Cart> AddItem([FromBody] CartAddDto dto)
    {
        if (dto is null || string.IsNullOrWhiteSpace(dto.Sku) || dto.Quantity <= 0)
        {
            return BadRequest(new { error = "sku and quantity > 0 required" });
        }
        return _cartService.AddItem(dto.Sku, dto.Quantity, dto.VariantSku);
    }

    /// <summary>POST /api/shop/cart/update — fija la cantidad de un item.</summary>
    [HttpPost("update")]
    public ActionResult<Cart> UpdateQuantity([FromBody] CartUpdateDto dto)
    {
        if (dto is null || string.IsNullOrWhiteSpace(dto.Sku))
        {
            return BadRequest(new { error = "sku required" });
        }
        return _cartService.UpdateQuantity(dto.Sku, dto.Quantity, dto.VariantSku);
    }

    /// <summary>POST /api/shop/cart/remove — remueve un item del cart.</summary>
    [HttpPost("remove")]
    public ActionResult<Cart> RemoveItem([FromBody] CartRemoveDto dto)
    {
        if (dto is null || string.IsNullOrWhiteSpace(dto.Sku))
        {
            return BadRequest(new { error = "sku required" });
        }
        return _cartService.RemoveItem(dto.Sku, dto.VariantSku);
    }

    /// <summary>POST /api/shop/cart/clear — vacía el cart completo.</summary>
    [HttpPost("clear")]
    public ActionResult<Cart> Clear() => _cartService.Clear();
}

/// <summary>POST body para <c>/cart/add</c>.</summary>
public sealed record CartAddDto(string Sku, int Quantity, string? VariantSku);

/// <summary>POST body para <c>/cart/update</c>.</summary>
public sealed record CartUpdateDto(string Sku, int Quantity, string? VariantSku);

/// <summary>POST body para <c>/cart/remove</c>.</summary>
public sealed record CartRemoveDto(string Sku, string? VariantSku);
