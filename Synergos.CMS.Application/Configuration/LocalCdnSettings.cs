namespace Synergos.CMS.Application.Configuration;

/// <summary>
/// Configura un endpoint static-files local que sirve bundles del
/// CDN desde un directorio físico (e.g. <c>C:\LOCAL_CDN</c>) bajo un
/// URL path tipo <c>/cdn-bundles/</c>. Sección
/// <c>Synergos:LocalCdn</c>. Cap-280 Batch A (Olas 281-282).
/// </summary>
/// <remarks>
/// Útil para deployments donde el operador todavía no tiene la CDN
/// remota publicada (ADR 0012 documenta los 5 puntos del CDN team
/// como bloqueo externo). Permite testing end-to-end del bundle
/// integration con bundles locales antes de migrar a la CDN real.
///
/// Cuando llegue la CDN remota: dejar <see cref="Enabled"/>=false
/// y configurar el <c>HttpBundleRegistryClient</c> contra el dominio
/// CDN real (Cap-280 Batch B). El fallback hacia local CDN queda
/// como herramienta dev-loop opt-in.
/// </remarks>
public sealed class LocalCdnSettings
{
    /// <summary>
    /// Si false (default), el endpoint NO se monta. Activar SOLO
    /// cuando el operador tenga bundles en <see cref="LocalPath"/>
    /// y quiera servirlos como CDN local.
    /// </summary>
    public bool Enabled { get; init; } = false;

    /// <summary>
    /// Path absoluto al directorio donde viven los bundles. Ej.
    /// <c>C:\LOCAL_CDN</c> o <c>/var/syn/cdn</c>. Vacío = no-op
    /// (mismo efecto que Enabled=false).
    /// </summary>
    public string LocalPath { get; init; } = string.Empty;

    /// <summary>
    /// URL path bajo el cual se sirve el contenido. Default
    /// <c>/cdn-bundles</c>. Sin trailing slash. Cambiar si conflicta
    /// con alguna ruta de Umbraco o si se desea un alias específico.
    /// </summary>
    public string RoutePath { get; init; } = "/cdn-bundles";

    /// <summary>
    /// <c>Cache-Control: public, max-age={N}, immutable</c> aplicado
    /// a cada response. Default 31536000 (1 año) — assumes que los
    /// bundles son fingerprinted (e.g. <c>element-bundle.{hash}.js</c>)
    /// y nunca se reusa el mismo URL para contenido distinto.
    /// </summary>
    public int CacheControlMaxAgeSeconds { get; init; } = 31_536_000;
}
