namespace Synergos.CMS.Interfaces;

/// <summary>Cómo terminó publicar (o retirar) la oferta de un equipo.</summary>
public enum EquipmentOfferOutcome
{
    /// <summary>El orquestador la aplicó: publicada, el equipo se alquila por la puerta; retirada, ya no.</summary>
    Published,

    /// <summary>No hay orquestador configurado: no se intentó, y no es un fallo.</summary>
    NoDestination,

    /// <summary>Se intentó y no se pudo. Quedó en el log; se repara republicando.</summary>
    Failed,
}

/// <summary>
/// Publica la oferta de un equipo —tarifa por día, tramos, garantía por unidad, días mínimo y máximo, unidades— en
/// <c>Synergos.Bff.Alquiler</c>, que la escribe en las capacidades (línea 005, research R2, T029).
/// </summary>
/// <remarks>
/// <para><b>Por qué existe.</b> La renta por la puerta cotiza con la tarifa publicada en <c>Api.Pricing</c> y aparta
/// en la agenda de <c>Api.Booking</c>, y el catálogo vive en el contenido. Sin publicar la oferta, todo equipo del
/// CMS sale por la puerta con <c>alquiler.equipment_not_offered</c>: el navegador no manda el precio, y el
/// orquestador no tiene de dónde sacarlo.</para>
///
/// <para><b>Nunca lanza por el orquestador.</b> Lo llama el publicar del editor, que no puede morir porque otro
/// proceso esté caído: el contenido ya se guardó. Devuelve cómo terminó; la reparación es republicar.</para>
///
/// <para><b>Nunca al arrancar</b> (ADR 0013): se dispara al publicar, al despublicar o a mano.</para>
/// </remarks>
public interface IEquipmentOfferPublisher
{
    /// <summary>Publica la oferta del equipo tal como lo sirve el catálogo.</summary>
    /// <param name="equipo">El equipo, con sus tarifas y su garantía.</param>
    /// <param name="cancellationToken">Cancelación de quien llama.</param>
    /// <returns>Cómo terminó.</returns>
    Task<EquipmentOfferOutcome> PublishAsync(RentalEquipment equipo, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retira la oferta de un equipo: no se alquila más por la puerta. Es lo que pasa al despublicarlo o borrarlo, y
    /// cuando el catálogo deja de servirlo. Lo ya confirmado no cambia.
    /// </summary>
    /// <param name="equipmentId">El slug del equipo.</param>
    /// <param name="cancellationToken">Cancelación de quien llama.</param>
    /// <returns>Cómo terminó.</returns>
    Task<EquipmentOfferOutcome> RetireAsync(string equipmentId, CancellationToken cancellationToken = default);
}
