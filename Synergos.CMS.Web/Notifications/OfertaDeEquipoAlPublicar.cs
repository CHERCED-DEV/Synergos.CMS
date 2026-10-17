using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services.Catalog;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;

namespace Synergos.CMS.Web.Notifications;

/// <summary>
/// Al publicar un <c>equipmentPage</c>, publica su oferta en el orquestador del alquiler (línea 005, research R2,
/// T029): lo que la renta por la puerta cotiza.
/// </summary>
/// <remarks>
/// <para><b>Sólo con el catálogo en el contenido</b> (<c>Synergos:Catalog:Sources:Alquiler = cms</c>): en
/// <c>demo</c> el catálogo es la siembra y un <c>equipmentPage</c> no se alquila.</para>
///
/// <para><b>Lee el equipo por el catálogo y no del contenido que llega</b>: así la oferta sale de la MISMA
/// proyección que pinta la ficha —con las reglas de <c>EquipmentContentRules</c> y el tope de días del despliegue—,
/// y no de una segunda lectura de las propiedades que se desviaría. Un equipo que el catálogo omite (sin nombre, mal
/// formado) no tiene ficha, y tampoco oferta: la que tuviera publicada se RETIRA.</para>
///
/// <para><b>Nunca tumba el publicar</b>: el contenido ya se guardó cuando esto corre. Lo que falle queda en el log,
/// y se repara republicando.</para>
/// </remarks>
public sealed class OfertaDeEquipoAlPublicar : INotificationAsyncHandler<ContentPublishedNotification>
{
    internal const string EquipmentPageAlias = "equipmentPage";

    internal const string PropiedadDelSlug = "equipmentSlug";

    private readonly IEquipmentCatalogProvider _catalogo;
    private readonly IEquipmentOfferPublisher _ofertas;
    private readonly IOptionsMonitor<CatalogSettings> _fuentes;
    private readonly ILogger<OfertaDeEquipoAlPublicar> _log;

    /// <summary>Construye el manejador.</summary>
    /// <param name="catalogo">El catálogo de equipos, la misma proyección que la ficha.</param>
    /// <param name="ofertas">Quien publica la oferta en el orquestador.</param>
    /// <param name="fuentes">De dónde sale cada catálogo.</param>
    /// <param name="log">Dónde queda lo que no se pudo.</param>
    public OfertaDeEquipoAlPublicar(
        IEquipmentCatalogProvider catalogo,
        IEquipmentOfferPublisher ofertas,
        IOptionsMonitor<CatalogSettings> fuentes,
        ILogger<OfertaDeEquipoAlPublicar> log)
    {
        _catalogo = catalogo;
        _ofertas = ofertas;
        _fuentes = fuentes;
        _log = log;
    }

    /// <inheritdoc />
    public async Task HandleAsync(ContentPublishedNotification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);

        var equipos = notification.PublishedEntities
            .Where(c => string.Equals(c.ContentType.Alias, EquipmentPageAlias, StringComparison.Ordinal))
            .ToList();
        if (equipos.Count == 0 || !DelContenido(_fuentes)) return;

        foreach (var contenido in equipos)
        {
            // El slug es la identidad del equipo: el mismo con que lo aparta la agenda y lo cotiza la tarifa.
            var slug = contenido.GetValue<string>(PropiedadDelSlug)?.Trim();
            if (string.IsNullOrEmpty(slug)) continue;

            try
            {
                var equipo = await _catalogo.GetAsync(slug, cancellationToken).ConfigureAwait(false);
                if (equipo is null)
                {
                    _log.LogWarning(
                        "Se publicó el equipo {Slug} y el catálogo no lo sirve: se retira su oferta. Ver el log del catálogo.",
                        slug);
                    await _ofertas.RetireAsync(slug, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                await _ofertas.PublishAsync(equipo, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogWarning(ex, "No se pudo publicar la oferta del equipo {Slug}. Se repara republicando.", slug);
            }
        }
    }

    /// <summary>Si los equipos salen del contenido: sólo entonces un <c>equipmentPage</c> se alquila.</summary>
    internal static bool DelContenido(IOptionsMonitor<CatalogSettings> fuentes)
        => fuentes.CurrentValue.Sources.TryGetValue(UmbracoEquipmentCatalogSource.Vertical, out var fuente)
           && string.Equals(fuente, "cms", StringComparison.OrdinalIgnoreCase);
}
