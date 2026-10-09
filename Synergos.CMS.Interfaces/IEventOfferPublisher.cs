namespace Synergos.CMS.Interfaces;

/// <summary>Cómo terminó publicar la oferta de un evento.</summary>
public enum EventOfferOutcome
{
    /// <summary>El orquestador la aplicó: publicada, el evento se vende por él; retirada, ya no.</summary>
    Published,

    /// <summary>No hay orquestador configurado: no se intentó, y no es un fallo.</summary>
    NoDestination,

    /// <summary>Se intentó y no se pudo. Quedó en el log; se repara republicando.</summary>
    Failed,
}

/// <summary>
/// Publica la oferta de un evento —precio por localidad, vigencia de venta, tope por compra y aforo— en
/// el orquestador de Eventos, que la escribe en las capacidades (ADR 0140 F3).
/// </summary>
/// <remarks>
/// <para><b>Por qué existe.</b> El orquestador vende contra <c>Api.Pricing</c> y <c>Api.Inventory</c>,
/// y el catálogo vive en el contenido. Sin publicar la oferta, todo evento del CMS sale por el
/// orquestador con «no hay precio» o «no hay aforo declarado».</para>
///
/// <para><b>Nunca lanza por el orquestador.</b> Lo llaman el publicar del editor y la creación de un
/// evento del organizador, y ninguno de los dos puede morir porque otro proceso esté caído: el
/// contenido ya se guardó. Devuelve cómo terminó, y quien llama decide qué contar; la reparación es
/// republicar.</para>
///
/// <para><b>Nunca al arrancar</b> (ADR 0013): se dispara al publicar, al crear o a mano.</para>
/// </remarks>
public interface IEventOfferPublisher
{
    /// <summary>Publica la oferta del evento tal como lo sirve el catálogo.</summary>
    /// <remarks>
    /// La oferta es el estado ENTERO del evento: una localidad que ya no viene deja de venderse, y un
    /// evento sin localidades se retira como con <see cref="RetireAsync"/>.
    /// </remarks>
    /// <param name="evento">El evento, con sus localidades y su mapa de butacas.</param>
    /// <param name="cancellationToken">Cancelación de quien llama.</param>
    Task<EventOfferOutcome> PublishAsync(EventDetail evento, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retira la oferta de un evento: ninguna de sus localidades se vende más por el orquestador. Es lo que
    /// pasa al despublicarlo o borrarlo, y cuando el catálogo deja de servirlo.
    /// </summary>
    /// <param name="eventId">El identificador del evento (su slug).</param>
    /// <param name="cancellationToken">Cancelación de quien llama.</param>
    Task<EventOfferOutcome> RetireAsync(string eventId, CancellationToken cancellationToken = default);
}
