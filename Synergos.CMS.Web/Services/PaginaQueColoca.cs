using Umbraco.Cms.Core.Models.Blocks;
using Umbraco.Cms.Core.Models.PublishedContent;
using AliasesDeEditor = Umbraco.Cms.Core.Constants.PropertyEditors.Aliases;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// La página que coloca un bloque —un ElementType— en alguno de sus BlockGrid o BlockList. Es
/// cómo el chrome enlaza una página que el EDITOR compone, en vez de escribir su ruta a mano.
/// </summary>
/// <remarks>
/// <para><b>El defecto que cierra</b> (Synergos.CMS#187): el botón del carrito de
/// <c>_Layout.cshtml</c> apuntaba a <c>/tienda/carrito</c> escrito a mano desde la Fase 0
/// (<c>3b10fd96</c>), en la Tienda, en Booking y en Propiedades. Esa página no existió nunca:
/// ningún contenido coloca el resumen del carrito (0 bloques en la base) y la historia no la
/// siembra en ningún commit. Un rastreo desde la portada lo encontró en 404, enlazado desde cada
/// página de los tres sitios.</para>
///
/// <para><b>Lo que hace ahora el chrome</b>: enlaza la página del sitio que coloca el bloque, y si
/// no hay ninguna no enlaza nada (ADR 0112: se degrada por ausencia). Cuando un editor componga la
/// página, el enlace aparece solo, con la URL que él eligió.</para>
///
/// <para>Sólo convierte las propiedades BlockGrid/BlockList: convertir todas pagaría el valor de
/// cada texto rico y cada medio de cada página para mirar dos editores.</para>
/// </remarks>
public static class PaginaQueColoca
{
    /// <summary>La primera de <paramref name="paginas"/> que coloca <paramref name="aliasDelBloque"/>; <c>null</c> si ninguna.</summary>
    public static IPublishedContent? Primera(IEnumerable<IPublishedContent> paginas, string aliasDelBloque)
    {
        ArgumentNullException.ThrowIfNull(paginas);
        ArgumentException.ThrowIfNullOrWhiteSpace(aliasDelBloque);
        return paginas.FirstOrDefault(p => Coloca(p, aliasDelBloque));
    }

    /// <summary>Si <paramref name="pagina"/> coloca <paramref name="aliasDelBloque"/>, a cualquier profundidad de áreas.</summary>
    public static bool Coloca(IPublishedElement pagina, string aliasDelBloque)
    {
        ArgumentNullException.ThrowIfNull(pagina);
        return pagina.Properties
            .Where(p => p.PropertyType.EditorAlias is AliasesDeEditor.BlockGrid or AliasesDeEditor.BlockList)
            .Select(p => p.GetValue())
            .Any(valor => valor switch
            {
                BlockGridModel grid => EnLaRejilla(grid, aliasDelBloque),
                BlockListModel lista => lista.Any(b => EsEl(b.Content, aliasDelBloque)),
                _ => false,
            });
    }

    private static bool EnLaRejilla(IEnumerable<BlockGridItem> bloques, string alias)
        => bloques.Any(b => EsEl(b.Content, alias) || b.Areas.Any(area => EnLaRejilla(area, alias)));

    private static bool EsEl(IPublishedElement? bloque, string alias)
        => string.Equals(bloque?.ContentType.Alias, alias, StringComparison.Ordinal);
}
