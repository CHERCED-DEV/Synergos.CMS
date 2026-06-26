using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.IO;
using Umbraco.Cms.Core.PropertyEditors;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Dev-only: crea nodos de Media (synImage) server-side vía
/// <see cref="IMediaService"/> con una imagen PNG generada (gradiente
/// branded vía ImageSharp — ya viene con Umbraco, sin paquete nuevo) y
/// devuelve el valor JSON listo para un campo MediaPicker3. Es el seam
/// que faltaba para autorar imágenes en Umbraco 13 (no hay Management
/// API; el upload multipart no existe). Idempotente por nombre.
/// </summary>
/// <remarks>
/// Gated indirectamente: solo se invoca desde tooling /dev/* gated por
/// <c>Synergos:DevSeed:Enabled</c> (ADR 0013). synImage.umbracoFile es
/// Umbraco.ImageCropper; la extensión SetValue(file) escribe el archivo
/// y rellena el cropper + width/height/bytes/extension automáticamente.
/// </remarks>
public sealed class DevMediaFactory
{
    private const string MediaTypeAlias = "synImage";

    // Mapa nombre-de-nodo → asset real del brand kit (relativo a _archive/multimedia).
    // Si el archivo existe, se importa el real; si no, se cae al gradiente generado.
    private static readonly Dictionary<string, string> RealAssets = new(StringComparer.Ordinal)
    {
        // Heroes (full-bleed): fondo oscuro on-brand SIN texto horneado → texto blanco
        // legible. Antes Home/Identidad/Casos usaban hero_branded_* (lleva grabado
        // "Synergos / Composable Digital Solutions" = marca de agua que competía con el
        // título); migrados a hero_bg (mismo gradiente, sin texto). S5a.
        ["Synergos Home Hero"] = "inspirations/files 2/hero_bg_synergos_dark.png",
        ["Synergos Identidad"] = "inspirations/files 2/hero_bg_synergos_dark.png",
        ["Synergos Casos Hero"]         = "inspirations/files 2/hero_bg_synergos_dark.png",
        ["Synergos Productos"] = "inspirations/files 2/hero_bg_synergos_dark.png",
        ["Synergos Contacto"]  = "inspirations/files 2/hero_bg_synergos_dark.png",
        ["Synergos Soluciones Hero"]    = "inspirations/files 2/hero_bg_synergos_dark.png",
        ["Synergos Como Funciona Hero"] = "inspirations/files 2/hero_bg_synergos_dark.png",
        ["Synergos Precios Hero"]       = "inspirations/files 2/hero_bg_synergos_dark.png",
        // Verticales: arte on-brand por tema (P1-5) — set hero_branded (misma
        // composición dark-base, acento gold/dark) para legibilidad consistente.
        // Verticales: fondo SIN texto horneado (los "branded_*" llevan "Synergos /
        // Composable Digital Solutions" grabado → competía con el título del vertical).
        ["Synergos Blogs Hero"]  = "inspirations/files 2/hero_bg_synergos_gold.png",
        ["Synergos Tienda Hero"] = "inspirations/files 2/hero_bg_synergos_dark.png",
        // Splits (imagen lateral): ilustraciones reales on-brand (fondo claro OK al lado del texto).
        ["Synergos Capas"]       = "imgs/infofeat 6.png", // columnas + puente = arquitectura por capas
        ["Synergos Proposito"]   = "imgs/infofeat 4.png", // bombillo + engranaje = propósito
        ["Synergos Polimorfico"] = "imgs/infofeat 1.png", // cubos 3D = polimórfico
        ["Synergos Branding"]    = "imgs/mision 2.png",   // piezas + handshake = tu marca encaja
        // S4 — cabecera gold a medida por post (1 distinta por artículo, reconocible).
        // Key = "Blog {title}" (lo que pasa SeedPost). Un typo cae silencioso al gradiente.
        ["Blog Un motor, mil productos: la idea detrás de SynergosLabs"] = "inspirations/s2/blog_header_synergos_gold_aurora.png",
        ["Blog Lanzamos los verticales: Blogs y Tienda"]                 = "inspirations/s2/blog_header_synergos_gold_ripples.png",
        ["Blog Componer en vez de programar: el editor visual"]          = "inspirations/s2/blog_header_synergos_gold_waves.png",
        ["Blog Arquitectura por capas: el grafo de dependencias"]        = "inspirations/s2/blog_header_synergos_gold_network.png",
        ["Blog Identidad por siteRoot: una marca, mil caras"]            = "inspirations/s2/blog_header_synergos_gold_blobs.png",
        ["Blog CDN híbrida: componentes Angular sobre SSR"]              = "inspirations/s2/blog_header_synergos_gold_aurora.png",
    };

    private readonly IMediaService _mediaService;
    private readonly MediaFileManager _mediaFileManager;
    private readonly MediaUrlGeneratorCollection _mediaUrlGenerators;
    private readonly IShortStringHelper _shortStringHelper;
    private readonly IContentTypeBaseServiceProvider _contentTypeBaseServiceProvider;
    private readonly ILogger<DevMediaFactory> _logger;

    public DevMediaFactory(
        IMediaService mediaService,
        MediaFileManager mediaFileManager,
        MediaUrlGeneratorCollection mediaUrlGenerators,
        IShortStringHelper shortStringHelper,
        IContentTypeBaseServiceProvider contentTypeBaseServiceProvider,
        ILogger<DevMediaFactory> logger)
    {
        _mediaService = mediaService;
        _mediaFileManager = mediaFileManager;
        _mediaUrlGenerators = mediaUrlGenerators;
        _shortStringHelper = shortStringHelper;
        _contentTypeBaseServiceProvider = contentTypeBaseServiceProvider;
        _logger = logger;
    }

    /// <summary>
    /// Devuelve el valor JSON de un MediaPicker3 apuntando a una imagen
    /// synImage con el nombre dado; la crea (gradiente branded) si no existe.
    /// </summary>
    public string GetOrCreatePickerValue(string name, string altText,
        string hexFrom = "#0A2540", string hexTo = "#0F58A7", int width = 1600, int height = 900)
    {
        var mediaKey = GetOrCreate(name, altText, hexFrom, hexTo, width, height);
        var entryKey = Guid.NewGuid().ToString();
        return $"[{{\"key\":\"{entryKey}\",\"mediaKey\":\"{mediaKey}\",\"crops\":[],\"focalPoint\":null}}]";
    }

    /// <summary>
    /// Devuelve el valor JSON de un MediaPicker3 apuntando a la OG image de
    /// marca (1200×630, gradiente dark + isotipo centrado) para social cards.
    /// La crea/refresca idempotente por nombre. (P0-3 — el &lt;head&gt; emite
    /// og:image solo si socialOgImage está seteado.)
    /// </summary>
    public string GetOrCreateOgImagePickerValue(string name, string altText)
    {
        var mediaKey = GetOrCreateOg(name, altText);
        var entryKey = Guid.NewGuid().ToString();
        return $"[{{\"key\":\"{entryKey}\",\"mediaKey\":\"{mediaKey}\",\"crops\":[],\"focalPoint\":null}}]";
    }

    private Guid GetOrCreateOg(string name, string altText)
    {
        var png = GenerateOgPng("#0A2540", "#4f6ef7");
        var existing = _mediaService.GetRootMedia()
            .FirstOrDefault(m => m.ContentType.Alias == MediaTypeAlias &&
                                 string.Equals(m.Name, name, StringComparison.Ordinal));
        if (existing is not null)
        {
            using var fs0 = new MemoryStream(png);
            existing.SetValue(_mediaFileManager, _mediaUrlGenerators, _shortStringHelper,
                _contentTypeBaseServiceProvider, "umbracoFile", Slug(name) + ".png", fs0);
            _mediaService.Save(existing);
            return existing.Key;
        }
        var media = _mediaService.CreateMedia(name, Constants.System.Root, MediaTypeAlias);
        using var stream = new MemoryStream(png);
        media.SetValue(_mediaFileManager, _mediaUrlGenerators, _shortStringHelper,
            _contentTypeBaseServiceProvider, "umbracoFile", Slug(name) + ".png", stream);
        media.SetValue("altDefault", altText);
        _mediaService.Save(media);
        _logger.LogInformation("DevMediaFactory: OG image '{Name}' creada (1200×630).", name);
        return media.Key;
    }

    // OG card: gradiente branded 1200×630 + isotipo centrado (si se encuentra).
    private static byte[] GenerateOgPng(string hexFrom, string hexTo)
    {
        const int w = 1200, h = 630;
        var baseBytes = GenerateGradientPng(hexFrom, hexTo, w, h);
        var isotipo = ResolveIsotipo();
        if (isotipo is null) { return baseBytes; }
        try
        {
            using var image = Image.Load<Rgba32>(baseBytes);
            using var logo = Image.Load<Rgba32>(isotipo);
            logo.Mutate(x => x.Resize(new ResizeOptions { Size = new Size(0, 260), Mode = ResizeMode.Max }));
            var loc = new Point((w - logo.Width) / 2, (h - logo.Height) / 2);
            image.Mutate(x => x.DrawImage(logo, loc, 1f));
            using var ms = new MemoryStream();
            image.Save(ms, new PngEncoder());
            return ms.ToArray();
        }
        catch
        {
            // Si el composite falla (isotipo ilegible), el gradiente branded sirve.
            return baseBytes;
        }
    }

    // Resuelve el isotipo subiendo desde el CWD (sirve corriendo desde el Web project o la raíz).
    private static string? ResolveIsotipo()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null)
        {
            var nested = Path.Combine(dir.FullName, "Synergos.CMS.Web", "wwwroot", "img", "synergoslabs-isotipo.png");
            if (File.Exists(nested)) { return nested; }
            var direct = Path.Combine(dir.FullName, "wwwroot", "img", "synergoslabs-isotipo.png");
            if (File.Exists(direct)) { return direct; }
            dir = dir.Parent;
        }
        return null;
    }

    private Guid GetOrCreate(string name, string altText, string hexFrom, string hexTo, int width, int height)
    {
        var realPath = ResolveRealAsset(name);
        var existing = _mediaService.GetRootMedia()
            .FirstOrDefault(m => m.ContentType.Alias == MediaTypeAlias &&
                                 string.Equals(m.Name, name, StringComparison.Ordinal));
        if (existing is not null)
        {
            // Refresca el binario del nodo existente si hay asset real (misma key → no rompe
            // las referencias por mediaKey en el contenido; solo cambia el archivo).
            if (realPath is not null)
            {
                using var fs0 = File.OpenRead(realPath);
                existing.SetValue(_mediaFileManager, _mediaUrlGenerators, _shortStringHelper,
                    _contentTypeBaseServiceProvider, "umbracoFile", Slug(name) + Path.GetExtension(realPath), fs0);
                _mediaService.Save(existing);
                _logger.LogInformation("DevMediaFactory: synImage '{Name}' actualizado al asset real.", name);
            }
            return existing.Key;
        }

        var media = _mediaService.CreateMedia(name, Constants.System.Root, MediaTypeAlias);

        if (realPath is not null)
        {
            using var fs = File.OpenRead(realPath);
            media.SetValue(_mediaFileManager, _mediaUrlGenerators, _shortStringHelper,
                _contentTypeBaseServiceProvider, "umbracoFile", Slug(name) + Path.GetExtension(realPath), fs);
            _logger.LogInformation("DevMediaFactory: synImage '{Name}' importado del kit ({Path}).", name, realPath);
        }
        else
        {
            var png = GenerateGradientPng(hexFrom, hexTo, width, height);
            using var stream = new MemoryStream(png);
            media.SetValue(_mediaFileManager, _mediaUrlGenerators, _shortStringHelper,
                _contentTypeBaseServiceProvider, "umbracoFile", Slug(name) + ".png", stream);
            _logger.LogInformation("DevMediaFactory: synImage '{Name}' creado (gradiente).", name);
        }
        media.SetValue("altDefault", altText); // Variations=Nothing

        _mediaService.Save(media);
        return media.Key;
    }

    private static byte[] GenerateGradientPng(string hexFrom, string hexTo, int width, int height)
    {
        var c1 = Rgba32.ParseHex(hexFrom);
        var c2 = Rgba32.ParseHex(hexTo);
        // Punto focal del highlight radial (tercio superior-izquierdo).
        var fx = width * 0.30f;
        var fy = height * 0.28f;
        var maxDist = MathF.Sqrt((width * width) + (height * height));
        var halfW = width / 2f;
        var halfH = height / 2f;
        using var image = new Image<Rgba32>(width, height);
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    // Base: gradiente diagonal.
                    var t = (float)(x + y) / (width + height);
                    var r = c1.R + ((c2.R - c1.R) * t);
                    var g = c1.G + ((c2.G - c1.G) * t);
                    var b = c1.B + ((c2.B - c1.B) * t);

                    // Highlight radial aditivo (luz suave desde el focal).
                    var dx = x - fx;
                    var dy = y - fy;
                    var dist = MathF.Sqrt((dx * dx) + (dy * dy)) / maxDist;
                    var glow = MathF.Max(0f, 1f - (dist * 1.8f));
                    glow = glow * glow * 42f;

                    // Viñeta sutil (oscurece bordes para dar profundidad).
                    var nx = (x - halfW) / halfW;
                    var ny = (y - halfH) / halfH;
                    var edge = MathF.Min(1f, (nx * nx) + (ny * ny));
                    var vignette = edge * edge * 36f;

                    row[x] = new Rgba32(
                        (byte)Clamp(r + glow - vignette),
                        (byte)Clamp(g + glow - vignette),
                        (byte)Clamp(b + glow - vignette),
                        255);
                }
            }
        });
        using var ms = new MemoryStream();
        image.Save(ms, new PngEncoder());
        return ms.ToArray();
    }

    // Resuelve el asset real del kit subiendo desde el CWD hasta encontrar _archive/multimedia.
    private static string? ResolveRealAsset(string name)
    {
        if (!RealAssets.TryGetValue(name, out var rel)) { return null; }
        var relNative = rel.Replace('/', Path.DirectorySeparatorChar);
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "_archive", "multimedia", relNative);
            if (File.Exists(candidate)) { return candidate; }
            dir = dir.Parent;
        }
        return null;
    }

    private static float Clamp(float v) => v < 0f ? 0f : (v > 255f ? 255f : v);

    private static string Slug(string s)
    {
        var clean = new string(s.ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray());
        while (clean.Contains("--")) clean = clean.Replace("--", "-");
        return clean.Trim('-');
    }
}
