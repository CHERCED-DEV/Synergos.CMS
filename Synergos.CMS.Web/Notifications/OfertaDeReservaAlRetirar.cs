using Synergos.CMS.Web.Services.Reservas;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Services;

namespace Synergos.CMS.Web.Notifications;

/// <summary>
/// Al despublicar, mandar a la papelera o borrar una ficha reservable —o un nodo que la contiene—, retira su
/// oferta de <c>Bff.Reservas</c>: el servicio deja de reservarse por la puerta (011-booking-reserva).
/// </summary>
/// <remarks>
/// <para>La puerta reserva contra la oferta del orquestador, no contra la ficha: una ficha despublicada se
/// seguiría reservando. Con sus descendientes al despublicar o mandar a la papelera, como la oferta de Eventos;
/// al borrar ya no hay descendientes que leer. Retirar la de una ficha que nunca se ofreció no hace nada.</para>
///
/// <para><b>Nunca tumba</b> la acción del editor: lo que falle queda en el log y se repara retirándola otra vez.</para>
/// </remarks>
public sealed class OfertaDeReservaAlRetirar :
    INotificationAsyncHandler<ContentUnpublishedNotification>,
    INotificationAsyncHandler<ContentMovedToRecycleBinNotification>,
    INotificationAsyncHandler<ContentDeletedNotification>
{
    private const int Pagina = 500;

    private readonly PublicadorDeOfertasDeReservas _ofertas;
    private readonly IContentService _contenidos;
    private readonly ILogger<OfertaDeReservaAlRetirar> _log;

    public OfertaDeReservaAlRetirar(
        PublicadorDeOfertasDeReservas ofertas, IContentService contenidos, ILogger<OfertaDeReservaAlRetirar> log)
    {
        _ofertas = ofertas;
        _contenidos = contenidos;
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
        var servicios = new HashSet<string>(StringComparer.Ordinal);
        foreach (var nodo in nodos)
        {
            Anotar(nodo, servicios);
            if (conDescendientes) Descendientes(nodo, servicios);
        }

        foreach (var servicio in servicios)
        {
            try
            {
                await _ofertas.RetirarAsync(servicio, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogWarning(ex, "No se pudo retirar la oferta del servicio {Servicio}. Se repara retirándola otra vez.", servicio);
            }
        }
    }

    private void Descendientes(IContent nodo, HashSet<string> servicios)
    {
        try
        {
            long total;
            var pagina = 0L;
            do
            {
                foreach (var hijo in _contenidos.GetPagedDescendants(nodo.Id, pagina++, Pagina, out total)) Anotar(hijo, servicios);
            }
            while (pagina * Pagina < total);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning(ex, "No se pudieron leer los descendientes de {Nodo} para retirar sus ofertas de reserva.", nodo.Id);
        }
    }

    private static void Anotar(IContent nodo, HashSet<string> servicios)
    {
        if (FichaReservable.LaLleva(nodo)) servicios.Add(nodo.Key.ToString("D"));
    }
}
