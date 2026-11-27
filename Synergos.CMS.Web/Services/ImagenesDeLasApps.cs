namespace Synergos.CMS.Web.Services;

/// <summary>
/// La foto de la tarjeta de una app (Alquiler, Tienda, Eventos, Educación) al ancho en que se ve (D-016-Y2, la regla de
/// X2c sobre <see cref="ImagenServida"/>): las APIs entregaban el original —hasta 946 KB por foto, 3,5 MB en
/// /alquiler/— para una tarjeta de 216 a 289 px.
/// </summary>
/// <remarks>
/// Se aplica a la SALIDA de la API y sólo en las listas: la URL nace en el catálogo o en el inventario y no se toca en el
/// origen, y la ficha de detalle conserva su foto grande.
/// </remarks>
public static class ImagenesDeLasApps
{
    /// <summary>La tarjeta: de 216 a 289 px en el teléfono y en el escritorio, al 2×.</summary>
    public const int AnchoDeLaTarjeta = 640;

    public static string? Tarjeta(string? url) => ImagenServida.Url(url, AnchoDeLaTarjeta);
}
