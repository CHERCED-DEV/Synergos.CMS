using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Web.Services.Catalog;
using Synergos.CMS.Web.Services.Gobierno;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Services;

namespace Synergos.CMS.Web.Notifications;

/// <summary>
/// Al despublicar, mandar a la papelera o borrar un <c>tramitePage</c> —o un nodo que lo contiene—, retira su oferta del
/// orquestador de Gobierno: el trámite deja de radicarse por la puerta (009-gobierno-tramite T034). El molde de
/// <see cref="OfertaDeCursoAlRetirar"/>.
/// </summary>
/// <remarks>
/// <para><b>Por qué hace falta.</b> La radicación por la puerta cotiza contra la tasa que el orquestador publicó, no contra
/// la ficha: un trámite despublicado se seguiría radicando. Una radicación ya abierta se cierra igual, con la tasa que se
/// le cotizó; abrir otra se rechaza.</para>
///
/// <para><b>Con sus descendientes</b> al despublicar o mandar a la papelera; al borrar ya no hay descendientes que
/// leer. <b>Sólo con el catálogo en el contenido</b>, como publicar, y <b>nunca tumba</b> la acción del editor: lo que
/// falle queda en el log y se repara retirándolo otra vez.</para>
/// </remarks>
public sealed class OfertaDeTramiteAlRetirar :
    INotificationAsyncHandler<ContentUnpublishedNotification>,
    INotificationAsyncHandler<ContentMovedToRecycleBinNotification>,
    INotificationAsyncHandler<ContentDeletedNotification>
{
    private const int Pagina = 500;

    private readonly IPublicadorDeOfertasDeGobierno _ofertas;
    private readonly IContentService _contenidos;
    private readonly IOptionsMonitor<CatalogSettings> _fuentes;
    private readonly ILogger<OfertaDeTramiteAlRetirar> _log;

    public OfertaDeTramiteAlRetirar(
        IPublicadorDeOfertasDeGobierno ofertas,
        IContentService contenidos,
        IOptionsMonitor<CatalogSettings> fuentes,
        ILogger<OfertaDeTramiteAlRetirar> log)
    {
        _ofertas = ofertas;
        _contenidos = contenidos;
        _fuentes = fuentes;
        _log = log;
    }

    public Task HandleAsync(ContentUnpublishedNotification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        return RetirarAsync(notification.UnpublishedEntities, conDescendientes: true, cancellationToken);
    }

    public Task HandleAsync(ContentMovedToRecycleBinNotification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        return RetirarAsync(notification.MoveInfoCollection.Select(m => m.Entity), conDescendientes: true, cancellationToken);
    }

    public Task HandleAsync(ContentDeletedNotification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        return RetirarAsync(notification.DeletedEntities, conDescendientes: false, cancellationToken);
    }

    private async Task RetirarAsync(IEnumerable<IContent> nodos, bool conDescendientes, CancellationToken ct)
    {
        if (!OfertaDeTramiteAlPublicar.DelContenido(_fuentes)) return;

        var tramites = new HashSet<string>(StringComparer.Ordinal);
        foreach (var nodo in nodos)
        {
            Anotar(nodo, tramites);
            if (conDescendientes) Descendientes(nodo, tramites);
        }

        foreach (var clave in tramites)
        {
            try
            {
                await _ofertas.RetirarAsync(clave, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogWarning(ex, "No se pudo retirar la oferta del trámite {Tramite}. Se repara retirándolo otra vez.", clave);
            }
        }
    }

    private void Descendientes(IContent nodo, HashSet<string> tramites)
    {
        try
        {
            long total;
            var pagina = 0L;
            do
            {
                foreach (var hijo in _contenidos.GetPagedDescendants(nodo.Id, pagina++, Pagina, out total)) Anotar(hijo, tramites);
            }
            while (pagina * Pagina < total);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning(ex, "No se pudieron leer los descendientes de {Nodo} para retirar sus ofertas de trámites.", nodo.Id);
        }
    }

    private static void Anotar(IContent nodo, HashSet<string> tramites)
    {
        if (string.Equals(nodo.ContentType.Alias, UmbracoTramiteCatalogSource.TramitePageAlias, StringComparison.Ordinal))
        {
            tramites.Add(nodo.Key.ToString());
        }
    }
}
