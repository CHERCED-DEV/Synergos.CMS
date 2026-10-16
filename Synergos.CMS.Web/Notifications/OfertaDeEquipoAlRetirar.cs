using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Services;

namespace Synergos.CMS.Web.Notifications;

/// <summary>
/// Al despublicar, mandar a la papelera o borrar un <c>equipmentPage</c> —o un nodo que lo contiene—, retira su
/// oferta del orquestador: no se alquila más por la puerta (línea 005, research R2, T029).
/// </summary>
/// <remarks>
/// <para><b>Por qué hace falta.</b> La puerta cotiza contra la tarifa que el orquestador publicó, no contra el
/// catálogo: un equipo que la ficha ya no muestra se seguiría alquilando. Publicar no alcanza para retirarlo, porque
/// un equipo despublicado no se publica. Lo ya confirmado no cambia: la oferta retirada sólo cierra lo nuevo.</para>
///
/// <para><b>Con sus descendientes</b> al despublicar o mandar a la papelera, como el de Eventos; al borrar ya no hay
/// descendientes que leer. <b>Sólo con el catálogo en el contenido</b>, y <b>nunca tumba</b> la acción del editor.</para>
/// </remarks>
public sealed class OfertaDeEquipoAlRetirar :
    INotificationAsyncHandler<ContentUnpublishedNotification>,
    INotificationAsyncHandler<ContentMovedToRecycleBinNotification>,
    INotificationAsyncHandler<ContentDeletedNotification>
{
    private const int Pagina = 500;

    private readonly IEquipmentOfferPublisher _ofertas;
    private readonly IContentService _contenidos;
    private readonly IOptionsMonitor<CatalogSettings> _fuentes;
    private readonly ILogger<OfertaDeEquipoAlRetirar> _log;

    /// <summary>Construye el manejador.</summary>
    /// <param name="ofertas">Quien retira la oferta en el orquestador.</param>
    /// <param name="contenidos">Para leer los descendientes de lo despublicado.</param>
    /// <param name="fuentes">De dónde sale cada catálogo.</param>
    /// <param name="log">Dónde queda lo que no se pudo.</param>
    public OfertaDeEquipoAlRetirar(
        IEquipmentOfferPublisher ofertas,
        IContentService contenidos,
        IOptionsMonitor<CatalogSettings> fuentes,
        ILogger<OfertaDeEquipoAlRetirar> log)
    {
        _ofertas = ofertas;
        _contenidos = contenidos;
        _fuentes = fuentes;
        _log = log;
    }

    /// <inheritdoc />
    public Task HandleAsync(ContentUnpublishedNotification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        return RetirarAsync(notification.UnpublishedEntities, conDescendientes: true, cancellationToken);
    }

    /// <inheritdoc />
    public Task HandleAsync(ContentMovedToRecycleBinNotification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        return RetirarAsync(notification.MoveInfoCollection.Select(m => m.Entity), conDescendientes: true, cancellationToken);
    }

    /// <inheritdoc />
    public Task HandleAsync(ContentDeletedNotification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        return RetirarAsync(notification.DeletedEntities, conDescendientes: false, cancellationToken);
    }

    private async Task RetirarAsync(IEnumerable<IContent> nodos, bool conDescendientes, CancellationToken ct)
    {
        if (!OfertaDeEquipoAlPublicar.DelContenido(_fuentes)) return;

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
                _log.LogWarning(ex, "No se pudo retirar la oferta del equipo {Slug}. Se repara retirándolo otra vez.", slug);
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
        if (!string.Equals(nodo.ContentType.Alias, OfertaDeEquipoAlPublicar.EquipmentPageAlias, StringComparison.Ordinal)) return;
        var slug = nodo.GetValue<string>(OfertaDeEquipoAlPublicar.PropiedadDelSlug)?.Trim();
        if (!string.IsNullOrEmpty(slug)) slugs.Add(slug);
    }
}
