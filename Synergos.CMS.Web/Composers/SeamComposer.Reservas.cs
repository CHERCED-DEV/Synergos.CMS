using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Notifications;
using Synergos.CMS.Web.Services;
using Synergos.CMS.Web.Services.Reservas;
using Umbraco.Cms.Core.Notifications;

namespace Synergos.CMS.Web.Composers;

public sealed partial class SeamComposer
{
    /// <summary>
    /// La reserva de un servicio por franja (011-booking-reserva): el CMS sólo abre la puerta hacia
    /// <c>Bff.Reservas</c>; acá va lo del sitio —si reserva en línea— que lee el resolver del elemento, y la
    /// oferta que se publica al publicar una ficha reservable.
    /// </summary>
    private static void ComposeReservas(IUmbracoBuilder builder)
    {
        builder.Services.AddSeccionDeNegocio<ReservaFeatureSettings, NegocioDeReserva>(
            builder.Config.GetSection("Synergos:Features:Reserva"));

        // La OFERTA: hay destino sólo si el despliegue dice dónde vive el orquestador (Synergos:Reservas:BaseUrl,
        // la misma clave con la que la puerta lo encuentra). Sin él, publicar una ficha no sale a la red.
        // Diez segundos: quien espera es el editor que publica, y una oferta que no salió se repara republicando.
        var seccion = builder.Config.GetSection("Synergos:Reservas");
        var hayOrquestador = !string.IsNullOrWhiteSpace(seccion["BaseUrl"]);
        if (hayOrquestador)
        {
            builder.Services.AddClienteDelArbolDeServicios(
                PublicadorDeOfertasDeReservas.ClientName,
                DestinoDelArbol.De(seccion, seccion["BaseUrl"]!, 10));
        }
        builder.Services.AddSingleton(sp => new PublicadorDeOfertasDeReservas(
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<ILogger<PublicadorDeOfertasDeReservas>>(),
            hayOrquestador));

        // Se dispara al publicar y al retirar, NUNCA al arrancar (ADR 0013).
        builder.AddNotificationAsyncHandler<ContentPublishedNotification, OfertaDeReservaAlPublicar>();
        builder.AddNotificationAsyncHandler<ContentUnpublishedNotification, OfertaDeReservaAlRetirar>();
        builder.AddNotificationAsyncHandler<ContentMovedToRecycleBinNotification, OfertaDeReservaAlRetirar>();
        builder.AddNotificationAsyncHandler<ContentDeletedNotification, OfertaDeReservaAlRetirar>();
    }
}
