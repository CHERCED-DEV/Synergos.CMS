namespace Synergos.CMS.Application.Configuration;

/// <summary>
/// POCO tipado bindeado de <c>Synergos:PaymentSessions</c>. Gobierna la persistencia
/// durable del estado de sesiones de pago (T3, doc 25). Espeja <see cref="ShopOrdersSettings"/>.
/// </summary>
public sealed class PaymentSessionsSettings
{
    /// <summary>
    /// Subdirectorio bajo <c>ContentRootPath</c> donde se persiste el JSON de cada
    /// sesión de pago (uno por <c>sessionId</c>). Default <c>"App_Data/syn-payments/"</c>
    /// — fuera de wwwroot, PII de compra (no PHI) → sin cifrar.
    /// </summary>
    public string StorageRoot { get; init; } = "App_Data/syn-payments/";
}
