namespace Synergos.CMS.Application.Configuration;

/// <summary>
/// Contra qué se reserva un hotel (HU #36) — sección <c>Synergos:Viajes</c>.
/// </summary>
/// <remarks>
/// <para><b>El default es el motor en proceso, y no es una transición.</b> Un clon limpio arranca
/// y reserva sin levantar cuatro servicios. Poner <see cref="Mode"/> en <c>Bff</c> sin el
/// orquestador arriba degrada —no se puede apartar ni cobrar, y lo dice— pero no tumba el
/// vertical: buscar, ver la ficha y consultar una reserva ya hecha siguen sirviendo.</para>
///
/// <para><b>Solo la vía hotel.</b> El carrito multi-producto (<c>TravelCartService</c>) NO se
/// cablea todavía, y no por falta de ganas: <c>TravelCartItem</c> no lleva fechas —ni el seam, ni
/// el DTO HTTP, ni el motor en proceso— y un apartado de <c>Api.Booking</c> <i>es</i> una ventana
/// sobre un recurso. Hasta que el contrato tenga periodo, ese carrito no se puede llevar allá sin
/// inventarse las fechas, que es exactamente el error que costó una vuelta en la HU #25.</para>
/// </remarks>
public sealed class ViajesSettings
{
    /// <summary>
    /// <c>Stub</c> (default, el motor en proceso) o <c>Bff</c> (contra <c>Synergos.Bff.Viajes</c>).
    /// </summary>
    public string Mode { get; init; } = "Stub";

    /// <summary>Dónde vive el orquestador.</summary>
    public string BaseUrl { get; init; } = "http://127.0.0.1:5304/";

    /// <summary>La llave compartida servicio↔servicio. Sin ella todo responde 401.</summary>
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>
    /// El <c>Kind</c> con el que este vertical nombra a quien viaja.
    /// </summary>
    /// <remarks>
    /// Viaja opaco: el orquestador lo guarda y lo devuelve, y ninguna capacidad ramifica sobre él
    /// (<c>CLAUDE.md</c> §13). Es configurable porque el nombre del sujeto es del negocio que
    /// despliega, no de la plataforma.
    /// </remarks>
    public string TravellerKind { get; init; } = "viajes.viajero";

    /// <summary>
    /// Segundos de espera. Generoso a propósito: reservar cruza cuatro servicios y NO es auxiliar.
    /// </summary>
    /// <remarks>
    /// Cortarlo pronto es peor que esperar. Un timeout no dice «no se cobró» — dice «no sé»; y el
    /// peor resultado posible es un cobro sin reserva.
    /// </remarks>
    public int TimeoutSeconds { get; init; } = 30;
}
