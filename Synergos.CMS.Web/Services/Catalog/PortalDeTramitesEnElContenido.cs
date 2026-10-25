using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Umbraco.Cms.Core.Web;

namespace Synergos.CMS.Web.Services.Catalog;

/// <summary>Si el catálogo del contenido ya ve un trámite publicado dentro del portal de trámites.</summary>
public interface IPortalDeTramitesEnElContenido
{
    /// <summary>
    /// Si el catálogo, por su propio recorrido de la caché publicada, encuentra el <c>siteRoot</c> del portal (el
    /// <c>brandKey</c> de <c>Synergos:Catalog:Scopes:Gov</c>) y, entre sus trámites, el nodo <paramref name="nodo"/>.
    /// Nunca deja log.
    /// </summary>
    bool Ve(Guid nodo);
}

/// <summary>
/// La pregunta «¿el catálogo ve ya este trámite en el portal?», sin log (009-gobierno-tramite T034). El molde de
/// <see cref="AcademiaEnElContenido"/>.
/// </summary>
/// <remarks>
/// Al publicar en lote —un import del árbol, una restauración, la caché sin calentar— un <c>tramitePage</c> puede
/// publicarse antes de que el recorrido desde las raíces alcance su <c>siteRoot</c>. Es pasajero: preguntar al catálogo
/// ahí haría que el manejador de la oferta la RETIRARA, y el trámite quedaría sin radicarse por la puerta hasta
/// republicar. Usa el mismo recorrido que el catálogo (<see cref="UmbracoTramiteCatalogSource.BuscarPortal"/>).
/// </remarks>
public sealed class PortalDeTramitesEnElContenido : IPortalDeTramitesEnElContenido
{
    private readonly IUmbracoContextAccessor _contexto;
    private readonly IOptionsMonitor<CatalogSettings> _ajustes;

    public PortalDeTramitesEnElContenido(IUmbracoContextAccessor contexto, IOptionsMonitor<CatalogSettings> ajustes)
    {
        _contexto = contexto;
        _ajustes = ajustes;
    }

    /// <inheritdoc />
    public bool Ve(Guid nodo)
    {
        var marca = UmbracoTramiteCatalogSource.MarcaDelPortal(_ajustes.CurrentValue);
        if (marca is null || !_contexto.TryGetUmbracoContext(out var contexto) || contexto.Content is not { } cache)
        {
            return false;
        }

        var portal = UmbracoTramiteCatalogSource.BuscarPortal(cache, marca);
        return portal is not null && UmbracoTramiteCatalogSource.TramitesDe(portal).Any(t => t.Key == nodo);
    }
}
