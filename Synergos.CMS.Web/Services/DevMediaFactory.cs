using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.IO;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.PropertyEditors;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Dev-only: crea nodos de Media (synImage) server-side vía
/// <see cref="IMediaService"/> y devuelve el valor JSON listo para un campo
/// MediaPicker3. Es el seam que faltaba para autorar imágenes en Umbraco 13
/// (no hay Management API; el upload multipart no existe). Idempotente por nombre.
/// </summary>
/// <remarks>
/// Dos fuentes de imagen, sin texto horneado en ninguna:
/// <list type="bullet">
/// <item><b>Ilustraciones premium reales</b> del brand kit (<c>_archive/multimedia/imgs</c>,
/// estilo render 3D suave azul/blanco) para blog headers, splits y feature-media. El
/// mapeo nombre-de-nodo → archivo vive en <see cref="RealAssets"/>.</item>
/// <item><b>Gradiente limpio on-brand</b> (<see cref="GenerateGradientPng"/>) para heroes
/// (banners oscuros), productos físicos y avatares — donde no aplica ninguna ilustración.
/// El gradiente sale de hexFrom/hexTo (la marca del siteRoot que pasa el caller): cero
/// colores random, CERO texto.</item>
/// </list>
/// Las imágenes se organizan en CARPETAS de Media (<see cref="GetOrCreateFolder"/>):
/// Ilustraciones / Heroes / Productos / Avatares / Marca. Nada queda en la raíz.
/// Gated indirectamente: solo se invoca desde tooling /dev/* gated por
/// <c>Synergos:DevSeed:Enabled</c> (ADR 0013). synImage.umbracoFile es
/// Umbraco.ImageCropper; la extensión SetValue(file) escribe el archivo y rellena el
/// cropper + width/height/bytes/extension automáticamente.
/// </remarks>
public sealed class DevMediaFactory
{
    private const string MediaTypeAlias = "synImage";
    private const string FolderTypeAlias = "Folder";

    // Carpetas de Media bajo la raíz. Cada media se coloca en la carpeta de su uso
    // (directiva del arquitecto: "las imágenes deben ir en carpetas dentro de media").
    private const string FolderIlustraciones = "Ilustraciones"; // blog headers, splits, features (ilustraciones reales)
    private const string FolderHeroes        = "Heroes";        // banners hero (gradiente limpio on-brand)
    private const string FolderProductos     = "Productos";     // imágenes de producto (gradiente limpio, sin foto real)
    private const string FolderAvatares      = "Avatares";      // avatares de autor (gradiente limpio)
    private const string FolderMarca         = "Marca";         // logos / isotipos / OG image

    // ───────────────────────────── Mapeo imagen → contenido ─────────────────────────────
    // Nombre-de-nodo (lo que pasa DevContentFiller) → ilustración premium real del kit
    // (relativa a _archive/multimedia, estilo render 3D suave azul/blanco). Si el archivo
    // existe se importa el real; si no, se cae al gradiente limpio on-brand (sin texto).
    //
    // Las ilustraciones son CLARAS (fondo blanco) → encajan junto al texto (splits/features)
    // y como cabecera de blog (que se renderiza sobre superficie clara). NO sirven como fondo
    // de un hero oscuro: por eso los heroes NO están acá y usan gradiente limpio.
    private static readonly Dictionary<string, string> RealAssets = new(StringComparer.Ordinal)
    {
        // ── Cabeceras de blog (key = "Blog {title}", lo que pasa SeedPost) ──────────────
        // Mapeo temático directo; cero reuso del mismo asset entre posts.
        ["Blog Un motor, mil productos: la idea detrás de SynergosLabs"] = "imgs/infofeat 1.png", // cubos 3D componibles = un motor
        ["Blog Lanzamos los verticales: Blogs y Tienda"]                 = "imgs/infofeat 6.png", // columnas + puente = dos verticales conectados
        ["Blog Componer en vez de programar: el editor visual"]          = "imgs/infofeat 4.png", // bombillo + engranaje = construir/componer
        ["Blog Arquitectura por capas: el grafo de dependencias"]        = "imgs/infofeat 2.png", // red de nodos = grafo de dependencias
        ["Blog Identidad por siteRoot: una marca, mil caras"]            = "imgs/mision 2.png",   // personas + piezas = una marca encaja en muchas
        ["Blog CDN híbrida: componentes Angular sobre SSR"]              = "imgs/mision 1.png",   // bombillo + engranaje + chart = innovación técnica

        // ── Ilustraciones in-article (los nombres cortos que pasa BuildArticle) ─────────
        // Coherentes con la cabecera del mismo post.
        ["Blog Motor Componible"] = "imgs/infofeat 1.png", // cubos 3D = núcleo componible
        ["Blog Verticales"]       = "imgs/infofeat 6.png", // puente entre columnas = verticales
        ["Blog Editor Visual"]    = "imgs/infofeat 4.png", // ideas/construir = editor
        ["Blog Arquitectura"]     = "imgs/infofeat 2.png", // red de nodos = grafo
        ["Blog Identidad"]        = "imgs/mision 2.png",   // encaje de piezas = identidad
        ["Blog CDN"]              = "imgs/mision 1.png",   // innovación técnica = distribución híbrida

        // ── Splits / feature-media de las páginas de marca (azul/blanco sobre claro) ────
        ["Synergos Capas"]       = "imgs/infofeat 6.png", // columnas + puente = arquitectura por capas
        ["Synergos Proposito"]   = "imgs/infofeat 4.png", // bombillo + engranaje = propósito
        ["Synergos Polimorfico"] = "imgs/infofeat 1.png", // cubos 3D = polimórfico / muchos productos
        ["Synergos Branding"]    = "imgs/mision 2.png",   // piezas + handshake = tu marca encaja
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
    /// Devuelve el valor JSON de un MediaPicker3 apuntando a una imagen synImage con
    /// el nombre dado; la crea si no existe. Si el nombre está mapeado a una ilustración
    /// real del kit (<see cref="RealAssets"/>) la importa a la carpeta <c>Ilustraciones</c>;
    /// si no, genera un gradiente LIMPIO on-brand (hexFrom→hexTo, SIN texto) en la carpeta
    /// que corresponda por el tipo de nodo (Heroes / Productos / Avatares).
    /// </summary>
    public string GetOrCreatePickerValue(string name, string altText,
        string hexFrom = "#0A2540", string hexTo = "#0F58A7", int width = 1600, int height = 900)
    {
        var mediaKey = GetOrCreate(name, altText, hexFrom, hexTo, width, height);
        return BuildPickerJson(mediaKey);
    }

    /// <summary>
    /// Devuelve el valor JSON de un MediaPicker3 apuntando a la OG image de marca
    /// (1200×630, gradiente dark limpio + isotipo centrado, SIN texto) para social cards.
    /// La crea/refresca idempotente por nombre, en la carpeta <c>Marca</c>. (P0-3 — el
    /// &lt;head&gt; emite og:image solo si socialOgImage está seteado.)
    /// </summary>
    public string GetOrCreateOgImagePickerValue(string name, string altText)
    {
        var folderId = GetOrCreateFolder(FolderMarca);
        var png = GenerateOgPng("#0A2540", "#4f6ef7");
        var mediaKey = GetOrCreateFromBytes(name, altText, png, folderId);
        return BuildPickerJson(mediaKey);
    }

    private static string BuildPickerJson(Guid mediaKey)
    {
        var entryKey = Guid.NewGuid().ToString();
        return $"[{{\"key\":\"{entryKey}\",\"mediaKey\":\"{mediaKey}\",\"crops\":[],\"focalPoint\":null}}]";
    }

    // ───────────────────────────── Carpetas de Media ─────────────────────────────

    /// <summary>
    /// Busca (o crea) una carpeta de Media (MediaType <c>Folder</c>) bajo la raíz con el
    /// nombre dado y devuelve su Id, listo para usarse como ParentId al crear imágenes.
    /// Idempotente: si la carpeta ya existe la reusa. Así ninguna media queda "quemada"
    /// en la raíz (directiva del arquitecto).
    /// </summary>
    public int GetOrCreateFolder(string folderName)
    {
        var existing = _mediaService.GetRootMedia()
            .FirstOrDefault(m => m.ContentType.Alias == FolderTypeAlias &&
                                 string.Equals(m.Name, folderName, StringComparison.Ordinal));
        if (existing is not null) { return existing.Id; }

        var folder = _mediaService.CreateMedia(folderName, Constants.System.Root, FolderTypeAlias);
        _mediaService.Save(folder);
        _logger.LogInformation("[DevMedia] carpeta de Media '{Folder}' creada.", folderName);
        return folder.Id;
    }

    // ───────────────────────────── Resolución de carpeta destino ─────────────────────────────

    // Elige la carpeta destino según el uso del nodo (deducido del nombre y de si está
    // mapeado a una ilustración real). Ilustraciones reales → Ilustraciones; heroes →
    // Heroes; productos → Productos; avatares → Avatares; el resto (logos de clientes,
    // etc.) → Ilustraciones por defecto.
    private string ResolveFolderName(string name)
    {
        if (RealAssets.ContainsKey(name)) { return FolderIlustraciones; }
        if (name.StartsWith("Producto ", StringComparison.Ordinal)) { return FolderProductos; }
        if (name.StartsWith("Avatar ", StringComparison.Ordinal)) { return FolderAvatares; }
        if (IsHero(name)) { return FolderHeroes; }
        // Splits/features de blog/marca no mapeados + logo cloud → Ilustraciones.
        return FolderIlustraciones;
    }

    // Un nodo es "hero" si su nombre termina en "Hero" (heroes verticales/página) o es uno
    // de los heroes de la Entidad cuyo nombre no lleva ese sufijo.
    private static bool IsHero(string name)
        => name.EndsWith(" Hero", StringComparison.Ordinal)
        || name is "Synergos Home Hero"
                or "Synergos Identidad"
                or "Synergos Productos"
                or "Synergos Contacto";

    // ───────────────────────────── Get-or-create de la imagen ─────────────────────────────

    private Guid GetOrCreate(string name, string altText, string hexFrom, string hexTo, int width, int height)
    {
        var realPath = ResolveRealAsset(name);
        var folderId = GetOrCreateFolder(ResolveFolderName(name));

        if (realPath is not null)
        {
            using var stream = System.IO.File.OpenRead(realPath);
            var fileName = Slug(name) + Path.GetExtension(realPath);
            return UpsertMedia(name, altText, fileName, stream, folderId,
                "[DevMedia] ilustración real para '{Name}' ({Path}) en carpeta {Folder}.",
                realPath);
        }

        // Sin ilustración real → gradiente LIMPIO on-brand (sin texto). Aviso explícito
        // para los nodos donde el arquitecto querría una FOTO/ilustración real.
        WarnIfNeedsRealMedia(name);
        var png = GenerateGradientPng(hexFrom, hexTo, width, height);
        using var ms = new MemoryStream(png);
        return UpsertMedia(name, altText, Slug(name) + ".png", ms, folderId, null, null);
    }

    private Guid GetOrCreateFromBytes(string name, string altText, byte[] png, int folderId)
    {
        using var ms = new MemoryStream(png);
        return UpsertMedia(name, altText, Slug(name) + ".png", ms, folderId, null, null);
    }

    /// <summary>
    /// Crea (o refresca) un nodo synImage con el binario dado bajo <paramref name="parentId"/>.
    /// Idempotente: busca el nodo existente por nombre en CUALQUIER ubicación (raíz o carpeta
    /// — por compatibilidad con media sembrada antes de las carpetas) y, si existe, refresca
    /// su archivo (misma Key → no rompe referencias por mediaKey) y lo reubica a la carpeta
    /// correcta vía Move (no destructivo). Si no existe, lo crea ya dentro de la carpeta.
    /// </summary>
    private Guid UpsertMedia(string name, string altText, string fileName, Stream file, int parentId,
        string? logTemplate, string? logPath)
    {
        var existing = FindMediaByName(name);
        if (existing is not null)
        {
            existing.SetValue(_mediaFileManager, _mediaUrlGenerators, _shortStringHelper,
                _contentTypeBaseServiceProvider, "umbracoFile", fileName, file);
            existing.SetValue("altDefault", altText);
            _mediaService.Save(existing);
            // Reubica a la carpeta correcta si quedó en otra ubicación (p.ej. en la raíz
            // de una corrida previa). Move es no destructivo y preserva la Key.
            if (existing.ParentId != parentId) { _mediaService.Move(existing, parentId); }
            if (logTemplate is not null) { _logger.LogInformation(logTemplate, name, logPath, MoveTargetName(parentId)); }
            return existing.Key;
        }

        var media = _mediaService.CreateMedia(name, parentId, MediaTypeAlias);
        media.SetValue(_mediaFileManager, _mediaUrlGenerators, _shortStringHelper,
            _contentTypeBaseServiceProvider, "umbracoFile", fileName, file);
        media.SetValue("altDefault", altText); // synImage.altDefault es Variations=Nothing
        _mediaService.Save(media);
        if (logTemplate is not null) { _logger.LogInformation(logTemplate, name, logPath, MoveTargetName(parentId)); }
        return media.Key;
    }

    // Busca un synImage por nombre en toda la biblioteca (raíz + carpetas de 1 nivel).
    private IMedia? FindMediaByName(string name)
    {
        var atRoot = _mediaService.GetRootMedia()
            .FirstOrDefault(m => m.ContentType.Alias == MediaTypeAlias &&
                                 string.Equals(m.Name, name, StringComparison.Ordinal));
        if (atRoot is not null) { return atRoot; }

        foreach (var folder in _mediaService.GetRootMedia().Where(m => m.ContentType.Alias == FolderTypeAlias))
        {
            var hit = _mediaService.GetPagedChildren(folder.Id, 0, 1000, out _)
                .FirstOrDefault(m => m.ContentType.Alias == MediaTypeAlias &&
                                     string.Equals(m.Name, name, StringComparison.Ordinal));
            if (hit is not null) { return hit; }
        }
        return null;
    }

    private string MoveTargetName(int parentId)
        => _mediaService.GetById(parentId)?.Name ?? "(raíz)";

    // Aviso explícito para los nodos que IDEALMENTE llevan una foto/ilustración real
    // provista por el arquitecto (productos físicos, equipo real). El gradiente limpio
    // on-brand es el placeholder digno mientras tanto — nunca texto pixelado.
    private void WarnIfNeedsRealMedia(string name)
    {
        if (name.StartsWith("Producto ", StringComparison.Ordinal))
        {
            _logger.LogWarning("[DevMedia] producto '{Name}' sin foto real — usando gradiente limpio; subir foto real de catálogo.", name);
        }
        else if (name.StartsWith("Avatar ", StringComparison.Ordinal))
        {
            _logger.LogWarning("[DevMedia] avatar '{Name}' sin foto real — usando gradiente limpio; subir retrato real del autor/equipo.", name);
        }
    }

    // ───────────────────────────── Gradiente limpio on-brand (SIN texto) ─────────────────────────────

    /// <summary>
    /// Gradiente diagonal de marca (hexFrom→hexTo) con un highlight radial suave y una
    /// viñeta sutil para profundidad. CERO texto, cero motivos: superficie premium limpia
    /// lista para que el contenido (título blanco del hero, ficha del producto) vaya encima.
    /// </summary>
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

    // OG card: gradiente branded limpio 1200×630 + isotipo centrado (si se encuentra). Sin texto.
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
            // Si el composite falla (isotipo ilegible), el gradiente branded limpio sirve.
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
            if (System.IO.File.Exists(nested)) { return nested; }
            var direct = Path.Combine(dir.FullName, "wwwroot", "img", "synergoslabs-isotipo.png");
            if (System.IO.File.Exists(direct)) { return direct; }
            dir = dir.Parent;
        }
        return null;
    }

    // Resuelve la ilustración real del kit subiendo desde el CWD hasta encontrar _archive/multimedia.
    private static string? ResolveRealAsset(string name)
    {
        if (!RealAssets.TryGetValue(name, out var rel)) { return null; }
        var relNative = rel.Replace('/', Path.DirectorySeparatorChar);
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "_archive", "multimedia", relNative);
            if (System.IO.File.Exists(candidate)) { return candidate; }
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
