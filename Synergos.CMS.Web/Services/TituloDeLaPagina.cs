namespace Synergos.CMS.Web.Services;

/// <summary>
/// El título de una página (<c>&lt;title&gt;</c>, <c>og:title</c>): el suyo, el de la marca, o su nombre con el del sitio
/// (D-016-X3). Sin título propio, 30 páginas se titulaban sólo «Carrito», «Pago», «Producto» o «Mi cuenta», que en una
/// pestaña o un resultado de búsqueda no dicen de qué sitio son. La raíz de un sitio se titula con el nombre que muestra el sitio («Educación», no «Educacion»).
/// </summary>
public static class TituloDeLaPagina
{
    public static string Resolver(string? propio, string? deLaMarca, string? nombre, string sitio, bool esLaRaiz)
    {
        if (!string.IsNullOrWhiteSpace(propio)) { return propio; }
        if (!string.IsNullOrWhiteSpace(deLaMarca)) { return deLaMarca; }
        if (string.IsNullOrWhiteSpace(nombre) || esLaRaiz) { return sitio; }
        return string.Equals(nombre.Trim(), sitio.Trim(), StringComparison.OrdinalIgnoreCase)
            ? nombre
            : $"{nombre} — {sitio}";
    }
}
