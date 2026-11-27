namespace Synergos.CMS.Web.Services;

/// <summary>
/// Una foto de la biblioteca servida al ancho en que se ve (D-016-X2): ImageSharp la achica y la pasa a WebP. Las tarjetas
/// pedían el original —ilustraciones PNG de 0,5 a 1,2 MB, fotos de 1920 px— para pintarlo a 280–600 px; medido, una
/// ilustración baja de 1.217 KB a 18 KB y una foto de 377 KB a 17 KB a 640 px. Sólo se toca una ruta de <c>/media/</c>
/// en JPG, PNG o WebP sin consulta previa: un SVG, un enlace externo o una URL ya pedida con su tamaño no cambian.
/// </summary>
public static class ImagenServida
{
    private static readonly string[] Formatos = [".jpg", ".jpeg", ".png", ".webp"];

    public static string? Url(string? url, int ancho)
    {
        if (string.IsNullOrWhiteSpace(url) || ancho <= 0 || url.Contains('?', StringComparison.Ordinal)) { return url; }
        var ruta = url;
        var esDeLaBiblioteca = ruta.StartsWith("/media/", StringComparison.OrdinalIgnoreCase)
            || ruta.Contains("://", StringComparison.Ordinal) is false && ruta.Contains("/media/", StringComparison.OrdinalIgnoreCase);
        if (!esDeLaBiblioteca || !Formatos.Any(f => ruta.EndsWith(f, StringComparison.OrdinalIgnoreCase))) { return url; }
        return $"{url}?width={ancho}&format=webp&quality=80";
    }
}
