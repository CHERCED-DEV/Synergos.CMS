using Synergos.CMS.Interfaces.SynHost;
using Umbraco.Cms.Core.Models.PublishedContent;

namespace Synergos.CMS.Web.Services.SynHost;

/// <summary><c>elementSynLightboxGallery</c> → <see cref="LightboxGalleryProps"/>.</summary>
/// <remarks>
/// Parsea en el servidor el JSON de imágenes: <c>fullUrl</c>/<c>thumbUrl</c> salen como
/// <c>src</c>/<c>thumb</c>. Las columnas viajan como número.
/// </remarks>
public sealed class LightboxGalleryResolutor : IResolutorSynHost<LightboxGalleryProps>
{
    private readonly IPublishedValueFallback _fallback;
    private readonly ILogger<LightboxGalleryResolutor> _log;

    public LightboxGalleryResolutor(IPublishedValueFallback fallback, ILogger<LightboxGalleryResolutor> log)
    {
        _fallback = fallback;
        _log = log;
    }

    public ElementoResuelto<LightboxGalleryProps> Resolver(IPublishedElement elemento)
    {
        var editor = new LectorDelEditor(elemento, _fallback, _log);
        return new ElementoResuelto<LightboxGalleryProps>(new LightboxGalleryProps(
            Images: Imagenes(editor),
            Columns: editor.Entero("columns")));
    }

    private static IReadOnlyList<LightboxGalleryImage>? Imagenes(LectorDelEditor editor)
    {
        var lista = editor.ListaJson("imagesJson", editor.Texto("imagesJson"));
        if (lista is null)
        {
            return null;
        }

        var imagenes = new List<LightboxGalleryImage>();
        foreach (var entrada in lista)
        {
            var grande = LectorDelEditor.Cadena(entrada, "fullUrl");
            if (grande is null)
            {
                editor.NoEsValido("imagesJson", entrada.GetRawText(), "una imagen con fullUrl");
                continue;
            }

            imagenes.Add(new LightboxGalleryImage(
                grande,
                LectorDelEditor.Cadena(entrada, "thumbUrl"),
                LectorDelEditor.Cadena(entrada, "alt"),
                LectorDelEditor.Cadena(entrada, "caption")));
        }

        return imagenes.Count > 0 ? imagenes : null;
    }
}
