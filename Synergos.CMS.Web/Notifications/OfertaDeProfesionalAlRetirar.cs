using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Web.Services.Catalog;
using Synergos.CMS.Web.Services.Salud;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Services;

namespace Synergos.CMS.Web.Notifications;

/// <summary>
/// Al despublicar, mandar a la papelera o borrar un <c>professionalPage</c> —o un nodo que lo contiene—, retira su oferta
/// del orquestador de Salud (003-salud-cita, T047). El molde de <see cref="OfertaDeCursoAlRetirar"/>.
/// </summary>
/// <remarks>
/// <para><b>Qué hace retirar, y qué no</b>: la oferta común vence el precio y DEJA el recurso de la agenda (T097 del
/// núcleo, como la de Reservas): las citas ya agendadas siguen en su agenda. El directorio deja de listar al profesional
/// porque la ficha ya no está publicada.</para>
///
/// <para><b>Con sus descendientes</b> al despublicar o mandar a la papelera; al borrar ya no hay descendientes que leer.
/// <b>Sólo con el directorio en el contenido</b>, como publicar, y <b>nunca tumba</b> la acción del editor.</para>
/// </remarks>
public sealed class OfertaDeProfesionalAlRetirar :
    INotificationAsyncHandler<ContentUnpublishedNotification>,
    INotificationAsyncHandler<ContentMovedToRecycleBinNotification>,
    INotificationAsyncHandler<ContentDeletedNotification>
{
    private const int Pagina = 500;

    private readonly IPublicadorDeOfertasDeProfesionales _ofertas;
    private readonly IContentService _contenidos;
    private readonly IOptionsMonitor<CatalogSettings> _fuentes;
    private readonly ILogger<OfertaDeProfesionalAlRetirar> _log;

    public OfertaDeProfesionalAlRetirar(
        IPublicadorDeOfertasDeProfesionales ofertas,
        IContentService contenidos,
        IOptionsMonitor<CatalogSettings> fuentes,
        ILogger<OfertaDeProfesionalAlRetirar> log)
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
        if (!OfertaDeProfesionalAlPublicar.DelContenido(_fuentes)) return;

        var profesionales = new HashSet<string>(StringComparer.Ordinal);
        foreach (var nodo in nodos)
        {
            Anotar(nodo, profesionales);
            if (conDescendientes) Descendientes(nodo, profesionales);
        }

        foreach (var profesional in profesionales)
        {
            try
            {
                await _ofertas.RetirarAsync(profesional, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogWarning(ex, "No se pudo retirar la oferta del profesional {Profesional}. Se repara retirándolo otra vez.", profesional);
            }
        }
    }

    private void Descendientes(IContent nodo, HashSet<string> profesionales)
    {
        try
        {
            long total;
            var pagina = 0L;
            do
            {
                foreach (var hijo in _contenidos.GetPagedDescendants(nodo.Id, pagina++, Pagina, out total)) Anotar(hijo, profesionales);
            }
            while (pagina * Pagina < total);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning(ex, "No se pudieron leer los descendientes de {Nodo} para retirar sus ofertas de profesionales.", nodo.Id);
        }
    }

    private static void Anotar(IContent nodo, HashSet<string> profesionales)
    {
        if (!string.Equals(nodo.ContentType.Alias, UmbracoProfessionalDirectorySource.ProfessionalPageAlias, StringComparison.Ordinal)) return;
        var profesional = nodo.GetValue<string>(OfertaDeProfesionalAlPublicar.PropiedadDelProfesional)?.Trim();
        if (!string.IsNullOrEmpty(profesional)) profesionales.Add(profesional);
    }
}
