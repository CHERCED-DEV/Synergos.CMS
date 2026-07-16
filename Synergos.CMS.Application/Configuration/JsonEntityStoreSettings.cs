namespace Synergos.CMS.Application.Configuration;

/// <summary>
/// POCO tipado bindeado de <c>Synergos:JsonEntityStore</c>. Gobierna la persistencia
/// durable genérica de entidades JSON (órdenes de Tienda, sesiones de pago, reservas,
/// órdenes de viaje). Reemplaza a los 4 POCOs dedicados que tenía cada store —
/// un solo knob para el operador.
/// </summary>
public sealed class JsonEntityStoreSettings
{
    /// <summary>
    /// Raíz bajo <c>ContentRootPath</c> donde vive cada familia de entidades, en su
    /// subdirectorio <c>syn-{resourceType}/</c> (ej. <c>App_Data/syn-orders/</c>,
    /// <c>App_Data/syn-payments/</c>). Default <c>"App_Data/"</c> — fuera de wwwroot,
    /// no servido por static-files. PII de compra (no PHI) → sin cifrar.
    /// </summary>
    public string StorageRoot { get; init; } = "App_Data/";
}
