namespace Synergos.CMS.Web.Services;

/// <summary>
/// La foto de un inmueble en la lista y en el mapa, al ancho en que se ve (D-016-X2c, sobre <see cref="ImagenServida"/>):
/// la API de realty entregaba el original —de 165 a 330 KB por foto, 1,4 MB en /propiedades/— para una tarjeta.
/// </summary>
/// <remarks>
/// Se aplica a la SALIDA de la API, como en Blogs: la URL nace en el catálogo y no se toca en el origen. La galería de
/// la ficha no pasa por aquí; es la foto grande y va aparte.
/// </remarks>
public static class ImagenesDeInmuebles
{
    /// <summary>La tarjeta de la lista o del mapa: hasta unos 400 px en escritorio y el ancho entero en el teléfono, 2×.</summary>
    public const int AnchoDeLaTarjeta = 800;

    public static string Tarjeta(string url) => ImagenServida.Url(url, AnchoDeLaTarjeta) ?? url;
}
