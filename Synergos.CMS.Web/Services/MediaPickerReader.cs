using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.PublishedContent;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Lee un <c>Umbraco.MediaPicker3</c> sin importar cuál de las CUATRO formas
/// devuelva su converter. Punto único: si aquí está bien, está bien en toda la vitrina.
/// </summary>
/// <remarks>
/// <b>Existe porque era un bug VIVO y silencioso, repetido en 4 sitios.</b> El DataType
/// <c>Media Picker</c> (Key <c>4309a3ea…</c>) está configurado con <c>"Multiple": false</c>,
/// así que el converter devuelve UN ítem, no una colección — y pedirlo como
/// <c>IEnumerable&lt;IPublishedContent&gt;</c> da <b>null</b>, sin lanzar. Resultado: las
/// imágenes de producto de la vitrina SSR no funcionaron NUNCA.
///
/// <para>Se prueban las 4 formas porque dependen de la config del DataType y de la versión:
/// single/múltiple × <c>MediaWithCrops</c> (lo que MediaPicker3 devuelve cuando hay crops o
/// focal point) / <c>IPublishedContent</c>. Cambiar <c>Multiple</c> en el backoffice no puede
/// volver a dejar la tienda sin fotos.</para>
///
/// <para><b>Es <c>public</c>, no <c>internal</c>, a propósito.</b> Dos de sus consumidores son
/// vistas Razor (<c>_ProductDetailCore.cshtml</c>, <c>_SeoStructuredData.cshtml</c>) y el
/// proyecto compila Razor en RUNTIME (<c>RazorCompileOnBuild=false</c>, ModelsMode
/// InMemoryAuto): las vistas son OTRO ensamblado, así que un <c>internal</c> compilaría verde
/// y reventaría (CS0122) al pedir la página. Mismo criterio que <see cref="LayoutCssBuilder"/>.</para>
///
/// <para>Vive en <c>Synergos.CMS.Web</c> y no baja a Application (ADR 0002): depende de
/// Umbraco. Compárese con <c>CatalogText</c>, que sí es de Application porque es texto puro.</para>
///
/// <para>Devuelve los ítems y no URLs ya resueltas para que cada llamador elija su
/// <see cref="UrlMode"/> — <c>_SeoStructuredData</c> necesita absolutas para el JSON-LD
/// (una URL relativa en <c>schema.org/Product.image</c> no la resuelve el crawler) mientras
/// la vitrina usa relativas.</para>
/// </remarks>
public static class MediaPickerReader
{
    /// <summary>
    /// Los ítems de media de <paramref name="alias"/>, o vacío si no hay ninguno.
    /// Nunca null.
    /// </summary>
    public static IReadOnlyList<IPublishedContent> ReadMedia(IPublishedContent? content, string alias)
    {
        if (content is null || string.IsNullOrWhiteSpace(alias))
        {
            return Array.Empty<IPublishedContent>();
        }

        // Orden deliberado: de la forma más específica a la más general. MediaWithCrops ES un
        // IPublishedContent, así que las ramas "plain" también atraparían los crops por
        // covarianza — pero pedir primero el tipo exacto evita depender de ese detalle.
        if (content.Value<IEnumerable<MediaWithCrops>>(alias) is { } many)
        {
            return many.Where(m => m is not null).ToArray();
        }

        if (content.Value<MediaWithCrops>(alias) is { } one)
        {
            return new IPublishedContent[] { one };
        }

        if (content.Value<IEnumerable<IPublishedContent>>(alias) is { } manyPlain)
        {
            return manyPlain.Where(m => m is not null).ToArray();
        }

        return content.Value<IPublishedContent>(alias) is { } onePlain
            ? new[] { onePlain }
            : Array.Empty<IPublishedContent>();
    }

    /// <summary>
    /// Las URLs no vacías de los ítems de <paramref name="alias"/>. Nunca null.
    /// </summary>
    public static IReadOnlyList<string> ReadMediaUrls(
        IPublishedContent? content,
        string alias,
        UrlMode mode = UrlMode.Default)
        => ReadMedia(content, alias)
            .Select(m => m.Url(mode: mode))
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .ToArray();

    /// <summary>
    /// La URL del primer ítem de <paramref name="alias"/>, o null si no hay imagen.
    /// </summary>
    public static string? ReadFirstMediaUrl(
        IPublishedContent? content,
        string alias,
        UrlMode mode = UrlMode.Default)
    {
        var urls = ReadMediaUrls(content, alias, mode);
        return urls.Count > 0 ? urls[0] : null;
    }
}
