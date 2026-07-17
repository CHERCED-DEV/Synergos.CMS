namespace Synergos.CMS.Application.Configuration;

/// <summary>
/// Configura el <c>IBundleRegistryClient</c> que resuelve URLs de
/// bundles UI hidratables. Sección <c>Synergos:BundleRegistry</c>.
/// Cap-280 Batch B (Olas 283-285).
/// </summary>
/// <remarks>
/// Cero scripts manuales — el client lee la CDN automáticamente
/// (filesystem o HTTP), detecta cambios via <c>FileSystemWatcher</c>
/// (FileSystem mode) o cache TTL (Http mode), computa SRI lazy si el
/// manifest no lo trae. ADR 0089.
/// </remarks>
public sealed class BundleRegistrySettings
{
    /// <summary>
    /// <c>Stub</c> | <c>FileSystem</c> | <c>Http</c>. Default Stub.
    /// El composer registra el adapter correspondiente.
    /// - <c>Stub</c>: siempre retorna null. Default cuando no hay CDN.
    /// - <c>FileSystem</c>: lee registry.json + manifests del filesystem.
    ///   Usar con CDN local (e.g. C:\LOCAL_CDN).
    /// - <c>Http</c>: GET al registry endpoint remoto (deferred).
    /// </summary>
    public string Mode { get; init; } = "Stub";

    /// <summary>
    /// Path absoluto al directorio raíz de la CDN cuando Mode=FileSystem.
    /// Ej. <c>C:\LOCAL_CDN</c>. El client busca el registry en
    /// <c>{LocalPath}/{BundlesNamespace}/{RegistryFileName}</c>.
    /// </summary>
    public string LocalPath { get; init; } = string.Empty;

    /// <summary>
    /// Subdirectorio bajo <see cref="LocalPath"/> (FileSystem) o segmento
    /// del URL (Http) donde viven los bundles. Default <c>"synergos"</c>.
    /// </summary>
    public string BundlesNamespace { get; init; } = "synergos";

    /// <summary>
    /// Filename del registry global. Default <c>"registry.json"</c>.
    /// </summary>
    public string RegistryFileName { get; init; } = "registry.json";

    /// <summary>
    /// URL base que el cliente usa para construir URLs de bundles servidas
    /// al browser. Cuando se usa con el static-files endpoint
    /// (<see cref="LocalCdnSettings"/>), debe matchear el RoutePath del
    /// endpoint, e.g. <c>"/cdn-bundles"</c>. Para CDN remota, e.g.
    /// <c>"https://cdn.synergos.com"</c>.
    /// </summary>
    public string PublicBaseUrl { get; init; } = "/cdn-bundles";

    /// <summary>
    /// Framework default cuando el caller no especifica explicitamente.
    /// Default <c>"angular"</c> (matchea memoria
    /// <c>feedback_framework_agnostic_integration</c> — Angular first).
    /// </summary>
    public string DefaultFramework { get; init; } = "angular";

    /// <summary>
    /// Slot/version pointer default. Valores válidos: <c>"latest"</c>,
    /// <c>"v0"</c>, <c>"v1"</c>, etc., o un semver exacto. Default
    /// <c>"latest"</c>.
    /// </summary>
    public string DefaultSlot { get; init; } = "latest";

    /// <summary>
    /// Si true, strip del prefix <c>"synergos-"</c> al resolver el folder.
    /// Tu CDN local tiene folders como <c>column/</c> (sin prefix); el
    /// tag DOM es <c>synergos-column</c>. Con true, el client resuelve
    /// el path correctamente. Default <c>true</c>.
    /// </summary>
    public bool StripFolderPrefix { get; init; } = true;

    /// <summary>
    /// Si true, computa el SRI integrity (sha384) del main.js cuando el
    /// manifest no trae <c>integrity</c>. Cacheado in-memory por path
    /// hasta que el FileSystemWatcher detecte cambio del archivo.
    /// Default <c>true</c>. Set a false si la CDN ya provee SRI hashes
    /// y querés evitar el read+hash overhead.
    /// </summary>
    public bool ComputeIntegrityIfMissing { get; init; } = true;

    /// <summary>
    /// Hot-reload via FileSystemWatcher (Mode=FileSystem). Default true.
    /// Set a false en environments con FS slow/networked donde watch
    /// notifications son unreliable; el client sigue funcionando pero
    /// requiere restart al cambiar el registry.
    /// </summary>
    public bool HotReloadEnabled { get; init; } = true;

    /// <summary>
    /// Debounce window tras un cambio detectado por FileSystemWatcher
    /// antes de recargar el registry. Default 500 ms — evita thrashing
    /// si el CDN team escribe múltiples archivos en burst.
    /// </summary>
    public int HotReloadDebounceMilliseconds { get; init; } = 500;

    /// <summary>
    /// Tag canónico que el <c>BundleRegistryProbe</c> intenta resolver
    /// para verdict de salud. Default <c>"synergos-column"</c> (primer
    /// primitive del catalog Synergos). Override útil para CDNs custom
    /// que no exponen ese tag. Cap-290 Batch A — ADR 0090.
    /// </summary>
    public string ProbeTag { get; init; } = "synergos-column";
}
