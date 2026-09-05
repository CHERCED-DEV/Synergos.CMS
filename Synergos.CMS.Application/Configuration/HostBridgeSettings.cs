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
    /// bridge. Útil para UI que customiza per-member. Default true.
    /// </summary>
    /// <remarks>
    /// <para><b>Decía lo contrario de lo que hace</b> (#95): afirmaba que el
    /// valor por defecto era el apagado «por privacy/compliance», y el default
    /// es <c>true</c> desde siempre. Nadie lo apagó nunca —no hay override en
    /// ningún <c>appsettings*</c> ni en el compose—, así que todo despliegue
    /// emite hoy el bloque <c>member</c> para cada visitante autenticado.</para>
    ///
    /// <para><b>Lo que estaba mal era el texto, no el valor.</b> Que el correo
    /// viaje ahí está decidido y documentado: <c>docs/contracts/host-bridge.md</c>
    /// §Security lo enumera de frente («display name + email + roles»). Pero
    /// éste era el único sitio del repo que describía la postura de privacidad
    /// del bloque, y describía la contraria — quien lo leyera para decidir si
    /// hacía falta configurar algo concluía que ya estaba apagado.</para>
    ///
    /// <para><b>Y ponerlo en false no lo nota ningún test.</b> Deja
    /// <c>window.synergos.member</c> en <c>null</c> en todo el sitio: el harness
    /// de contratos monta su propio mock del bridge y el gate de #88 cruza
    /// nombres de campo, no si el bloque llega a emitirse. La UI leería
    /// <c>member</c> como <c>undefined</c> y trataría a un miembro autenticado
    /// como anónimo, con el sitio devolviendo 200. Es una consecuencia que hay
    /// que decidir, no heredar.</para>
    /// </remarks>
    public bool IncludeMemberContext { get; init; } = true;

    /// <summary>
    /// Si true, incluye Page metadata (id + docType + cultures) en
    /// el bridge. Útil para UI que routeа page-specific. Cero
    /// privacy concern — public info. Default true.
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
