namespace Synergos.CMS.Application.Configuration;

/// <summary>
/// Tuning del host bridge inyectado en <c>window.synergos</c>.
/// Sección <c>Synergos:HostBridge</c>. Ola 216, ADR 0083.
/// </summary>
public sealed class HostBridgeSettings
{
    /// <summary>
    /// Prefixes de Dictionary keys a publicar en el bridge i18n.
    /// El subset evita serializar las 369+ keys del CMS — solo las
    /// consumed por components UI client-side. Default cubre los
    /// dominios que tienen UI hidratable.
    /// </summary>
    public string[] I18nKeyPrefixes { get; init; } = new[]
    {
        "Form.",
        "Search.",
        "Common.",
        "Comments.",
        "Cart.",
        "Shop.",
        "Pagination.",
        "Modal.",
        "Share.",
        "Nav.",
        "Account.",
    };

    /// <summary>
    /// Si true, incluye Member context (key + email + roles) en el
    /// bridge. Útil para UI que customiza per-member. False por
    /// privacy/compliance default.
    /// </summary>
    public bool IncludeMemberContext { get; init; } = true;

    /// <summary>
    /// Si true, incluye Page metadata (id + docType + cultures) en
    /// el bridge. Útil para UI que routeа page-specific. Cero
    /// privacy concern — public info.
    /// </summary>
    public bool IncludePageMetadata { get; init; } = true;

    /// <summary>
    /// Bridge contract version emitida en window.synergos.version.
    /// UI verifica compat. Bump major si cambian contracts.
    /// </summary>
    public string ContractVersion { get; init; } = "1.0.0";

    /// <summary>
    /// Si true (default false), el partial <c>_SynergosBridge.cshtml</c>
    /// emite <c>&lt;script src="/synergos-bridge.js"&gt;</c> en lugar
    /// del bloque inline <c>&lt;script&gt;window.synergos = {...}&lt;/script&gt;</c>.
    /// El endpoint <c>SynergosBridgeController</c> sirve el mismo
    /// payload con <c>Cache-Control: private, no-store</c> (per-member,
    /// per-page). Cap-260 Batch A (Olas 251-253). Activar cuando el
    /// site tiene CSP estricto sin <c>'unsafe-inline'</c> en
    /// <c>script-src</c>.
    /// </summary>
    public bool CspStrictMode { get; init; } = false;
}
