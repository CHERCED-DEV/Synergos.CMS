namespace Synergos.CMS.Application.Configuration;

/// <summary>
/// POCO tipado bindeado de <c>Synergos:Reservations</c>. Gobierna la persistencia
/// durable del estado de reservas de stock (T3, doc 25). Espeja <see cref="ShopOrdersSettings"/>.
/// El motor de reservas es transversal (Tienda + Booking + Eventos) — durabilizarlo
/// cierra el restart-gap para todos.
/// </summary>
public sealed class ReservationsSettings
{
    /// <summary>
    /// Subdirectorio bajo <c>ContentRootPath</c> donde se persiste el JSON de cada
    /// reserva (uno por <c>reservationId</c>). Default <c>"App_Data/syn-reservations/"</c>.
    /// </summary>
    public string StorageRoot { get; init; } = "App_Data/syn-reservations/";
}
