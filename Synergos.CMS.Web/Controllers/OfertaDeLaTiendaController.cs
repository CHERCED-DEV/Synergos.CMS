using Microsoft.AspNetCore.Mvc;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services.Catalog;

namespace Synergos.CMS.Web.Controllers;

/// <summary>
/// Republicar la oferta del catálogo entero de la tienda en su orquestador (004-tienda-compra, R-9), a mano.
/// </summary>
/// <remarks>
/// <code>
/// POST /api/shop/oferta/republicar → { total, publicados, fallidos, sinDestino, reservables }   🔒 personal de la tienda
/// </code>
/// Recorre el catálogo activo y publica la oferta de cada producto: es la reparación de un publicar que no
/// llegó y la forma de poner a la venta la siembra de la demo, que ningún editor publica. NUNCA corre al
/// arrancar (ADR 0013): lo invoca una persona. Sólo el personal de la tienda (el rol de la consola de
/// devoluciones), porque escribe la oferta de TODOS los productos. El molde de
/// <c>POST /api/eventos/oferta/republicar</c>.
///
/// Una ficha marcada reservable (FR-026, la nota de 011) se salta y se cuenta en <c>reservables</c>: es un servicio
/// por cita y su oferta es la de <c>Bff.Reservas</c>, no la de un producto de carrito.
/// </remarks>
[ApiController]
[Route("api/shop/oferta")]
public sealed class OfertaDeLaTiendaController : ControllerBase
{
    /// <summary>De a cuántos productos se lee el catálogo: el tope de una página de búsqueda.</summary>
    internal const int Pagina = 48;

    private readonly IProductCatalogProvider _catalogo;
    private readonly ITiendaOfferPublisher _ofertas;
    private readonly IMemberAccessGate _gate;
    private readonly ITiendaEnElContenido _tienda;
    private readonly ILogger<OfertaDeLaTiendaController> _log;

    public OfertaDeLaTiendaController(
        IProductCatalogProvider catalogo,
        ITiendaOfferPublisher ofertas,
        IMemberAccessGate gate,
        ITiendaEnElContenido tienda,
        ILogger<OfertaDeLaTiendaController> log)
    {
        _catalogo = catalogo;
        _ofertas = ofertas;
        _gate = gate;
        _tienda = tienda;
        _log = log;
    }

    [HttpPost("republicar")]
    public async Task<IActionResult> Republicar(CancellationToken cancellationToken)
    {
        if (!_gate.IsAuthenticated)
        {
            return Unauthorized(new { error = "Inicie sesión como personal de la tienda." });
        }
        if (!_gate.HasAnyRole("admin"))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Republicar la oferta es del personal de la tienda." });
        }

        // Se avanza por lo que llegó y no por lo pedido: si el catálogo topa la página más abajo, saltar de a
        // Pagina dejaría productos sin publicar en silencio.
        var ids = new List<string>();
        for (var desde = 0; ;)
        {
            var pagina = await _catalogo.SearchAsync(new ProductQuery(Skip: desde, Take: Pagina), cancellationToken);
            ids.AddRange(pagina.Products.Select(p => p.Id));
            desde += pagina.Products.Count;
            if (pagina.Products.Count == 0 || desde >= pagina.Total) break;
        }

        var reservables = _tienda.SkusReservables();
        int publicados = 0, fallidos = 0, sinDestino = 0, saltados = 0;
        foreach (var id in ids.Distinct(StringComparer.Ordinal))
        {
            if (reservables.Contains(id))
            {
                saltados++;
                continue;
            }

            var producto = await _catalogo.GetProductAsync(id, cancellationToken);
            if (producto is null)
            {
                fallidos++;
                _log.LogWarning("Republicar: el catálogo lista {Producto} y no sirve su ficha.", id);
                continue;
            }

            switch (await _ofertas.PublishAsync(producto, cancellationToken))
            {
                case TiendaOfferOutcome.Published: publicados++; break;
                case TiendaOfferOutcome.NoDestination: sinDestino++; break;
                default: fallidos++; break;
            }
        }

        return Ok(new RepublicarOfertaResponse(
            ids.Distinct(StringComparer.Ordinal).Count(), publicados, fallidos, sinDestino, saltados));
    }

    /// <summary>
    /// Cómo terminó republicar: cuántos productos se recorrieron y qué pasó con cada uno. <c>Reservables</c>: las
    /// fichas que son servicios por cita y no se publicaron como productos (FR-026).
    /// </summary>
    public sealed record RepublicarOfertaResponse(int Total, int Publicados, int Fallidos, int SinDestino, int Reservables);
}
