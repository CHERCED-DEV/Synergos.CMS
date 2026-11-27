namespace Synergos.CMS.Web.Services;

/// <summary>
/// Las fotos de Blogs al ancho en que se ven (012-blogs-social, sobre <see cref="ImagenServida"/> de D-016-X2): la portada
/// de una publicación, el avatar de quien firma y la foto grande del artículo. Va la misma regla en las vistas, en el
/// respaldo SSR de <c>&lt;synergos-blogs&gt;</c> y en la API que lee la app. Con la MISMA URL en los tres sitios, la app no
/// vuelve a pedir la foto después de hidratar.
/// </summary>
/// <remarks>
/// Se aplica a la SALIDA, nunca a lo que se siembra en el stream. La URL de la portada entra en la huella de
/// <c>CatalogContentStream</c>, que sólo sabe crear: cambiarla en el origen sembraría cada artículo otra vez, y el viejo
/// se quedaría en el feed.
/// </remarks>
public static class ImagenesDeBlogs
{
    /// <summary>La portada en una tarjeta: 526 px en escritorio y 341 en el teléfono (682 con pantalla 2×).</summary>
    public const int AnchoDePortada = 800;

    /// <summary>
    /// El avatar: de 32 a 64 px en la app, 56 en el artículo y 96 en la página del autor. 192 cubre el mayor en una
    /// pantalla 2×, y así es una sola URL por avatar.
    /// </summary>
    public const int AnchoDeAvatar = 192;

    /// <summary>La foto grande del artículo: 800 px en escritorio y el ancho entero en el teléfono.</summary>
    public const int AnchoDeLaFotoDelArticulo = 1200;

    public static string? Portada(string? url) => ImagenServida.Url(url, AnchoDePortada);

    public static string? Avatar(string? url) => ImagenServida.Url(url, AnchoDeAvatar);

    public static string? FotoDelArticulo(string? url) => ImagenServida.Url(url, AnchoDeLaFotoDelArticulo);
}
