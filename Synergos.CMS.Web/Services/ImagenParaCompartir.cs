using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.Blocks;
using Umbraco.Cms.Core.Models.PublishedContent;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// La imagen con la que se comparte una página (<c>og:image</c>) cuando la página no trae <c>ogImage</c> (D-016-X1):
/// ninguna de las 124 páginas tenía una, y no existe ningún <c>siteConfigSettings</c> que diera la de respaldo, así que
/// un enlace compartido salía sin foto. Se toma la imagen propia de la página (la del producto, el equipo, el inmueble)
/// y, si no la tiene, la primera de sus bloques (casi siempre el hero), recortada al tamaño que usan las redes.
/// </summary>
public static class ImagenParaCompartir
{
    /// <summary>Las propiedades con la imagen propia de una página, en orden de preferencia.</summary>
    public static readonly IReadOnlyList<string> PropiedadesPropias = ["productImages", "equipmentCover", "propertyImages"];

    private const string Bloques = "sections";
    private const string ImagenDelBloque = "mediaReference";

    /// <summary>
    /// La URL absoluta de la imagen para compartir: la de la página; si no tiene, la del primer hijo que tenga una (una
    /// categoría muestra un producto); luego la de su sitio, o la primera imagen propia de ese sitio (las vistas de una
    /// app, como el carrito); en la raíz de la plataforma, la de su primer sitio. Null si nada de eso tiene imagen.
    /// </summary>
    public static string? DeLaPagina(IPublishedContent? pagina)
    {
        if (pagina is null) { return null; }
        if (DeSiMisma(pagina) is { } propia) { return propia; }
        if (pagina.Children?.Select(DeSuPropiedad).FirstOrDefault(u => u is not null) is { } delHijo) { return delHijo; }

        if (pagina.AncestorOrSelf(SitioAlias) is { } sitio)
        {
            return (sitio.Id != pagina.Id ? DeSiMisma(sitio) : null) ?? DentroDelSitio(sitio);
        }
        // La raíz de la plataforma (el lanzador): la del primero de sus sitios que tenga una.
        return pagina.Children?
            .Where(c => c.ContentType.Alias == SitioAlias)
            .Select(s => DeSiMisma(s) ?? DentroDelSitio(s))
            .FirstOrDefault(u => u is not null);
    }

    private static string? DentroDelSitio(IPublishedContent sitio) =>
        sitio.Descendants().Take(MaximoDeNodosDelSitio).Select(DeSiMisma).FirstOrDefault(u => u is not null);

    private const string SitioAlias = "siteRoot";

    /// <summary>Cuántos nodos de un sitio se miran, como mucho, buscando una imagen propia.</summary>
    private const int MaximoDeNodosDelSitio = 200;

    /// <summary>La imagen propia de la página o la de su primer bloque.</summary>
    private static string? DeSiMisma(IPublishedContent pagina) => DeSuPropiedad(pagina) ?? DeSusBloques(pagina);

    private static string? DeSuPropiedad(IPublishedContent pagina)
    {
        foreach (var alias in PropiedadesPropias)
        {
            if (pagina.HasProperty(alias) && MediaPickerReader.ReadFirstMediaUrl(pagina, alias, UrlMode.Absolute) is { Length: > 0 } propia)
            {
                return ParaCompartir(propia);
            }
        }
        return null;
    }

    private static string? DeSusBloques(IPublishedContent pagina)
    {
        if (pagina.HasProperty(Bloques) && pagina.Value<BlockGridModel>(Bloques) is { } bloques)
        {
            foreach (var elemento in Elementos(bloques))
            {
                if (elemento.HasProperty(ImagenDelBloque) && PrimeraUrl(elemento) is { Length: > 0 } delBloque)
                {
                    return ParaCompartir(delBloque);
                }
            }
        }
        return null;
    }

    /// <summary>La URL pedida a ImageSharp en 1200 × 630, recortada: el tamaño que usan las redes para la vista previa.</summary>
    public static string ParaCompartir(string url) =>
        url + (url.Contains('?', StringComparison.Ordinal) ? "&" : "?") + "width=1200&height=630&rmode=crop";

    private static IEnumerable<IPublishedElement> Elementos(IEnumerable<BlockGridItem> items)
    {
        foreach (var item in items)
        {
            yield return item.Content;
            foreach (var area in item.Areas)
            {
                foreach (var hijo in Elementos(area))
                {
                    yield return hijo;
                }
            }
        }
    }

    private static string? PrimeraUrl(IPublishedElement elemento)
    {
        var media = elemento.Value<IEnumerable<MediaWithCrops>>(ImagenDelBloque)?.FirstOrDefault()
            ?? (IPublishedContent?)elemento.Value<MediaWithCrops>(ImagenDelBloque)
            ?? elemento.Value<IPublishedContent>(ImagenDelBloque);
        return media?.Url(mode: UrlMode.Absolute);
    }
}
