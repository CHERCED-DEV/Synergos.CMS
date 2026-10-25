using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services.Catalog;
using Synergos.CMS.Web.Services.Gobierno;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;

namespace Synergos.CMS.Web.Notifications;

/// <summary>
/// Al publicar un <c>tramitePage</c>, publica su oferta en el orquestador de Gobierno (009-gobierno-tramite T034): un
/// trámite nuevo queda radicable por la puerta, con la tasa que publicó la entidad, sin tocar código ni configuración. El
/// molde de <see cref="OfertaDeCursoAlPublicar"/>.
/// </summary>
/// <remarks>
/// <para><b>Sólo con el catálogo en el contenido</b> (<c>Synergos:Catalog:Sources:Gov = cms</c>): en <c>demo</c> el
/// catálogo es la siembra, sin clave del nodo, y un <c>tramitePage</c> no se radica por la puerta.</para>
///
/// <para><b>Lee el trámite por el catálogo y no del contenido que llega</b>: la oferta sale de la MISMA proyección que
/// pinta la ficha, con la tasa ya resuelta por <see cref="TramiteContentRules.TryResolveFee"/>. Un trámite que el catálogo
/// omite (sin slug, sin nombre, o sin marcar gratuito y con tasa en cero) no tiene ficha, y tampoco oferta: la que
/// tuviera se RETIRA, y el motivo ya lo dejó el catálogo en el log como error.</para>
///
/// <para><b>Antes de pedir el trámite, pregunta si el catálogo ya lo ve en el portal</b>
/// (<see cref="IPortalDeTramitesEnElContenido"/>, sin log): al publicar en lote una ficha puede publicarse antes de que
/// el catálogo alcance su <c>siteRoot</c>, y entonces no se retira nada.</para>
///
/// <para><b>Nunca tumba el publicar</b>: el contenido ya se guardó cuando esto corre. Lo que falle queda en el log, y
/// se repara republicando.</para>
/// </remarks>
public sealed class OfertaDeTramiteAlPublicar : INotificationAsyncHandler<ContentPublishedNotification>
{
    private readonly ITramiteCatalogProvider _catalogo;
    private readonly IPublicadorDeOfertasDeGobierno _ofertas;
    private readonly IOptionsMonitor<CatalogSettings> _fuentes;
    private readonly IPortalDeTramitesEnElContenido _portal;
    private readonly ILogger<OfertaDeTramiteAlPublicar> _log;

    public OfertaDeTramiteAlPublicar(
        ITramiteCatalogProvider catalogo,
        IPublicadorDeOfertasDeGobierno ofertas,
        IOptionsMonitor<CatalogSettings> fuentes,
        IPortalDeTramitesEnElContenido portal,
        ILogger<OfertaDeTramiteAlPublicar> log)
    {
        _catalogo = catalogo;
        _ofertas = ofertas;
        _fuentes = fuentes;
        _portal = portal;
        _log = log;
    }

    public async Task HandleAsync(ContentPublishedNotification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);

        var tramites = notification.PublishedEntities
            .Where(c => string.Equals(c.ContentType.Alias, UmbracoTramiteCatalogSource.TramitePageAlias, StringComparison.Ordinal))
            .ToList();
        if (tramites.Count == 0 || !DelContenido(_fuentes)) return;

        foreach (var contenido in tramites)
        {
            var clave = contenido.Key.ToString();
            if (!_portal.Ve(contenido.Key))
            {
                _log.LogWarning(
                    "Se publicó el trámite {Tramite} y el catálogo no lo ve en el portal (todavía no está en la caché, como en "
                    + "un import, o es de otro sitio): no se toca su oferta. Si es del portal, se repara republicándolo.", clave);
                continue;
            }

            try
            {
                var tramite = await _catalogo.GetAsync(clave, cancellationToken).ConfigureAwait(false);
                if (tramite is null)
                {
                    _log.LogWarning(
                        "Se publicó el trámite {Tramite} y el catálogo no lo sirve: se retira su oferta. Ver el log del catálogo.", clave);
                    await _ofertas.RetirarAsync(clave, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                await _ofertas.PublicarAsync(tramite, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogWarning(ex, "No se pudo publicar la oferta del trámite {Tramite}. Se repara republicándolo.", clave);
            }
        }
    }

    /// <summary>Si los trámites salen del contenido: sólo entonces un <c>tramitePage</c> se radica por la puerta.</summary>
    internal static bool DelContenido(IOptionsMonitor<CatalogSettings> fuentes)
        => fuentes.CurrentValue.Sources.TryGetValue(UmbracoTramiteCatalogSource.Vertical, out var fuente)
           && string.Equals(fuente, "cms", StringComparison.OrdinalIgnoreCase);
}
