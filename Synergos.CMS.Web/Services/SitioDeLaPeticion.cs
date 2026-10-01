using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Routing;
using Umbraco.Cms.Core.Web;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// El sitio —el <c>siteRoot</c>— y la cultura de una petición que Umbraco NO ruteó: la que pinta
/// un controlador MVC propio (<c>/blog/tag/*</c>, <c>/error/*</c>).
/// </summary>
/// <remarks>
/// <para><b>Por qué hace falta</b> (#190, #188). Una página de contenido llega con su sitio y su
/// cultura resueltos por Umbraco: el router mira el hostname contra los dominios asignados y, si
/// ninguno casa, usa la cultura por defecto. Un controlador MVC no pasa por ese router, así que
/// no hay ni una cosa ni la otra: la petición se queda con la cultura por defecto de ASP.NET
/// —<c>en-US</c>— y quien buscaba «su sitio» tomaba el primero del árbol. Medido en vivo:
/// <c>/blog/tag/*</c> y <c>/error/404</c> publicaban el bridge en <c>en-US</c> con
/// <c>&lt;html lang="en"&gt;</c>, y la página de error de un sitio con dominio propio era la del
/// primer sitio.</para>
///
/// <para><b>Se resuelve como lo resuelve Umbraco, con sus piezas</b>: los dominios del caché
/// publicado, <see cref="DomainUtilities.SelectDomain"/> contra la URL de la petición —la misma
/// llamada del router— y, sin dominio, la cultura por defecto del caché, que sale de
/// Idiomas → default y no de un literal. El sitio es el <c>siteRoot</c> que contiene el nodo del
/// dominio (§0.A.8: multi-sitio por hostname nativo de Umbraco; nada de un resolvedor de
/// inquilinos propio).</para>
///
/// <para><b>Sin dominio que case no hay sitio</b> —<see cref="SitioResuelto.SiteRoot"/> nulo— y
/// cada consumidor decide su respaldo. No se adivina por la ruta: con los sitios colgando por
/// ruta del mismo <c>platformRoot</c>, <c>/blog/tag/x</c> no pertenece a ninguno.</para>
/// </remarks>
public sealed class SitioDeLaPeticion
{
    private const string SiteRootAlias = "siteRoot";

    private readonly IUmbracoContextAccessor _accesor;

    public SitioDeLaPeticion(IUmbracoContextAccessor accesor) => _accesor = accesor;

    /// <summary>El sitio y la cultura de la petición en curso.</summary>
    public SitioResuelto Resolver()
    {
        if (!_accesor.TryGetUmbracoContext(out var contexto))
        {
            return SitioResuelto.Ninguno;
        }

        var dominios = contexto.Domains;
        var porDefecto = dominios?.DefaultCulture;
        var dominio = DomainUtilities.SelectDomain(
            dominios?.GetAll(false), contexto.CleanedUmbracoUrl, defaultCulture: porDefecto);

        if (dominio is null)
        {
            return new SitioResuelto(null, porDefecto);
        }

        var nodo = contexto.Content?.GetById(dominio.ContentId);
        return new SitioResuelto(nodo?.AncestorOrSelf(SiteRootAlias), dominio.Culture ?? porDefecto);
    }
}

/// <summary>Lo que <see cref="SitioDeLaPeticion"/> sabe de la petición.</summary>
/// <param name="SiteRoot">El <c>siteRoot</c> del dominio de la petición; nulo sin dominio que case.</param>
/// <param name="Cultura">La cultura del dominio, o la por defecto; nula sin contexto de Umbraco.</param>
public sealed record SitioResuelto(IPublishedContent? SiteRoot, string? Cultura)
{
    /// <summary>Sin contexto de Umbraco no se sabe nada.</summary>
    public static SitioResuelto Ninguno { get; } = new(null, null);
}
