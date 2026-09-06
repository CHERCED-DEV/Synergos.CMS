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
    /// Si true (default true), incluye Member context (key + email + roles) en el bridge.
    /// Útil para UI que customiza per-member.
    /// </summary>
    /// <remarks>
    /// <para><b>Decía «False por privacy/compliance default» y el default es y era `true`</b>
    /// (defecto #95). No hay override en ningún `appsettings*.json` ni en el compose, así que todo
    /// despliegue emite hoy el bloque `member` —clave, correo y roles— en el HTML de cada página
    /// para cada visitante autenticado.</para>
    ///
    /// <para><b>Que el correo viaje ahí está DECIDIDO y documentado</b> —`docs/contracts/host-bridge.md`
    /// §Security lo enumera de frente—, así que el defecto no era que saliera: era que el único
    /// texto del repo que hablaba de la postura de privacidad de este bloque <b>afirmaba la
    /// contraria a la que el código aplica</b>. Quien lo leyera para decidir si hacía falta
    /// configurar algo concluía que ya estaba apagado.</para>
    ///
    /// <para><b>Se corrigió la PROSA, no el valor</b>, y a propósito: cambiarlo alteraría en
    /// silencio lo que emite cada despliegue, y la UI no tiene definido qué hacer sin bloque
    /// `member` —el contrato v1 asume que está, su tabla de degradación habla de un `member` que
    /// se queda stale, o sea de uno que existe—. Apagarlo es una decisión del arquitecto y está
    /// planteada en #95.</para>
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
