namespace Synergos.CMS.Application.Configuration;

/// <summary>
/// POCO tipado bindeado de <c>Synergos:TravelOrders</c>. Gobierna la persistencia
/// durable del estado de órdenes de viaje del dominio Booking (fan-out de T1, doc 25).
/// Espeja <see cref="ShopOrdersSettings"/>.
/// </summary>
public sealed class TravelOrdersSettings
{
    /// <summary>
    /// Subdirectorio bajo <c>ContentRootPath</c> donde se persiste el JSON de cada
    /// orden de viaje (uno por <c>orderRef</c>). Default <c>"App_Data/syn-travel-orders/"</c>.
    /// </summary>
    public string StorageRoot { get; init; } = "App_Data/syn-travel-orders/";
}
