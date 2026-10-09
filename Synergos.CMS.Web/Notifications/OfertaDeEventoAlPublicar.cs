using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services.Catalog;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;

namespace Synergos.CMS.Web.Notifications;

/// <summary>
/// Al publicar un <c>eventPage</c>, publica su oferta en el orquestador (ADR 0140 F3).
/// </summary>
/// <remarks>
/// <para><b>Sólo con el catálogo en el contenido</b> (<c>Synergos:Catalog:Sources:Events = cms</c>): en
/// <c>demo</c> la agenda es la siembra y un <c>eventPage</c> no se vende. La siembra se publica a mano,
/// con <c>POST /api/eventos/oferta/republicar</c>.</para>
///
/// <para><b>Lee el evento por el catálogo y no del contenido que llega</b>: así la oferta sale de la
/// MISMA proyección que pinta la ficha —con las fechas ya en la zona del sitio y las reglas de
/// <c>EventContentRules</c>—, y no de una segunda lectura de las propiedades que se desviaría. Un
/// evento que el catálogo omite (sin título, mal formado) no tiene ficha, y tampoco oferta: la que
/// tuviera publicada se RETIRA, para que no se siga vendiendo por la puerta lo que la ficha ya no
/// muestra.</para>
///
/// <para><b>Nunca tumba el publicar</b>: el contenido ya se guardó cuando esto corre. Lo que falle
/// queda en el log, y se repara republicando.</para>
/// </remarks>
public sealed class OfertaDeEventoAlPublicar : INotificationAsyncHandler<ContentPublishedNotification>
{
    internal const string EventPageAlias = "eventPage";

    internal const string PropiedadDelSlug = "eventSlug";

    private readonly IEventCatalogProvider _catalogo;
    private readonly IEventOfferPublisher _ofertas;
    private readonly IOptionsMonitor<CatalogSettings> _fuentes;
    private readonly ILogger<OfertaDeEventoAlPublicar> _log;

    public OfertaDeEventoAlPublicar(
        IEventCatalogProvider catalogo,
        IEventOfferPublisher ofertas,
        IOptionsMonitor<CatalogSettings> fuentes,
        ILogger<OfertaDeEventoAlPublicar> log)
    {
        _catalogo = catalogo;
        _ofertas = ofertas;
        _fuentes = fuentes;
        _log = log;
    }

    public async Task HandleAsync(ContentPublishedNotification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);

        var eventos = notification.PublishedEntities
            .Where(c => string.Equals(c.ContentType.Alias, EventPageAlias, StringComparison.Ordinal))
            .ToList();
        if (eventos.Count == 0 || !DelContenido(_fuentes)) return;

        foreach (var contenido in eventos)
        {
            // El slug no varía por cultura (Variations=Nothing): es la identidad del evento.
            var slug = contenido.GetValue<string>(PropiedadDelSlug)?.Trim();
            if (string.IsNullOrEmpty(slug)) continue;

            try
            {
                var evento = await _catalogo.GetEventAsync(slug, cancellationToken).ConfigureAwait(false);
                if (evento is null)
                {
                    _log.LogWarning(
                        "Se publicó el evento {Slug} y el catálogo no lo sirve: se retira su oferta. Ver el log del catálogo.",
                        slug);
                    await _ofertas.RetireAsync(slug, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                await _ofertas.PublishAsync(evento, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogWarning(ex, "No se pudo publicar la oferta del evento {Slug}. Se repara republicando.", slug);
            }
        }
    }

    /// <summary>Si los eventos salen del contenido: sólo entonces un <c>eventPage</c> se vende.</summary>
    internal static bool DelContenido(IOptionsMonitor<CatalogSettings> fuentes)
        => fuentes.CurrentValue.Sources.TryGetValue(UmbracoEventCatalogSource.Vertical, out var fuente)
           && string.Equals(fuente, "cms", StringComparison.OrdinalIgnoreCase);
}
