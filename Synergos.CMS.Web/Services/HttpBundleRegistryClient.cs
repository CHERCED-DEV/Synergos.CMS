using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// El <see cref="IBundleRegistryClient"/> que lee el registry por HTTP — el gemelo remoto de
/// <see cref="FileSystemBundleRegistryClient"/>.
/// </summary>
/// <remarks>
/// <para><b>Esto es lo que ADR 0012 declaró «bloqueado externamente esperando al equipo del
/// CDN».</b> El bloqueo dejó de ser externo en algún momento y nadie movió la etiqueta: el equipo
/// del CDN somos nosotros, y el pipeline que publica el registry ya existía — publicaba a una
/// carpeta local, que es exactamente lo que el cliente de filesystem lee.</para>
///
/// <para><b>Mismo mapeo, distinto transporte.</b> La cascada de framework → slot → manifiesto y
/// la construcción de la URL salen tal cual del cliente de filesystem, que lleva tiempo
/// funcionando. Lo que cambia acá es de dónde llegan los bytes, y las tres decisiones que eso
/// obliga a tomar:</para>
///
/// <list type="number">
///   <item><b>Nunca se busca en la red durante un render.</b> Se sirve del último snapshot bueno
///   y un temporizador lo refresca por detrás. Ir a la red por elemento y por página metería una
///   ida y vuelta en el camino crítico de cada visita, y convertiría un CDN lento en un sitio
///   lento.</item>
///
///   <item><b>Si el registry deja de responder, se sigue sirviendo el último bueno.</b> Un
///   registry caído no puede vaciar una página que se venía pintando bien. Se grita en el log y
///   se sigue; el día que el snapshot viejo esté de verdad obsoleto, lo que falla es un bundle
///   concreto y no el sitio entero.</item>
///
///   <item><b>Los manifiestos se cachean para siempre por su URL versionada.</b> Esa ruta es
///   inmutable por contrato —la versión exacta nunca cambia de contenido—, así que releerla sería
///   trabajo sin información.</item>
/// </list>
///
/// <para><b>Sin SRI, y dicho de frente.</b> El manifiesto que publica hoy <c>publish.mjs</c> no
/// trae <c>integrity</c>, y calcularlo acá exigiría descargarse cada bundle entero para hacerle
/// el hash. El sitio correcto para arreglarlo es el publicador —que ya calcula SRI para el
/// import-map del runtime, o sea que sabe hacerlo—, no el consumidor. Mientras tanto el
/// descriptor sale con <c>Integrity = null</c>, que es un valor previsto y documentado.</para>
/// </remarks>
public sealed class HttpBundleRegistryClient : IBundleRegistryClient, IDisposable
{
    private static readonly JsonSerializerOptions DeserializeOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;
    private readonly IOptionsMonitor<BundleRegistrySettings> _settings;
    private readonly ILogger<HttpBundleRegistryClient> _logger;
    private readonly TimeProvider _clock;

    private readonly SemaphoreSlim _cargando = new(1, 1);
    private readonly ConcurrentDictionary<string, ElementManifest> _manifiestos = new(StringComparer.Ordinal);

    // Referencia volátil: los lectores ven un snapshot consistente sin cerrojo, y el refresco
    // lo reemplaza entero de un golpe. Nunca hay un snapshot a medio construir.
    private volatile Snapshot? _snapshot;
    private DateTimeOffset _cargadoUtc = DateTimeOffset.MinValue;
    private bool _disposed;

    public HttpBundleRegistryClient(
        HttpClient http,
        IOptionsMonitor<BundleRegistrySettings> settings,
        ILogger<HttpBundleRegistryClient> logger,
        TimeProvider clock)
    {
        _http = http;
        _settings = settings;
        _logger = logger;
        _clock = clock;

        // A propósito NO se carga en el constructor: sería E/S de red en el arranque de la
        // aplicación, y un CDN lento retrasaría el boot del CMS entero. El warmup hosted service
        // llama a TryResolveAsync una vez, y ahí se paga la primera carga.
    }

    public async Task<BundleDescriptor?> TryResolveAsync(string elementKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(elementKey)) return null;

        var s = _settings.CurrentValue;
        var snap = await ObtenerSnapshotAsync(s, ct).ConfigureAwait(false);
        if (snap is null) return null;

        // Por tag, alias o nombre — el registry trae los tres, y quien pregunta puede tener
        // cualquiera de ellos a mano según de dónde venga.
        if (!snap.PorTag.TryGetValue(elementKey, out var el)
            && !snap.PorAlias.TryGetValue(elementKey, out el)
            && !snap.PorNombre.TryGetValue(elementKey, out el))
        {
            return null;
        }

        var framework = ElegirFramework(el, s.DefaultFramework);
        if (framework is null)
        {
            _logger.LogWarning("Bundle {Tag}: el registry no lista ninguna implementación", el.Tag);
            return null;
        }

        var (slot, version) = ElegirSlot(el, framework, s.DefaultSlot);
        if (version is null)
        {
            _logger.LogWarning(
                "Bundle {Tag} {Framework}: no existe el slot {Slot} ni hay 'latest' al que caer",
                el.Tag, framework, s.DefaultSlot);
            return null;
        }

        var carpeta = ResolverCarpeta(el.Name, el.Tag, s.StripFolderPrefix);

        // Se apunta SIEMPRE a la ruta versionada, no al puntero móvil. Es lo que permite que el
        // navegador la cachee como inmutable: `/latest/` obligaría a revalidar en cada
        // navegación, un viaje por bundle y por página.
        var rutaVersion = EsSemver(version) ? version : slot;

        var manifiesto = await ObtenerManifiestoAsync(s, carpeta, framework, rutaVersion, ct).ConfigureAwait(false);
        var entrada = string.IsNullOrWhiteSpace(manifiesto?.EntryScript) ? "main.js" : manifiesto!.EntryScript!;

        return new BundleDescriptor(
            MainEntryUri: new Uri(Url(s, carpeta, framework, rutaVersion, entrada), UriKind.RelativeOrAbsolute),
            Dependencies: ResolverDependencias(el, snap, s),
            Version: version,
            Tag: el.Tag,
            Alias: el.Alias,
            Tier: el.Tier,
            // Ver el remark de la clase: el publicador es quien tiene que emitirlo.
            Integrity: manifiesto?.Integrity,
            Framework: framework);
    }

    // ── El snapshot ─────────────────────────────────────────────────────────

    private async Task<Snapshot?> ObtenerSnapshotAsync(BundleRegistrySettings s, CancellationToken ct)
    {
        var actual = _snapshot;
        var vencido = _clock.GetUtcNow() - _cargadoUtc >= TimeSpan.FromSeconds(Math.Max(5, s.RefreshSeconds));

        // El camino caliente: hay snapshot y está fresco. Sin cerrojos, sin red.
        //
        // Es un ATAJO, no la garantía. Quitarlo no cambia lo que se devuelve —la comprobación de
        // adentro del cerrojo vuelve a mirar lo mismo— solo hace que cada resolución pase por el
        // semáforo. Se comprobó mutándolo: ningún test cae, porque no hay defecto que provocar.
        // Lo que sí cambia es que veinte bloques de una página se serialicen contra un cerrojo
        // para no hacer nada. Están las dos a propósito.
        if (actual is not null && !vencido) return actual;

        // Un solo hilo recarga; los demás siguen sirviendo el snapshot viejo en vez de hacer cola.
        // Con veinte bloques en una página, esperar todos a la misma recarga sería una estampida
        // que convierte un refresco de fondo en una pausa visible.
        if (actual is not null && !await _cargando.WaitAsync(0, ct).ConfigureAwait(false)) return actual;
        if (actual is null) await _cargando.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            // Otro hilo pudo haber recargado mientras esperábamos.
            if (_snapshot is not null && _clock.GetUtcNow() - _cargadoUtc < TimeSpan.FromSeconds(Math.Max(5, s.RefreshSeconds)))
            {
                return _snapshot;
            }

            var nuevo = await DescargarRegistryAsync(s, ct).ConfigureAwait(false);
            if (nuevo is not null)
            {
                _snapshot = nuevo;
                _cargadoUtc = _clock.GetUtcNow();
                return nuevo;
            }

            // No se pudo. Se marca el intento para no martillar el CDN caído en cada render, y
            // se devuelve el último bueno — que puede ser null si nunca hubo uno.
            _cargadoUtc = _clock.GetUtcNow();
            return _snapshot;
        }
        finally
        {
            _cargando.Release();
        }
    }

    private async Task<Snapshot?> DescargarRegistryAsync(BundleRegistrySettings s, CancellationToken ct)
    {
        var url = $"{s.PublicBaseUrl.TrimEnd('/')}/{s.BundlesNamespace}/{s.RegistryFileName}";

        try
        {
            var json = await _http.GetByteArrayAsync(url, ct).ConfigureAwait(false);
            var archivo = JsonSerializer.Deserialize<RegistryFile>(json, DeserializeOptions);

            if (archivo?.Elements is null || archivo.Elements.Count == 0)
            {
                // Un registry vacío es indistinguible de uno roto desde el punto de vista del
                // sitio: todos los elementos dejarían de resolver a la vez. Se rechaza y se sigue
                // con el anterior en vez de aceptar un reemplazo que apaga la página.
                _logger.LogWarning("El registry de {Url} vino vacío o ilegible; se conserva el anterior", url);
                return null;
            }

            var porTag = new Dictionary<string, RegistryElement>(StringComparer.OrdinalIgnoreCase);
            var porAlias = new Dictionary<string, RegistryElement>(StringComparer.OrdinalIgnoreCase);
            var porNombre = new Dictionary<string, RegistryElement>(StringComparer.OrdinalIgnoreCase);

            foreach (var el in archivo.Elements)
            {
                if (string.IsNullOrWhiteSpace(el.Tag) || el.Implementations is null) continue;
                porTag[el.Tag] = el;
                if (!string.IsNullOrWhiteSpace(el.Alias)) porAlias[el.Alias] = el;
                if (!string.IsNullOrWhiteSpace(el.Name)) porNombre[el.Name] = el;
            }

            _logger.LogInformation(
                "Registry cargado desde {Url}: {Count} elementos", url, archivo.Elements.Count);

            return new Snapshot(porTag, porAlias, porNombre);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            // Se grita, y se sigue con lo que había. Un CDN caído no puede vaciar una página que
            // se venía sirviendo bien.
            _logger.LogWarning(ex,
                "No se pudo leer el registry de {Url}. Se sigue con el snapshot anterior ({Estado})",
                url, _snapshot is null ? "no hay" : "vigente");
            return null;
        }
    }

    // ── Los manifiestos ─────────────────────────────────────────────────────

    private async Task<ElementManifest?> ObtenerManifiestoAsync(
        BundleRegistrySettings s, string carpeta, string framework, string rutaVersion, CancellationToken ct)
    {
        var url = Url(s, carpeta, framework, rutaVersion, "manifest.json");

        // Cachear sin vencimiento es correcto SOLO porque la ruta lleva la versión exacta, que
        // por contrato no cambia de contenido. Si algún día se pidiera el manifiesto de `latest`,
        // esta caché serviría datos viejos para siempre.
        if (_manifiestos.TryGetValue(url, out var cacheado)) return cacheado;

        try
        {
            var json = await _http.GetByteArrayAsync(url, ct).ConfigureAwait(false);
            var manifiesto = JsonSerializer.Deserialize<ElementManifest>(json, DeserializeOptions);
            if (manifiesto is not null) _manifiestos[url] = manifiesto;
            return manifiesto;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            // Sin manifiesto se cae al nombre de entrada por defecto en vez de no emitir nada:
            // `main.js` es lo que publica el 100% de los elementos hoy, así que perder el
            // manifiesto degrada la información, no el elemento.
            _logger.LogWarning(ex, "No se pudo leer el manifiesto {Url}; se asume main.js", url);
            return null;
        }
    }

    // ── Lo que sale igual que en el cliente de filesystem ───────────────────

    private IReadOnlyList<Uri> ResolverDependencias(RegistryElement el, Snapshot snap, BundleRegistrySettings s)
    {
        if (el.Dependencies is null || el.Dependencies.Count == 0) return Array.Empty<Uri>();

        var uris = new List<Uri>(el.Dependencies.Count);
        foreach (var nombre in el.Dependencies)
        {
            if (string.IsNullOrWhiteSpace(nombre)) continue;

            // Sin recursión, igual que en el cliente de filesystem: una dependencia se resuelve a
            // su bundle, no a su árbol. Así un ciclo A→B→A no puede colgar el render.
            if (string.Equals(nombre, el.Name, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Bundle {Name}: se declara dependencia de sí mismo, se omite", el.Name);
                continue;
            }

            if (!snap.PorNombre.TryGetValue(nombre, out var dep))
            {
                _logger.LogWarning(
                    "Bundle {Name}: la dependencia '{Dep}' no está en el registry, se omite", el.Name, nombre);
                continue;
            }

            var framework = ElegirFramework(dep, s.DefaultFramework);
            if (framework is null) continue;

            var (slot, version) = ElegirSlot(dep, framework, s.DefaultSlot);
            if (version is null) continue;

            // La dependencia NO espera a su manifiesto: sería una ida y vuelta más por cada una,
            // en serie, dentro del render. Se asume `main.js`, que es lo que publican todas.
            var carpeta = ResolverCarpeta(dep.Name, dep.Tag, s.StripFolderPrefix);
            var ruta = EsSemver(version) ? version : slot;
            uris.Add(new Uri(Url(s, carpeta, framework, ruta, "main.js"), UriKind.RelativeOrAbsolute));
        }

        return uris;
    }

    private static string? ElegirFramework(RegistryElement el, string porDefecto)
    {
        if (el.Implementations is null || el.Implementations.Count == 0) return null;
        return el.Implementations.ContainsKey(porDefecto) ? porDefecto : el.Implementations.Keys.FirstOrDefault();
    }

    private static (string Slot, string? Version) ElegirSlot(RegistryElement el, string framework, string pedido)
    {
        var impls = el.Implementations![framework];
        if (impls.TryGetValue(pedido, out var v)) return (pedido, v);
        if (pedido != "latest" && impls.TryGetValue("latest", out var latest)) return ("latest", latest);
        return (pedido, null);
    }

    private static string ResolverCarpeta(string name, string tag, bool quitarPrefijo)
    {
        if (!string.IsNullOrWhiteSpace(name)) return name;
        return quitarPrefijo && tag.StartsWith("synergos-", StringComparison.OrdinalIgnoreCase) ? tag[9..] : tag;
    }

    private static string Url(BundleRegistrySettings s, string carpeta, string framework, string slot, string archivo)
        => $"{s.PublicBaseUrl.TrimEnd('/')}/{s.BundlesNamespace}/{carpeta}/{framework}/{slot}/{archivo}";

    /// <summary>Si el texto empieza por <c>N.N.N</c> — o sea, si nombra una versión exacta.</summary>
    private static bool EsSemver(string v)
    {
        // A mano y no con una expresión regular: se llama una vez por elemento y por render, y
        // acá lo único que hace falta es distinguir `0.1.0` de `latest` o `v0`.
        var puntos = 0;
        foreach (var c in v)
        {
            if (c == '.') { puntos++; continue; }
            if (!char.IsAsciiDigit(c)) return puntos >= 2;
        }
        return puntos >= 2;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cargando.Dispose();
    }

    // ─── La forma de lo que llega ──────────────────────────────────────────
    // Idéntica a la del cliente de filesystem a propósito: es el MISMO registry, servido por
    // otro transporte. Si las dos formas divergieran, una de las dos estaría leyendo mal.

    private sealed record Snapshot(
        IReadOnlyDictionary<string, RegistryElement> PorTag,
        IReadOnlyDictionary<string, RegistryElement> PorAlias,
        IReadOnlyDictionary<string, RegistryElement> PorNombre);

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
}
