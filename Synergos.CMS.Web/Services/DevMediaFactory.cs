using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
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

    private Guid GetOrCreate(string name, string altText, string hexFrom, string hexTo, int width, int height)
    {
        var existing = _mediaService.GetRootMedia()
            .FirstOrDefault(m => m.ContentType.Alias == MediaTypeAlias &&
                                 string.Equals(m.Name, name, StringComparison.Ordinal));
        if (existing is not null)
        {
            return existing.Key;
        }

        var media = _mediaService.CreateMedia(name, Constants.System.Root, MediaTypeAlias);

        var png = GenerateGradientPng(hexFrom, hexTo, width, height);
        using (var stream = new MemoryStream(png))
        {
            var fileName = Slug(name) + ".png";
            media.SetValue(_mediaFileManager, _mediaUrlGenerators, _shortStringHelper,
                _contentTypeBaseServiceProvider, "umbracoFile", fileName, stream);
        }
        media.SetValue("altDefault", altText); // Variations=Nothing

        _mediaService.Save(media);
        _logger.LogInformation("DevMediaFactory: synImage '{Name}' creado (key={Key}).", name, media.Key);
        return media.Key;
    }

    private static byte[] GenerateGradientPng(string hexFrom, string hexTo, int width, int height)
    {
        var c1 = Rgba32.ParseHex(hexFrom);
        var c2 = Rgba32.ParseHex(hexTo);
        using var image = new Image<Rgba32>(width, height);
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    // Gradiente diagonal suave + viñeta sutil.
                    var t = (float)(x + y) / (width + height);
                    var r = (byte)(c1.R + (c2.R - c1.R) * t);
                    var g = (byte)(c1.G + (c2.G - c1.G) * t);
                    var b = (byte)(c1.B + (c2.B - c1.B) * t);
                    row[x] = new Rgba32(r, g, b, 255);
                }
            }
        });
        using var ms = new MemoryStream();
        image.Save(ms, new PngEncoder());
        return ms.ToArray();
    }

    private static string Slug(string s)
    {
        var clean = new string(s.ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray());
        while (clean.Contains("--")) clean = clean.Replace("--", "-");
        return clean.Trim('-');
    }
}
