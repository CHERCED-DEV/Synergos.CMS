using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Umbraco.Cms.Core.Web;

namespace Synergos.CMS.Web.Services.SynHost;

/// <summary>Cómo pinta su cabecera la app de la página en curso (D-016-V2): <c>secundaria</c> o <c>completa</c>.</summary>
public interface ICabeceraDeLasApps
{
    /// <summary>El modo para la app de la página en curso; sin petición, la completa.</summary>
    string Actual();
}

/// <summary>
/// Cómo pinta su cabecera la app de la página en curso (D-016-V2): lo que declara su sitio
/// (<see cref="CabeceraFeatureSettings"/>) si la página pinta la cabecera del sitio; si no, la completa.
/// </summary>
/// <remarks>
/// El sitio es el siteRoot de la PÁGINA, no el del dominio (<c>INegocioDelSitio</c>): la base no tiene dominios, y por
/// dominio regiría siempre la base (lo mismo que la regla del carrito, <see cref="CompraFeatureSettings"/>).
/// </remarks>
public sealed class CabeceraDeLasApps(
    IUmbracoContextAccessor contexto,
    IPageRenderContextResolver pagina,
    IOptionsMonitor<CabeceraFeatureSettings> seccion) : ICabeceraDeLasApps
{
    /// <summary>El modo para la app de la página en curso; sin petición, la completa.</summary>
    public string Actual()
    {
        if (!contexto.TryGetUmbracoContext(out var umbraco) || umbraco.PublishedRequest?.PublishedContent is not { } actual)
        {
            return ModoDeCabecera.Completa;
        }
        var declarado = seccion.CurrentValue.Para(actual.AncestorOrSelf("siteRoot")?.Key).Modo;
        return ModoDeCabecera.Decidir(pagina.Resolve().ShowHeader, declarado);
    }
}
