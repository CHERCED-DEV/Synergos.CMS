using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services;
using Synergos.CMS.Web.Services.Puerta;

namespace Synergos.CMS.Web.Controllers;

/// <summary>
/// El registro de las compras de la tienda hechas por la puerta (004-tienda-compra, US 6): lo que el CMS agrega a
/// una compra que abre, cobra y despacha el orquestador, para que la vean «Mis compras», el seguimiento, la
/// devolución y las reseñas.
/// </summary>
/// <remarks>
/// <code>
/// POST /api/shop/compras/{id}/anotar   → { orderRef, numero, estado }        🔒 sesión · mismo origen
/// GET  /api/shop/compras/{id}          → la orden como la lee «Mis compras»  🔒 sesión
/// GET  /api/shop/compras/pendientes    → { compras:[{ id, numero, total, moneda, estado }] } 🔒 sesión
/// </code>
/// La compra la abre y la cierra la PUERTA (<c>/api/flujos/tienda.compra/…</c>); estas rutas no la tocan: la leen
/// al orquestador con el sujeto del miembro de la sesión, así que una ajena es un 404. Nadie guarda la respuesta.
/// </remarks>
[ApiController]
[Route("api/shop/compras")]
public sealed class ComprasDeLaTiendaController : ControllerBase
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private readonly RegistroDeComprasDeLaTienda _registro;
    private readonly IMemberAccessGate _gate;

    public ComprasDeLaTiendaController(RegistroDeComprasDeLaTienda registro, IMemberAccessGate gate)
    {
        _registro = registro;
        _gate = gate;
    }

    [HttpPost("{id}/anotar")]
    public async Task<IActionResult> Anotar(string id, CancellationToken cancellationToken)
    {
        SinCache();
        if (Miembro() is { } sinSesion) return sinSesion;
        if (FiltroMismoOrigenJson.Origen(Request) is { } ajeno) return Problema(ajeno.Estado, ajeno.Codigo, ajeno.Mensaje, ajeno.Transitorio);

        var r = await _registro.AnotarAsync(id, _gate.CurrentMemberKey!.Value, _gate.CurrentMemberDisplayName, _gate.CurrentMemberEmail, cancellationToken);
        return r.Bien ? Ok(new { orderRef = r.Orden!.OrderRef, numero = r.Orden.OrderNumber, estado = r.Orden.Status.ToString() }) : Problema(r);
    }

    [HttpGet("pendientes")]
    public async Task<IActionResult> Pendientes(CancellationToken cancellationToken)
    {
        SinCache();
        if (Miembro() is { } sinSesion) return sinSesion;

        var (compras, fallo) = await _registro.PendientesAsync(_gate.CurrentMemberKey!.Value, cancellationToken);
        return fallo is not null
            ? Problema(fallo)
            : Ok(new { compras = compras.Select(c => new { id = c.Id, numero = c.Numero, total = c.Total, moneda = c.Moneda, estado = c.Estado }) });
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Compra(string id, CancellationToken cancellationToken)
    {
        SinCache();
        if (Miembro() is { } sinSesion) return sinSesion;

        var r = await _registro.CompraAsync(id, _gate.CurrentMemberKey!.Value, _gate.CurrentMemberDisplayName, _gate.CurrentMemberEmail, cancellationToken);
        if (!r.Bien) return Problema(r);

        var o = r.Orden!;
        return Ok(new
        {
            orderRef = o.OrderRef,
            orderNumber = o.OrderNumber,
            status = o.Status.ToString(),
            total = o.Total,
            currency = o.Currency,
            items = o.Lines.Select(l => new
            {
                productId = l.ProductId,
                variantId = l.VariantId,
                title = l.ProductName,
                qty = l.Quantity,
                unitPrice = l.UnitPrice,
                lineTotal = l.LineTotal,
            }),
        });
    }

    /// <summary>Que nadie guarde la respuesta: es la compra de una persona. También el rechazo.</summary>
    private void SinCache()
    {
        if (HttpContext is { } http) http.Response.Headers.CacheControl = "no-store";
    }

    /// <summary>El miembro de la sesión con su <c>MemberKey</c>, que es el sujeto de sus compras; si no, 401.</summary>
    private ContentResult? Miembro()
        => _gate.IsAuthenticated && _gate.CurrentMemberKey is { } k && k != Guid.Empty
            ? null
            : Problema(StatusCodes.Status401Unauthorized, "puerta.sesion_requerida", "Hace falta iniciar sesión.", false);

    private static ContentResult Problema(ResultadoDelRegistro r)
        => Problema(r.Estado, r.Codigo ?? "tienda.registro", r.Mensaje ?? string.Empty, r.Transitorio);

    /// <summary>Un «no» como problem+json, con <c>code</c> y <c>transient</c> como la puerta.</summary>
    private static ContentResult Problema(int estado, string codigo, string mensaje, bool transitorio) => new()
    {
        StatusCode = estado,
        ContentType = "application/problem+json",
        Content = JsonSerializer.Serialize(new
        {
            type = "about:blank",
            title = ReasonPhrases.GetReasonPhrase(estado),
            status = estado,
            detail = mensaje,
            code = codigo,
            transient = transitorio,
        }, Web),
    };
}
