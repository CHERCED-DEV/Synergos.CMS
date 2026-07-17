namespace Synergos.CMS.Application.Configuration;

/// <summary>
/// Typed POCO bound from <c>appsettings.*.json</c> section
/// <c>Synergos:Cart</c>. Configura el comportamiento del cart state
/// (cookie name, HMAC secret, límite de items, moneda default).
/// </summary>
/// <remarks>
/// El cart vive en una cookie firmada (HMAC) — sin DB, sin login
/// requerido, persiste entre sesiones del visitante. Para sitios con
/// member auth requerida, una futura impl puede mover el cart al
/// member tree.
///
/// <strong>SECURITY</strong>: <see cref="SecretKey"/> debe sobreescribirse
/// en producción con un valor random de 32+ caracteres. El default
/// embebido es solo para desarrollo y emite warning al boot si no se
/// cambia.
/// </remarks>
public sealed class CartSettings
{
    /// <summary>Nombre de la cookie del cart. Default <c>"syn_cart"</c>.</summary>
    public string CookieName { get; init; } = "syn_cart";

    /// <summary>
    /// Secreto HMAC para firmar el contenido de la cookie y prevenir
    /// tampering. Default <c>"synergos-dev-secret-change-me"</c> —
    /// SOBREESCRIBIR EN PRODUCCIÓN.
    /// </summary>
    public string SecretKey { get; init; } = "synergos-dev-secret-change-me";

    /// <summary>Límite duro de items distintos en el cart. Default 50.</summary>
    public int MaxItems { get; init; } = 50;

    /// <summary>Moneda por defecto del cart (ISO 4217). Default <c>"COP"</c>.</summary>
    public string Currency { get; init; } = "COP";

    /// <summary>Días de vida de la cookie. Default 30.</summary>
    public int CookieLifetimeDays { get; init; } = 30;
}
