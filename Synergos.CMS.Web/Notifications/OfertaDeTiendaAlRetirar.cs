using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Services;

namespace Synergos.CMS.Web.Notifications;

/// <summary>
/// Al despublicar, mandar a la papelera o borrar un <c>productPage</c> —o un nodo que lo contiene—, retira su
/// oferta del orquestador de la tienda: ninguna de sus variantes se vende más (004-tienda-compra, R-9). El
/// molde de <see cref="OfertaDeEventoAlRetirar"/>.
/// </summary>
/// <remarks>
/// <para><b>Por qué hace falta.</b> La compra por el orquestador vende contra el precio y las existencias que
/// él publicó, no contra el catálogo: un producto que la ficha ya no muestra se seguiría vendiendo. Publicar
/// no alcanza para retirarlo, porque un producto despublicado no se publica.</para>
///
/// <para><b>Con sus descendientes</b> al despublicar o mandar a la papelera (una categoría despublicada se
/// lleva sus productos); al borrar ya no hay descendientes que leer: los borrados llegan en la notificación o
/// ya se retiraron al ir a la papelera.</para>
///
/// <para><b>Sólo con el catálogo en el contenido</b>, como publicar, y <b>nunca tumba</b> la acción del
/// editor: lo que falle queda en el log y se repara retirándolo otra vez.</para>
/// </remarks>
public sealed class OfertaDeTiendaAlRetirar :
    INotificationAsyncHandler<ContentUnpublishedNotification>,
    INotificationAsyncHandler<ContentMovedToRecycleBinNotification>,
    INotificationAsyncHandler<ContentDeletedNotification>
{
    private const int Pagina = 500;

    private readonly ITiendaOfferPublisher _ofertas;
    private readonly IContentService _contenidos;
    private readonly IOptionsMonitor<CatalogSettings> _fuentes;
    private readonly ILogger<OfertaDeTiendaAlRetirar> _log;

    public OfertaDeTiendaAlRetirar(
        ITiendaOfferPublisher ofertas,
        IContentService contenidos,
        IOptionsMonitor<CatalogSettings> fuentes,
        ILogger<OfertaDeTiendaAlRetirar> log)
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
        if (!OfertaDeTiendaAlPublicar.DelContenido(_fuentes)) return;

        var skus = new HashSet<string>(StringComparer.Ordinal);
        foreach (var nodo in nodos)
        {
            Anotar(nodo, skus);
            if (conDescendientes) Descendientes(nodo, skus);
        }

        foreach (var sku in skus)
        {
            try
            {
                await _ofertas.RetireAsync(sku, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogWarning(ex, "No se pudo retirar la oferta del producto {Sku}. Se repara retirándolo otra vez.", sku);
            }
        }
    }

    private void Descendientes(IContent nodo, HashSet<string> skus)
    {
        try
        {
            long total;
            var pagina = 0L;
            do
            {
                foreach (var hijo in _contenidos.GetPagedDescendants(nodo.Id, pagina++, Pagina, out total)) Anotar(hijo, skus);
            }
            while (pagina * Pagina < total);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning(ex, "No se pudieron leer los descendientes de {Nodo} para retirar sus ofertas.", nodo.Id);
        }
    }

    private static void Anotar(IContent nodo, HashSet<string> skus)
    {
        if (!string.Equals(nodo.ContentType.Alias, OfertaDeTiendaAlPublicar.ProductPageAlias, StringComparison.Ordinal)) return;
        var sku = nodo.GetValue<string>(OfertaDeTiendaAlPublicar.PropiedadDelSku)?.Trim();
        if (!string.IsNullOrEmpty(sku)) skus.Add(sku);
    }
}
