using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Services;

namespace Synergos.CMS.Web.Notifications;

/// <summary>
/// Al despublicar, mandar a la papelera o borrar un <c>eventPage</c> —o un nodo que lo contiene—, retira
/// su oferta del orquestador: ninguna de sus localidades se vende más por la puerta (ADR 0140 F3).
/// </summary>
/// <remarks>
/// <para><b>Por qué hace falta.</b> La puerta vende contra el precio y el aforo que el orquestador
/// publicó, no contra el catálogo: un evento que la ficha ya no muestra se seguía vendiendo hasta que
/// empezara. Publicar no alcanza para retirarlo, porque un evento despublicado no se publica.</para>
///
/// <para><b>Con sus descendientes</b> al despublicar o mandar a la papelera: los hijos de un nodo
/// despublicado dejan de verse en el sitio aunque no se despubliquen uno a uno, y la papelera se los
/// lleva con el padre. Al borrar ya no hay descendientes que leer: los eventos borrados llegan en la
/// notificación o ya se retiraron al ir a la papelera.</para>
///
/// <para><b>Sólo con el catálogo en el contenido</b>, como publicar, y <b>nunca tumba</b> la acción del
/// editor: lo que falle queda en el log y se repara retirándolo otra vez (despublicar de nuevo).</para>
/// </remarks>
public sealed class OfertaDeEventoAlRetirar :
    INotificationAsyncHandler<ContentUnpublishedNotification>,
    INotificationAsyncHandler<ContentMovedToRecycleBinNotification>,
    INotificationAsyncHandler<ContentDeletedNotification>
{
    private const int Pagina = 500;

    private readonly IEventOfferPublisher _ofertas;
    private readonly IContentService _contenidos;
    private readonly IOptionsMonitor<CatalogSettings> _fuentes;
    private readonly ILogger<OfertaDeEventoAlRetirar> _log;

    public OfertaDeEventoAlRetirar(
        IEventOfferPublisher ofertas,
        IContentService contenidos,
        IOptionsMonitor<CatalogSettings> fuentes,
        ILogger<OfertaDeEventoAlRetirar> log)
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
        if (!OfertaDeEventoAlPublicar.DelContenido(_fuentes)) return;

        var slugs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var nodo in nodos)
        {
            Anotar(nodo, slugs);
            if (conDescendientes) Descendientes(nodo, slugs);
        }

        foreach (var slug in slugs)
        {
            try
            {
                await _ofertas.RetireAsync(slug, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogWarning(ex, "No se pudo retirar la oferta del evento {Slug}. Se repara retirándolo otra vez.", slug);
            }
        }
    }

    private void Descendientes(IContent nodo, HashSet<string> slugs)
    {
        try
        {
            long total;
            var pagina = 0L;
            do
            {
                foreach (var hijo in _contenidos.GetPagedDescendants(nodo.Id, pagina++, Pagina, out total)) Anotar(hijo, slugs);
            }
            while (pagina * Pagina < total);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning(ex, "No se pudieron leer los descendientes de {Nodo} para retirar sus ofertas.", nodo.Id);
        }
    }

    private static void Anotar(IContent nodo, HashSet<string> slugs)
    {
        if (!string.Equals(nodo.ContentType.Alias, OfertaDeEventoAlPublicar.EventPageAlias, StringComparison.Ordinal)) return;
        var slug = nodo.GetValue<string>(OfertaDeEventoAlPublicar.PropiedadDelSlug)?.Trim();
        if (!string.IsNullOrEmpty(slug)) slugs.Add(slug);
    }
}
