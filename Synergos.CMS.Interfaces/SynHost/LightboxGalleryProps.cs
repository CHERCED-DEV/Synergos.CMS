namespace Synergos.CMS.Interfaces.SynHost;

/// <summary>
/// Lo que viaja a <c>&lt;synergos-lightbox-gallery&gt;</c>.
/// </summary>
/// <remarks>
/// <para><b>El defecto que cierra (D1).</b> La vista mandaba el TEXTO <c>imagesJson</c> y el
/// elemento lee la LISTA <c>images</c> en el <c>config</c> (<c>imagesJson</c> sólo lo acepta como
/// atributo): la galería colocada decía «No hay imágenes para mostrar». <c>columns</c> sí llegaba
/// —el elemento acepta el número escrito como texto— y ahora viaja como número.</para>
///
/// <para><b>Cada imagen</b>: el editor escribe <c>fullUrl</c>/<c>thumbUrl</c> (así lo documenta el
/// ElementType) y el elemento lee <c>src</c>/<c>thumb</c>; <c>alt</c> y <c>caption</c> se llaman
/// igual. Una imagen sin <c>fullUrl</c> no viaja y se anota: agrandar la miniatura sería inventar
/// la imagen grande.</para>
///
/// <para><c>closeLabel</c> y <c>emptyLabel</c> los acepta el elemento y no los autora el
/// ElementType: quedan como atributos.</para>
/// </remarks>
[ElementoSynHost("lightbox-gallery", TipoDeColocable.Pieza)]
public sealed record LightboxGalleryProps(
    [property: CampoSynHost(OrigenDelCampo.Contenido)] IReadOnlyList<LightboxGalleryImage>? Images,
    [property: CampoSynHost(OrigenDelCampo.Decision)] int? Columns);

/// <summary>Una imagen de la galería: la grande, su miniatura, su texto alternativo y su pie.</summary>
public sealed record LightboxGalleryImage(string Src, string? Thumb = null, string? Alt = null, string? Caption = null);
