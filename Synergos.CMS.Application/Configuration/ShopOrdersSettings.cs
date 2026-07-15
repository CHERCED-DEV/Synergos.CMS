namespace Synergos.CMS.Application.Configuration;

/// <summary>
/// POCO tipado bindeado de la sección <c>Synergos:ShopOrders</c> de
/// <c>appsettings.*.json</c>. Gobierna la persistencia durable de órdenes de
/// Tienda (T1, doc 25). Espeja <see cref="CommentsSettings"/>.
/// </summary>
public sealed class ShopOrdersSettings
{
    /// <summary>
    /// Subdirectorio bajo <c>ContentRootPath</c> donde se persiste el JSON de cada
    /// orden (uno por <c>orderRef</c>). Default <c>"App_Data/syn-orders/"</c> — fuera
    /// de wwwroot, no servido por static-files. Es PII de compra, no PHI → sin cifrar.
    /// </summary>
    public string StorageRoot { get; init; } = "App_Data/syn-orders/";
}
