using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Implementación filesystem-backed del <see cref="IBundleRegistryClient"/>.
/// Cap-280 Batch B (Olas 283-285). Lee el <c>registry.json</c> +
/// <c>manifest.json</c> per-element directamente del disco. Cero
/// scripts manuales — el operador deja los archivos en el LocalPath
/// y el cliente los detecta + sirve automáticamente.
/// </summary>
/// <remarks>
/// Patrón heredado del legacy (<c>_archive/fails/Synergos.CMS.epicfail2</c>):
///
/// 1. <b>Boot</b>: carga <c>registry.json</c> a un dictionary.
/// 2. <b>Hot-reload</b>: <see cref="FileSystemWatcher"/> con debounce
///    detecta cambios y recarga el registry + invalida manifest cache
///    de los elementos afectados. Atomic swap del dictionary
///    (<c>volatile</c> reference) garantiza reads lock-free.
/// 3. <b>SRI</b>: lo que traiga el <c>manifest.json</c>, si no lo que el
///    publicador escribió en <c>meta.json</c> (que es donde vive de
///    verdad), y sólo si no hay ninguno de los dos se calcula on-the-fly
///    del <c>main.js</c>. Ese cálculo es un RESPALDO, no la fuente: es lo
///    que tapó durante meses que este cliente leyera el SRI en el fichero
///    equivocado — su gemelo HTTP, que no puede calcular nada, devolvía
///    <c>null</c> siempre (defecto #32).
/// 4. <b>Degradación graceful</b>: registry roto o ausente → log
///    Warning + el cliente retorna null para todos los tags. Manifests
///    individuales rotos → log + ese tag retorna null, el resto sigue.
///
/// Resolución del tag DOM (e.g. <c>synergos-column</c>) al folder
/// filesystem (e.g. <c>column/</c>): si
/// <see cref="BundleRegistrySettings.StripFolderPrefix"/> es true,
/// el client strip <c>synergos-</c> antes de buscar. Sino busca literal.
/// </remarks>
public sealed class FileSystemBundleRegistryClient : IBundleRegistryClient, IDisposable
{
    private static readonly JsonSerializerOptions DeserializeOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IOptionsMonitor<BundleRegistrySettings> _settings;
    private readonly ILogger<FileSystemBundleRegistryClient> _logger;
    private readonly object _reloadLock = new();
    private readonly ConcurrentDictionary<string, ManifestCacheEntry> _manifestCache = new(StringComparer.OrdinalIgnoreCase);

    // volatile field permite atomic reference swap; readers ven snapshot
    // consistente sin lock.
    private volatile RegistrySnapshot? _snapshot;

    // FileSystemWatcher + debounce timer.
    private FileSystemWatcher? _watcher;
    private System.Timers.Timer? _debounceTimer;
    private bool _disposed;

    public FileSystemBundleRegistryClient(
        IOptionsMonitor<BundleRegistrySettings> settings,
        ILogger<FileSystemBundleRegistryClient> logger)
    {
        _settings = settings;
        _logger = logger;
        InitialLoad();
    }

    /// <inheritdoc />
    public async Task<BundleDescriptor?> TryResolveAnyAsync(CancellationToken ct = default)
    {
        var snap = _snapshot;
        if (snap is null || snap.ByTag.Count == 0) return null;

        // Se prueban VARIOS y no sólo el primero: un elemento roto —sin implementación para el
        // framework por defecto, con el manifiesto ilegible— no significa que el registry esté
        // caído, y rendirse en el primero convertiría un bache en un rojo del sitio entero.
        //
        // El tope existe porque esto corre dentro de /_health: recorrer el registry entero ante
        // un CDN de verdad roto convertiría el chequeo de salud en el trabajo más caro del
        // proceso, justo cuando algo anda mal. Cuántos elementos hay no cambia el argumento —y
        // por eso no va acá: la cifra vive en CLAUDE.md §11 y sólo ahí (#86).
        var intentos = 0;
        foreach (var tag in snap.ByTag.Keys)
        {
            if (intentos++ >= MaxSondeos) break;
            var d = await TryResolveAsync(tag, ct).ConfigureAwait(false);
            if (d is not null) return d;
        }

        return null;
    }

    /// <summary>Cuántos elementos se prueban antes de declarar que el registry no sirve ninguno.</summary>
    private const int MaxSondeos = 5;

    public Task<BundleDescriptor?> TryResolveAsync(string elementKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(elementKey)) return Task.FromResult<BundleDescriptor?>(null);

        var snap = _snapshot;
        if (snap is null) return Task.FromResult<BundleDescriptor?>(null);

        // Lookup por tag, alias, o name (en orden). Tu registry tiene los 3.
        if (!snap.ByTag.TryGetValue(elementKey, out var element) &&
            !snap.ByAlias.TryGetValue(elementKey, out element) &&
            !snap.ByName.TryGetValue(elementKey, out element))
        {
            // Tag con prefix synergos- pero registry indexed sin prefix?
            // El registry actual SÍ guarda el tag completo, pero defensivo.
            return Task.FromResult<BundleDescriptor?>(null);
        }

        var s = _settings.CurrentValue;
        var framework = ChooseFramework(element, s.DefaultFramework);
        if (framework is null)
        {
            _logger.LogWarning(
                "Bundle {Tag}: no implementations available, returning null", element.Tag);
            return Task.FromResult<BundleDescriptor?>(null);
        }

        var slot = s.DefaultSlot;
        if (!element.Implementations![framework].TryGetValue(slot, out var version))
        {
            // Slot solicitado no existe — fallback a "latest" si es distinto.
            if (slot != "latest" && element.Implementations![framework].TryGetValue("latest", out var latestVersion))
            {
                version = latestVersion;
                slot = "latest";
            }
            else
            {
                _logger.LogWarning(
                    "Bundle {Tag} {Framework}: slot {Slot} not found and no latest fallback",
                    element.Tag, framework, s.DefaultSlot);
                return Task.FromResult<BundleDescriptor?>(null);
            }
        }

        var folderName = ResolveFolderName(element.Name, element.Tag, s.StripFolderPrefix);
        var bundleDir = Path.Combine(snap.BundlesRootPath, folderName, framework, slot);
        var manifestPath = Path.Combine(bundleDir, "manifest.json");

        var manifest = LoadManifestCached(manifestPath);
        if (manifest is null)
        {
            return Task.FromResult<BundleDescriptor?>(null);
        }

        var entryScript = string.IsNullOrWhiteSpace(manifest.EntryScript) ? "main.js" : manifest.EntryScript;
        // PERF: emitir la URL VERSIONADA (semver, p.ej. /0.1.0/main.js) en vez del pointer
        // mutable (/latest/). El versionado se sirve con Cache-Control immutable max-age=1y
        // (Program.cs) → el navegador NO revalida en cada navegación (el no-cache de /latest/
        // causaba round-trips por bundle en cada page nav = lentitud). Un republish cambia el
        // semver en el registry (hot-reload) → cache-bust natural. SRI idéntico (mismo main.js).
        var urlSlot = System.Text.RegularExpressions.Regex.IsMatch(version, @"^\d+\.\d+\.\d+")
            ? version
            : slot;
        var mainEntryUrl = BuildPublicUrl(s.PublicBaseUrl, s.BundlesNamespace, folderName, framework, urlSlot, entryScript);

        // Orden: lo que el manifiesto diga, lo que el publicador escribió en meta.json, y sólo
        // entonces calcularlo. Leer meta.json ANTES de calcular es lo que arregló el defecto #32:
        // el SRI se publica desde siempre y este cliente lo ignoraba — no se notaba porque su
        // respaldo tapaba la lectura equivocada. Su gemelo HTTP no puede calcular nada, así que
        // ahí el mismo error quedaba a la vista como un `integrity` nulo.
        //
        // Y hace que las dos formas de leer el mismo registry vuelvan a coincidir: recalcular
        // emitía sha384 donde el CDN publica sha256, o sea DOS SRI distintos para el MISMO
        // fichero según por dónde se leyera. Los dos son válidos, pero divergentes; y esta clase
        // ya advertía que si los gemelos divergen, uno de los dos está leyendo mal.
        string? integrity = manifest.Integrity;
        if (string.IsNullOrWhiteSpace(integrity))
        {
            integrity = LoadPublishedIntegrity(bundleDir, entryScript);
        }
        if (string.IsNullOrWhiteSpace(integrity) && s.ComputeIntegrityIfMissing)
        {
            // Del entryScript, NO de `main.js` a secas: este path estaba cableado a `main.js` y
            // habría hasheado el fichero equivocado para cualquier elemento que publicara otra
            // entrada — el mismo defecto de #32 (leer el SRI de otro fichero) cinco líneas más
            // abajo. Hoy no lo dispara nadie porque TODAS las entradas del CDN son `main.js`,
            // que es justamente por lo que nunca se vio. Lo que importa es «todas», no cuántas.
            integrity = ComputeIntegrityCached(Path.Combine(bundleDir, entryScript));
        }

        var descriptor = new BundleDescriptor(
            MainEntryUri: new Uri(mainEntryUrl, UriKind.RelativeOrAbsolute),
            Dependencies: ResolveDependencyUris(element, snap, s),
            Version: version,
            Tag: element.Tag,
            Alias: element.Alias,
            Tier: element.Tier,
            Integrity: integrity,
            Framework: framework);

        return Task.FromResult<BundleDescriptor?>(descriptor);
    }

    private static string? ChooseFramework(RegistryElement element, string defaultFramework)
    {
        if (element.Implementations is null || element.Implementations.Count == 0) return null;
        if (element.Implementations.ContainsKey(defaultFramework)) return defaultFramework;
        // Fallback: primer framework disponible (orden no garantizado pero
        // determinista por StringComparer del dict construido).
        return element.Implementations.Keys.FirstOrDefault();
    }

    /// <summary>
    /// Traduce los nombres de <see cref="RegistryElement.Dependencies"/> a las URLs de sus
    /// bundles, en orden de carga. El emitter las escribe como &lt;script&gt; ANTES del
    /// principal (<c>BuildScriptTags</c>), que es lo que hace que un custom element escrito
    /// crudo en la plantilla de otra app llegue registrado al navegador.
    ///
    /// Era el eslabón que faltaba: el descriptor ya llevaba <c>Dependencies</c> y el emitter
    /// ya las recorría, pero aquí se pasaba <c>Array.Empty&lt;Uri&gt;()</c> fijo. Capacidad
    /// completa y sin rellenar nunca — el bundle existía publicado y nadie lo cargaba.
    ///
    /// Degrada en silencio y a propósito: una dependencia que no resuelva (nombre mal escrito,
    /// bundle sin publicar, manifiesto ausente) se OMITE con un warning en vez de tumbar el
    /// elemento anfitrión. Perder el contador es malo; perder la ficha entera por el contador
    /// es peor.
    /// </summary>
    private IReadOnlyList<Uri> ResolveDependencyUris(
        RegistryElement element, RegistrySnapshot snap, BundleRegistrySettings s)
    {
        if (element.Dependencies is null || element.Dependencies.Count == 0)
        {
            return Array.Empty<Uri>();
        }

        var uris = new List<Uri>(element.Dependencies.Count);
        foreach (var depName in element.Dependencies)
        {
            if (string.IsNullOrWhiteSpace(depName))
            {
                continue;
            }

            // Sin recursión: una dependencia se resuelve a su bundle, no a su árbol. Así el
            // ciclo A→B→A no puede colgar el render, y una cadena de 3 niveles se declara
            // explícita en quien la necesita en vez de aparecer por herencia invisible.
            if (string.Equals(depName, element.Name, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "Bundle {Name}: se declara dependencia de sí mismo, se omite", element.Name);
                continue;
            }

            if (!snap.ByName.TryGetValue(depName, out var dep))
            {
                _logger.LogWarning(
                    "Bundle {Name}: la dependencia '{Dep}' no está en el registry, se omite",
                    element.Name, depName);
                continue;
            }

            var url = TryBuildMainEntryUrl(dep, snap, s);
            if (url is null)
            {
                _logger.LogWarning(
                    "Bundle {Name}: la dependencia '{Dep}' está en el registry pero su bundle no resuelve, se omite",
                    element.Name, depName);
                continue;
            }

            uris.Add(new Uri(url, UriKind.RelativeOrAbsolute));
        }

        return uris;
    }

    /// <summary>
    /// La URL pública del bundle de un elemento, o null si no resuelve. Misma cascada de
    /// framework/slot/manifiesto que usa la resolución principal.
    /// </summary>
    private string? TryBuildMainEntryUrl(
        RegistryElement element, RegistrySnapshot snap, BundleRegistrySettings s)
    {
        var framework = ChooseFramework(element, s.DefaultFramework);
        if (framework is null)
        {
            return null;
        }

        var slot = s.DefaultSlot;
        if (!element.Implementations![framework].TryGetValue(slot, out var version))
        {
            if (slot == "latest" || !element.Implementations![framework].TryGetValue("latest", out version))
            {
                return null;
            }
        }

        var folderName = ResolveFolderName(element.Name, element.Tag, s.StripFolderPrefix);
        var manifest = LoadManifestCached(
            Path.Combine(snap.BundlesRootPath, folderName, framework, slot, "manifest.json"));
        if (manifest is null)
        {
            return null;
        }

        var entryScript = string.IsNullOrWhiteSpace(manifest.EntryScript) ? "main.js" : manifest.EntryScript;
        var urlSlot = System.Text.RegularExpressions.Regex.IsMatch(version, @"^\d+\.\d+\.\d+")
            ? version
            : slot;

        return BuildPublicUrl(s.PublicBaseUrl, s.BundlesNamespace, folderName, framework, urlSlot, entryScript);
    }

    private static string ResolveFolderName(string name, string tag, bool stripPrefix)
    {
        // Preferimos el `name` del registry (e.g. "column") porque es
        // exactamente el folder filesystem. El tag con/sin strip es
        // fallback defensivo para registries que solo tengan tag.
        if (!string.IsNullOrWhiteSpace(name)) return name;
        if (stripPrefix && tag.StartsWith("synergos-", StringComparison.OrdinalIgnoreCase))
        {
            return tag[9..]; // strip "synergos-"
        }
        return tag;
    }

    private static string BuildPublicUrl(string publicBase, string ns, string folder, string framework, string slot, string entryScript)
    {
        // PublicBase: "/cdn-bundles" (relative) o "https://cdn.example.com" (absolute).
        var baseTrimmed = publicBase.TrimEnd('/');
        return $"{baseTrimmed}/{ns}/{folder}/{framework}/{slot}/{entryScript}";
    }

    private ElementManifest? LoadManifestCached(string manifestPath)
    {
        if (_manifestCache.TryGetValue(manifestPath, out var cached) && cached.LastWriteUtc != DateTime.MinValue)
        {
            // Revalidate via mtime para minimal IO. Si filesystem watcher
            // ya invalidó (LastWriteUtc reset a MinValue), recarga.
            try
            {
                var currentMtime = File.GetLastWriteTimeUtc(manifestPath);
                if (currentMtime == cached.LastWriteUtc) return cached.Manifest;
            }
            catch (IOException)
            {
                // Archivo borrado entre check e read; cae al reload abajo.
            }
        }

        try
        {
            var bytes = File.ReadAllBytes(manifestPath);
            var manifest = JsonSerializer.Deserialize<ElementManifest>(bytes, DeserializeOptions);
            if (manifest is null) return null;
            var mtime = File.GetLastWriteTimeUtc(manifestPath);
            _manifestCache[manifestPath] = new ManifestCacheEntry(manifest, mtime);
            return manifest;
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            _logger.LogWarning(ex, "Failed to read/parse manifest {Path}", manifestPath);
            return null;
        }
    }

    /// <summary>
    /// El SRI que el publicador ya escribió en <c>meta.json</c>, si describe al script que se va
    /// a emitir.
    /// </summary>
    /// <remarks>
    /// La comprobación del <c>entryScript</c> es la misma que hace el gemelo HTTP y por la misma
    /// razón: <c>meta.json</c> hashea <c>main.js</c> —su <c>bundleSize</c> es el de ese fichero—,
    /// no «la entrada, sea cual sea». Con otro <c>entryScript</c> se cae al cálculo local, que sí
    /// sabe hashear el fichero correcto. Un SRI equivocado no degrada el elemento: lo borra.
    /// </remarks>
    private string? LoadPublishedIntegrity(string bundleDir, string entryScript)
    {
        if (!string.Equals(entryScript, "main.js", StringComparison.Ordinal)) return null;

        var metaPath = Path.Combine(bundleDir, "meta.json");
        try
        {
            if (_metaCache.TryGetValue(metaPath, out var cached)
                && cached.LastWriteUtc == File.GetLastWriteTimeUtc(metaPath))
            {
                return cached.Integrity;
            }
        }
        catch (IOException)
        {
            // Borrado entre el check y la lectura; cae al reload de abajo.
        }

        try
        {
            var meta = JsonSerializer.Deserialize<ElementMeta>(File.ReadAllBytes(metaPath), DeserializeOptions);
            if (string.IsNullOrWhiteSpace(meta?.Integrity)) return null;
            _metaCache[metaPath] = new IntegrityCacheEntry(meta!.Integrity!, File.GetLastWriteTimeUtc(metaPath));
            return meta.Integrity;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Un CDN local sin meta.json es lo normal en un clon a medio publicar. Se cae al
            // cálculo, que es lo que este cliente venía haciendo siempre.
            return null;
        }
    }

    private static readonly ConcurrentDictionary<string, IntegrityCacheEntry> _metaCache =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly ConcurrentDictionary<string, IntegrityCacheEntry> _integrityCache =
        new(StringComparer.OrdinalIgnoreCase);

    private string? ComputeIntegrityCached(string mainJsPath)
    {
        try
        {
            var mtime = File.GetLastWriteTimeUtc(mainJsPath);
            if (_integrityCache.TryGetValue(mainJsPath, out var cached) && cached.LastWriteUtc == mtime)
            {
                return cached.Integrity;
            }
            var bytes = File.ReadAllBytes(mainJsPath);
            var hash = SHA384.HashData(bytes);
            var sri = $"sha384-{Convert.ToBase64String(hash)}";
            _integrityCache[mainJsPath] = new IntegrityCacheEntry(sri, mtime);
            return sri;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Failed to compute SRI integrity for {Path}", mainJsPath);
            return null;
        }
    }

    private void InitialLoad()
    {
        var s = _settings.CurrentValue;
        if (!string.Equals(s.Mode, "FileSystem", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation(
                "FileSystemBundleRegistryClient: Mode={Mode} (no-op until set to FileSystem)", s.Mode);
            return;
        }
        if (string.IsNullOrWhiteSpace(s.LocalPath) || !Directory.Exists(s.LocalPath))
        {
            _logger.LogWarning(
                "FileSystemBundleRegistryClient: LocalPath missing or invalid: {Path}", s.LocalPath);
            return;
        }

        ReloadRegistry();

        if (s.HotReloadEnabled)
        {
            try
            {
                var bundlesRoot = Path.Combine(s.LocalPath, s.BundlesNamespace);
                _watcher = new FileSystemWatcher(bundlesRoot)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName,
                    EnableRaisingEvents = true,
                };
                _watcher.Changed += OnFileSystemChanged;
                _watcher.Created += OnFileSystemChanged;
                _watcher.Deleted += OnFileSystemChanged;
                _watcher.Renamed += OnFileSystemChanged;
                _logger.LogInformation(
                    "FileSystemBundleRegistryClient: hot-reload watcher attached to {Path}", bundlesRoot);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "FileSystemBundleRegistryClient: failed to attach FileSystemWatcher; manual restart required for changes");
            }
        }
    }

    private void OnFileSystemChanged(object sender, FileSystemEventArgs e)
    {
        // Debounce: cualquier cambio dispara un timer; reload corre 500ms
        // tras el último evento. Evita thrashing con bursts de writes.
        var s = _settings.CurrentValue;
        var debounceMs = Math.Max(50, s.HotReloadDebounceMilliseconds);

        lock (_reloadLock)
        {
            _debounceTimer?.Stop();
            _debounceTimer?.Dispose();
            _debounceTimer = new System.Timers.Timer(debounceMs) { AutoReset = false };
            _debounceTimer.Elapsed += (_, _) =>
            {
                try { ReloadRegistry(); InvalidateManifestCache(); }
                catch (Exception ex) { _logger.LogError(ex, "Hot-reload failed"); }
            };
            _debounceTimer.Start();
        }
    }

    private void InvalidateManifestCache()
    {
        // Reset todos los cache entries — reload completo. Cheap (~50 entries).
        _manifestCache.Clear();
        _integrityCache.Clear();
    }

    private void ReloadRegistry()
    {
        var s = _settings.CurrentValue;
        var bundlesRoot = Path.Combine(s.LocalPath, s.BundlesNamespace);
        var registryPath = Path.Combine(bundlesRoot, s.RegistryFileName);

        if (!File.Exists(registryPath))
        {
            _logger.LogWarning(
                "FileSystemBundleRegistryClient: registry file not found at {Path}", registryPath);
            _snapshot = null;
            return;
        }

        try
        {
            var bytes = File.ReadAllBytes(registryPath);
            var registry = JsonSerializer.Deserialize<RegistryFile>(bytes, DeserializeOptions);
            if (registry is null || registry.Elements is null)
            {
                _logger.LogWarning("Registry file empty or malformed: {Path}", registryPath);
                return;
            }

            var byTag = new Dictionary<string, RegistryElement>(StringComparer.OrdinalIgnoreCase);
            var byAlias = new Dictionary<string, RegistryElement>(StringComparer.OrdinalIgnoreCase);
            var byName = new Dictionary<string, RegistryElement>(StringComparer.OrdinalIgnoreCase);

            foreach (var el in registry.Elements)
            {
                if (string.IsNullOrWhiteSpace(el.Tag) || el.Implementations is null) continue;
                byTag[el.Tag] = el;
                if (!string.IsNullOrWhiteSpace(el.Alias)) byAlias[el.Alias] = el;
                if (!string.IsNullOrWhiteSpace(el.Name)) byName[el.Name] = el;
            }

            _snapshot = new RegistrySnapshot(bundlesRoot, byTag, byAlias, byName, registry.Elements.Count);
            _logger.LogInformation(
                "FileSystemBundleRegistryClient: registry loaded with {Count} elements from {Path}",
                registry.Elements.Count, registryPath);
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            _logger.LogError(ex, "Failed to load registry from {Path}", registryPath);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _watcher?.Dispose();
        _debounceTimer?.Dispose();
    }

    // ─── Internal records ──────────────────────────────────────────

    private sealed record RegistrySnapshot(
        string BundlesRootPath,
        IReadOnlyDictionary<string, RegistryElement> ByTag,
        IReadOnlyDictionary<string, RegistryElement> ByAlias,
        IReadOnlyDictionary<string, RegistryElement> ByName,
        int Count);

    private sealed class RegistryFile
    {
        public string? Generated { get; set; }
        public string? Version { get; set; }
        public string? BaseUrl { get; set; }
        public List<RegistryElement>? Elements { get; set; }
    }

    private sealed class RegistryElement
    {
        public string Name { get; set; } = string.Empty;
        public string Alias { get; set; } = string.Empty;
        public string Tag { get; set; } = string.Empty;
        public string Tier { get; set; } = string.Empty;
        public Dictionary<string, Dictionary<string, string>>? Implementations { get; set; }

        /// <summary>
        /// Otros elementos cuyo bundle hay que cargar ANTES que éste, por nombre de
        /// registro. Existe para los custom elements que una app escribe CRUDOS en su
        /// plantilla (p.ej. <c>eventos</c> pinta <c>&lt;synergos-countdown-clock&gt;</c>):
        /// esos tags no los compone el editor, así que no pasan por el SynHost y nadie
        /// emitía su script — el elemento quedaba en el DOM sin registrar y sin hidratar,
        /// dejando en pantalla el rótulo que lo acompaña y un hueco detrás.
        ///
        /// ⚠️ NO se usa para componentes Angular importados: ésos ya viajan dentro del
        /// bundle. Es sólo para elementos PUBLICADOS que otro elemento consume por tag
        /// (un elemento publicado es una app y no se importa — ADR 0113).
        /// </summary>
        public List<string>? Dependencies { get; set; }
    }

    private sealed class ElementManifest
    {
        public string? Tag { get; set; }
        public string? Alias { get; set; }
        public string? Framework { get; set; }
        public string? Version { get; set; }
        public string? Tier { get; set; }
        public string? EntryScript { get; set; }
        public string? Integrity { get; set; }
    }

    /// <summary>
    /// El <c>meta.json</c> que <c>publish.mjs</c> deja al lado del manifiesto — el fichero donde
    /// el SRI vive de verdad. <c>BundleSize</c> se lee aunque no se use: es lo que documenta que
    /// el <c>Integrity</c> de acá es el de <c>main.js</c>.
    /// </summary>
    private sealed class ElementMeta
    {
        public string? Element { get; set; }
        public string? Framework { get; set; }
        public string? Version { get; set; }
        public string? Commit { get; set; }
        public string? BuiltAt { get; set; }
        public long? BundleSize { get; set; }
        public string? Integrity { get; set; }
    }

    private sealed record ManifestCacheEntry(ElementManifest Manifest, DateTime LastWriteUtc);
    private sealed record IntegrityCacheEntry(string Integrity, DateTime LastWriteUtc);
}
