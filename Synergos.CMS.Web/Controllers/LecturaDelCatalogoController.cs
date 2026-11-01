using Microsoft.AspNetCore.Mvc;
using Synergos.CMS.Interfaces.Lecturas;

namespace Synergos.CMS.Web.Controllers;

/// <summary>
/// El listado facetado y la ficha de un catálogo, con forma neutra de vertical (014-despiece-tienda, I4). La tienda los
/// leía de <c>GET api/shop/search</c> y <c>GET api/shop/product/{id}</c>, que siguen como interinas.
/// </summary>
/// <remarks>
/// Los filtros van por el campo de cada faceta, el mismo que trae el listado: <c>?filtro.category=Hogar,Deportes</c>.
/// Varios valores de un campo son la unión, como en el motor.
/// </remarks>
[ApiController]
[Route("api/lecturas/{vertical}")]
public sealed class LecturaDelCatalogoController : ControllerBase
{
    /// <summary>Elementos por página si no se pide otra cosa: los de la tienda.</summary>
    private const int PorPaginaPorDefecto = 12;

    private const string PrefijoDelFiltro = "filtro.";

    private readonly IEnumerable<ILecturaDelCatalogo> _lecturas;

    /// <summary>Con las lecturas del catálogo que declaran los verticales.</summary>
    public LecturaDelCatalogoController(IEnumerable<ILecturaDelCatalogo> lecturas)
    {
        _lecturas = lecturas ?? throw new ArgumentNullException(nameof(lecturas));
    }

    /// <summary>Una página del listado del catálogo del vertical, con sus facetas.</summary>
    [HttpGet("catalogo")]
    public async Task<IActionResult> Listado(
        string vertical,
        [FromQuery] string? texto,
        [FromQuery] string? orden,
        [FromQuery] int? pagina,
        [FromQuery] int? porPagina,
        CancellationToken cancellationToken)
    {
        if (_lecturas.DelVertical(vertical) is not { } lectura)
        {
            return this.SinLectura();
        }

        var consulta = new ConsultaDelCatalogo(
            Texto: string.IsNullOrWhiteSpace(texto) ? null : texto.Trim(),
            Filtros: FiltrosDe(HttpContext?.Request.Query),
            Orden: string.IsNullOrWhiteSpace(orden) ? null : orden.Trim(),
            Pagina: pagina is > 0 ? pagina.Value : 1,
            PorPagina: porPagina is > 0 ? porPagina.Value : PorPaginaPorDefecto);
        return Ok(await lectura.ListarAsync(consulta, cancellationToken));
    }

    /// <summary>La ficha de un elemento del catálogo del vertical.</summary>
    [HttpGet("catalogo/{id}")]
    public async Task<IActionResult> Ficha(string vertical, string id, CancellationToken cancellationToken)
    {
        if (_lecturas.DelVertical(vertical) is not { } lectura)
        {
            return this.SinLectura();
        }

        var ficha = await lectura.FichaAsync(id, cancellationToken);
        return ficha is null ? NotFound(new { error = "No está en el catálogo." }) : Ok(ficha);
    }

    /// <summary>Los filtros de la query: cada <c>filtro.{campo}</c> con sus valores separados por coma.</summary>
    internal static IReadOnlyDictionary<string, IReadOnlyList<string>> FiltrosDe(IQueryCollection? query)
    {
        var filtros = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        if (query is null)
        {
            return filtros;
        }

        foreach (var (clave, valores) in query)
        {
            if (clave.Length <= PrefijoDelFiltro.Length
                || !clave.StartsWith(PrefijoDelFiltro, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var lista = valores
                .SelectMany(v => (v ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (lista.Count > 0)
            {
                filtros[clave[PrefijoDelFiltro.Length..]] = lista;
            }
        }

        return filtros;
    }
}
