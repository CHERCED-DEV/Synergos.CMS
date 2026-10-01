using Umbraco.Cms.Core.Models.PublishedContent;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Qué hijos del <c>siteRoot</c> salen como enlace en la navegación del sitio (header y footer de
/// <c>_Layout</c>).
/// </summary>
/// <remarks>
/// <para><b>Sólo las PÁGINAS, y una página es lo que Umbraco sabe pintar: un nodo con
/// plantilla</b> (#185). Antes salía todo hijo visible, y el <c>siteRoot</c> no tiene sólo
/// páginas: la carpeta <c>siteConfigFolder</c> es el único padre que el schema permite para los
/// avisos transversales, así que el primer editor que creaba un aviso por la vía prevista ponía
/// «Configuración» en el menú, como un enlace a <c>/synergos/configuracion/</c> que contesta 404
/// (medido en vivo). <c>eventPage</c> tampoco tiene plantilla —es un dato del catálogo de
/// eventos— y podría colgar del mismo sitio.</para>
///
/// <para><b>Por qué la plantilla y no una lista de alias de carpeta</b>: una lista se queda
/// corta con el siguiente tipo de dato que el schema deje colgar del <c>siteRoot</c>, y la
/// plantilla es justo la condición del 404 — sin ella Umbraco no tiene con qué contestar. Las
/// páginas con controlador propio (<c>RenderController</c>) también tienen plantilla.</para>
///
/// <para>Es una pieza y no dos <c>.Where</c> en la vista porque el header y el footer tienen que
/// aplicar LA MISMA regla, y una vista no se puede probar.</para>
/// </remarks>
public static class NavegacionDelSitio
{
    /// <summary>
    /// Los hijos del <paramref name="siteRoot"/> que se enlazan: páginas, visibles y no ocultas
    /// por <paramref name="aliasOculto"/> (<c>hideFromMainMenu</c> o <c>hideFromFooter</c>).
    /// </summary>
    public static IReadOnlyList<IPublishedContent> Enlaces(
        IPublishedContent? siteRoot,
        string aliasOculto,
        IPublishedValueFallback fallback)
        => siteRoot?.Children?
               .Where(c => EsPagina(c)
                           && c.IsVisible(fallback)
                           && !c.Value<bool>(fallback, aliasOculto))
               .ToList()
           ?? [];

    /// <summary>Si Umbraco puede pintar el nodo: tiene plantilla.</summary>
    public static bool EsPagina(IPublishedContent nodo) => nodo.TemplateId is > 0;
}
