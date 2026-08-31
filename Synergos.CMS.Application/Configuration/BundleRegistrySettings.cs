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
    /// Si true, computa el SRI integrity (sha384) del entry script cuando
    /// NI el <c>manifest.json</c> NI el <c>meta.json</c> lo traen. Cacheado
    /// in-memory por path hasta que el FileSystemWatcher detecte cambio.
    /// Default <c>true</c>. Sólo aplica al cliente de filesystem: el HTTP no
    /// puede descargarse cada bundle para hashearlo.
    /// </summary>
    /// <remarks>
    /// Es un RESPALDO, no la fuente. El SRI se publica en <c>meta.json</c>
    /// desde siempre, y este cálculo fue lo que tapó durante meses que el
    /// cliente de filesystem lo leyera en el fichero equivocado — mientras su
    /// gemelo HTTP, incapaz de calcular nada, devolvía <c>null</c> a secas
    /// (defecto #32). Se lee lo publicado primero.
    /// </remarks>
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
    /// Cada cuánto se vuelve a leer el registry cuando <c>Mode=Http</c>.
    /// </summary>
    /// <remarks>
    /// <para><b>Nunca se busca en la red durante un render</b>: se sirve del último snapshot
    /// bueno y este intervalo decide cada cuánto lo refresca alguien por detrás. Bajarlo mucho
    /// no hace que los cambios aparezcan antes de lo que tarda el CDN en propagarlos; solo
    /// multiplica las peticiones.</para>
    ///
    /// <para>Sesenta segundos es lo mismo que la caché del <c>registry.json</c> publicado, así
    /// que pedirlo más seguido devolvería la misma copia del borde.</para>
    /// </remarks>
    public int RefreshSeconds { get; init; } = 60;

    /// <summary>
    /// Cuánto se espera al CDN antes de darlo por no disponible (<c>Mode=Http</c>).
    /// </summary>
    /// <remarks>
    /// Corto a propósito. Este tiempo NO está en el camino de un render —el render usa el
    /// snapshot que ya hay— pero sí retiene un hilo durante el refresco. Un CDN que tarda más de
    /// unos segundos está caído para lo que a nosotros respecta.
    /// </remarks>
    public int TimeoutSeconds { get; init; } = 5;

    /// <summary>
    /// Elemento concreto que el <c>BundleRegistryProbe</c> vigila. <b>Vacío por defecto</b>: se
    /// pregunta por cualquiera, que es lo que corresponde a un chequeo del registry.
    /// </summary>
    /// <remarks>
    /// <para><b>El default era <c>"synergos-column"</c> y eso fue un defecto</b> (#39). El CDN
    /// retiró ese elemento a propósito, el probe se puso rojo con el registry perfecto, y el rojo
    /// era indistinguible del de un CDN caído. Un chequeo atado a un tag no vigila el registry:
    /// vigila que <i>ese</i> elemento siga publicado — y qué se publica no lo decide el CMS.</para>
    ///
    /// <para><b>Ponerlo sigue siendo legítimo</b>, y significa otra cosa: «avisame si este
    /// elemento en particular deja de resolver». Un operador que sepa que su sitio se cae sin
    /// cierto elemento lo quiere así. Pero es una decisión explícita, no lo que pasa por omisión —
    /// y el probe lo dice en su mensaje cuando falla, para que nadie confunda «se cayó el CDN» con
    /// «retiraron el elemento que yo elegí vigilar».</para>
    /// </remarks>
    public string ProbeTag { get; init; } = string.Empty;

    /// <summary>
    /// Exige lo que el modo <c>Http</c> necesita y no puede darse por hecho: una URL base
    /// absoluta. Lanza si falta, <b>al cablear</b>.
    /// </summary>
    /// <remarks>
    /// <para><b>El defecto que evita</b> (#56). <c>CLAUDE.md</c> §11 manda encender el CDN con dos
    /// variables. Con la primera puesta y la segunda olvidada, el compose pasa
    /// <c>PublicBaseUrl</c> <b>presente y vacía</b> —no ausente—, así que pisa el default de esta
    /// clase; la URL sale relativa (<c>/synergos/registry.json</c>) y el <c>HttpClient</c> del
    /// registry no tiene <c>BaseAddress</c>. Un default protege de la ausencia, no del vacío.</para>
    ///
    /// <para><b>Y el arranque quedaba verde.</b> El warmup sólo resuelve el cliente —no descarga—
    /// y atrapa <c>Exception</c> entera, así que el contenedor subía, <c>/health</c> contestaba y
    /// la prueba de humo pasaba. Es la misma forma que ya costó dos veces: la llave de firma de
    /// <c>Api.Identity</c> (HU #14, rebanada 3) y el <c>TimeProvider</c> de la ADR 0132. La regla
    /// está escrita doce líneas encima del registro que fallaba, y no se le aplicó al único valor
    /// que el operador escribe a mano.</para>
    ///
    /// <para><b>Se exige absoluta sólo en <c>Http</c></b>, a propósito: el default
    /// <c>/cdn-bundles</c> es relativo y es el correcto para <c>FileSystem</c>, donde la sirve el
    /// propio sitio. Exigirla siempre rompería el camino local.</para>
    ///
    /// <para><b>Lanza en vez de caer a <c>Stub</c></b>, por lo mismo que <c>Api.Identity</c>: uno
    /// que dice servir bundles y no puede es peor que uno caído, porque parece que funciona. Caer
    /// a <c>Stub</c> dejaría el sitio en pie sin ningún elemento — justo la degradación silenciosa
    /// que este arreglo existe para acabar.</para>
    /// </remarks>
    /// <param name="publicBaseUrl">Lo que trae la configuración, tal cual.</param>
    /// <exception cref="InvalidOperationException">Si falta o no es absoluta.</exception>
    public static void ExigirUrlPublicaAbsoluta(string? publicBaseUrl)
    {
        if (string.IsNullOrWhiteSpace(publicBaseUrl))
        {
            throw new InvalidOperationException(
                "Synergos:BundleRegistry:Mode=Http exige Synergos:BundleRegistry:PublicBaseUrl, y "
                + "llegó vacía. En el despliegue es SYNERGOS_CDN_URL — el compose la pasa presente "
                + "y vacía cuando no está definida, así que pisa el default en vez de dejarlo. Sin "
                + "ella el registry se pediría a una URL relativa y ninguna página con un "
                + "<synergos-*> renderizaría, con el arranque en verde.");
        }

        if (!Uri.TryCreate(publicBaseUrl.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException(
                $"Synergos:BundleRegistry:PublicBaseUrl vale «{publicBaseUrl}», que no es una URL "
                + "http(s) absoluta. En modo Http el cliente no tiene BaseAddress, así que una "
                + "relativa —el default /cdn-bundles, correcto para FileSystem— no se puede pedir.");
        }
    }
}
