using System.Text;
using System.Text.RegularExpressions;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Los créditos que una licencia pide mostrar (016-contenido-piezas): una foto CC BY sólo se puede usar si se ve quién la
/// hizo, de dónde salió y con qué licencia. El crédito vive en el medio (<c>creditLabel</c> y <c>license</c>); esto lo
/// convierte en la lista que publica la página «Créditos de las fotos».
/// </summary>
/// <remarks>
/// El crédito se guarda como <c>Foto: Autor · «Título» · https://origen</c> (el título es opcional). CC0 y dominio público
/// no piden atribución y no se listan.
/// </remarks>
public static partial class CreditosDeFotos
{
    [GeneratedRegex(@"^CC\s*BY(?<variante>(?:-(?:SA|ND|NC))*)\s+(?<version>\d\.\d)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LicenciaConAtribucion();

    /// <summary>Si la licencia pide mostrar el crédito (cualquier CC BY, con o sin SA, ND o NC).</summary>
    public static bool PideAtribucion(string? licencia) =>
        !string.IsNullOrWhiteSpace(licencia) && LicenciaConAtribucion().IsMatch(licencia.Trim());

    /// <summary>El texto legal de la licencia en español (<c>deed.es</c>), o null si no es una CC BY.</summary>
    public static string? UrlDeLicencia(string? licencia)
    {
        if (string.IsNullOrWhiteSpace(licencia)) { return null; }
        var m = LicenciaConAtribucion().Match(licencia.Trim());
        if (!m.Success) { return null; }
        var tipo = "by" + m.Groups["variante"].Value.ToLowerInvariant();
        return $"https://creativecommons.org/licenses/{tipo}/{m.Groups["version"].Value}/deed.es";
    }

    /// <summary>Autor, título (si lo hay) y origen de un crédito guardado como <c>Foto: Autor · «Título» · URL</c>.</summary>
    public static (string Autor, string? Titulo, string? Origen) Leer(string? credito)
    {
        var partes = (credito ?? string.Empty).Split('·', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var autor = partes.Length > 0 ? Regex.Replace(partes[0], @"^Foto:\s*", string.Empty, RegexOptions.IgnoreCase) : string.Empty;
        var origen = partes.LastOrDefault(p => p.StartsWith("http", StringComparison.OrdinalIgnoreCase));
        var titulo = partes.Skip(1).FirstOrDefault(p => !p.StartsWith("http", StringComparison.OrdinalIgnoreCase))?.Trim('«', '»', ' ');
        return (autor, string.IsNullOrWhiteSpace(titulo) ? null : titulo, origen);
    }

    /// <summary>
    /// Lo que se le hizo a la obra (CC BY pide decirlo): las partes del crédito entre el título y el origen, como en
    /// <c>Foto: Autor · «Título» · recortada · URL</c>.
    /// </summary>
    public static IReadOnlyList<string> Cambios(string? credito)
    {
        var partes = (credito ?? string.Empty).Split('·', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return partes.Skip(1).Where(p => !p.StartsWith("http", StringComparison.OrdinalIgnoreCase)).Skip(1).ToList();
    }

    /// <summary>Lo justo para que un texto no rompa el HTML; las tildes se quedan como se leen en el editor.</summary>
    private static string Escapar(string texto) =>
        texto.Replace("&", "&amp;", StringComparison.Ordinal)
             .Replace("<", "&lt;", StringComparison.Ordinal)
             .Replace(">", "&gt;", StringComparison.Ordinal)
             .Replace("\"", "&quot;", StringComparison.Ordinal);

    /// <summary>
    /// La lista en HTML: qué muestra la foto, quién la hizo, la obra enlazada a su origen y la licencia enlazada a su
    /// texto. Sólo las que piden atribución; sin ninguna, una línea que lo dice.
    /// </summary>
    public static string Html(IEnumerable<(string Alt, string Licencia, string Credito)> fotos)
    {
        // Una foto vive en varios medios (la tarjeta y la ficha del producto): se acredita una vez.
        var lista = fotos.Where(f => PideAtribucion(f.Licencia)).DistinctBy(f => (f.Credito.Trim(), f.Licencia.Trim())).ToList();
        if (lista.Count == 0)
        {
            return "<p>Hoy ninguna foto del sitio pide crédito.</p>";
        }
        var html = new StringBuilder("<ul>");
        foreach (var (alt, licencia, credito) in lista)
        {
            var (autor, titulo, origen) = Leer(credito);
            var obra = Escapar(titulo ?? "Foto original");
            html.Append("<li><strong>").Append(Escapar(alt)).Append("</strong>. ");
            html.Append(origen is null ? $"«{obra}»" : $"<a href=\"{Escapar(origen)}\" rel=\"nofollow noopener\">«{obra}»</a>");
            html.Append(", de ").Append(Escapar(autor));
            foreach (var cambio in Cambios(credito)) { html.Append(", ").Append(Escapar(cambio)); }
            html.Append(". Licencia ");
            html.Append($"<a href=\"{UrlDeLicencia(licencia)}\" rel=\"license noopener\">{Escapar(licencia.Trim())}</a>");
            html.Append(".</li>");
        }
        return html.Append("</ul>").ToString();
    }
}
