using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Web.Services.Reservas;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Web;

namespace Synergos.CMS.Web.Services.Catalog;

/// <summary>Si el catálogo del contenido ya ve una ficha publicada dentro de la tienda.</summary>
public interface ITiendaEnElContenido
{
    /// <summary>
    /// Si el catálogo, por su propio recorrido de la caché publicada, encuentra el <c>siteRoot</c> de la tienda (el
    /// <c>brandKey</c> de <c>Synergos:Catalog:Scopes:Shop</c>) y, entre sus fichas, el nodo <paramref name="nodo"/>.
    /// Nunca deja log.
    /// </summary>
    bool Ve(Guid nodo);

    /// <summary>
    /// Los SKU de las fichas de la tienda marcadas reservables (<c>compReservable</c> de 011): servicios por cita, que no
    /// se venden como productos de carrito (FR-026). Por el mismo recorrido que el catálogo; vacío sin tienda a la
    /// vista. Nunca deja log.
    /// </summary>
    IReadOnlySet<string> SkusReservables();
}

/// <summary>
/// La pregunta «¿el catálogo ve ya esta ficha en la tienda?», sin log (004-tienda-compra; el rojo del CI de
/// 2026-10-22).
/// </summary>
/// <remarks>
/// <para><b>Para quien no es una página.</b> Cuando una página pide el catálogo y no hay tienda, es una configuración
/// mal hecha, y <see cref="UmbracoProductCatalogSource"/> lo dice con un ERR. Pero al publicar fichas en lote —un
/// import del árbol entero, una restauración, la caché sin calentar— cada <c>productPage</c> puede publicarse antes de
/// que el recorrido desde las raíces alcance su <c>siteRoot</c>. Es pasajero: preguntar al catálogo ahí dejaba un ERR
/// por ficha y, peor, el manejador de la oferta la RETIRABA, y la tienda quedaba sin vender hasta republicar.</para>
///
/// <para><b>El mismo recorrido que el catálogo</b> (<see cref="UmbracoProductCatalogSource.BuscarTienda"/> y
/// <see cref="UmbracoProductCatalogSource.FichasDe"/>): mirar por los ancestros del nodo decía «la veo» mientras el
/// catálogo no la veía.</para>
///
/// <para><b>No tiene logger a propósito</b>: lo que no se ve no se registra aquí. Quien pregunta decide qué decir.</para>
/// </remarks>
public sealed class TiendaEnElContenido : ITiendaEnElContenido
{
    /// <summary>El alias con que el catálogo lee el SKU de una ficha (su <c>Id</c> en el catálogo).</summary>
    private const string PropiedadDelSku = "productSku";

    private readonly IUmbracoContextAccessor _contexto;
    private readonly IOptionsMonitor<CatalogSettings> _ajustes;
    private readonly IVariationContextAccessor _variacion;

    public TiendaEnElContenido(
        IUmbracoContextAccessor contexto,
        IOptionsMonitor<CatalogSettings> ajustes,
        IVariationContextAccessor variacion)
    {
        _contexto = contexto;
        _ajustes = ajustes;
        _variacion = variacion;
    }

    /// <inheritdoc />
    public bool Ve(Guid nodo)
    {
        var marca = UmbracoProductCatalogSource.MarcaDeLaTienda(_ajustes.CurrentValue);
        if (marca is null || !_contexto.TryGetUmbracoContext(out var contexto) || contexto.Content is not { } cache)
        {
            return false;
        }

        var tienda = UmbracoProductCatalogSource.BuscarTienda(cache, marca, _variacion);
        return tienda is not null
            && UmbracoProductCatalogSource.FichasDe(tienda, _variacion).Any(ficha => ficha.Key == nodo);
    }

    /// <inheritdoc />
    public IReadOnlySet<string> SkusReservables()
    {
        var marca = UmbracoProductCatalogSource.MarcaDeLaTienda(_ajustes.CurrentValue);
        if (marca is null || !_contexto.TryGetUmbracoContext(out var contexto) || contexto.Content is not { } cache)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        var tienda = UmbracoProductCatalogSource.BuscarTienda(cache, marca, _variacion);
        if (tienda is null)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        return UmbracoProductCatalogSource.FichasDe(tienda, _variacion)
            .Where(ficha => Marcada(ficha.GetProperty(FichaReservable.Marca)?.GetValue()))
            .Select(ficha => ficha.GetProperty(PropiedadDelSku)?.GetValue()?.ToString()?.Trim())
            .Where(sku => !string.IsNullOrEmpty(sku))
            .Select(sku => sku!)
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>La marca en verdadero, con los mismos valores que acepta <see cref="FichaReservable.Leer"/>.</summary>
    private static bool Marcada(object? valor) => valor switch
    {
        bool b => b,
        int i => i == 1,
        string s => s.Trim() is "1" or "true" or "True",
        _ => false,
    };
}
