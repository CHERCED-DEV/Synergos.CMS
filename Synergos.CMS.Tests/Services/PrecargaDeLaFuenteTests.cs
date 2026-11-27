using System.Text.RegularExpressions;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// La precarga de Manrope (D-012-V14b, 012-blogs-social): cada cabecera que carga la hoja de Manrope de Google Fonts pide
/// también, desde el primer byte del HTML, el .woff2 latino que esa hoja usa (uno solo, variable, para todos los pesos).
/// Así la fuente llega antes del primer pintado y el texto no cambia de líneas al llegar.
/// </summary>
/// <remarks>
/// Sin <c>crossorigin</c>, el navegador no usa la precarga (las fuentes se piden siempre en modo CORS) y baja la fuente
/// dos veces. Cuando Google cambie la versión de la URL, la precarga deja de servir sin romper nada: se cambia en todas
/// las cabeceras a la vez, por eso las cinco deben llevar la misma.
/// </remarks>
public sealed class PrecargaDeLaFuenteTests
{
    private const string HojaDeManrope = "https://fonts.googleapis.com/css2?family=Manrope";

    private static readonly Regex Precarga = new(
        "<link rel=\"preload\" href=\"(?<href>https://fonts\\.gstatic\\.com/s/manrope/v\\d+/[A-Za-z0-9_-]+\\.woff2)\" as=\"font\" type=\"font/woff2\" crossorigin />",
        RegexOptions.CultureInvariant);

    [Fact]
    public void Cada_cabecera_que_carga_la_hoja_de_Manrope_precarga_el_mismo_woff2_antes_de_la_hoja()
    {
        var vistas = Directory.GetFiles(RepoDir("Synergos.CMS.Web", "Views"), "*.cshtml", SearchOption.AllDirectories)
            .Select(ruta => (ruta, texto: File.ReadAllText(ruta)))
            .Where(v => v.texto.Contains(HojaDeManrope, StringComparison.Ordinal))
            .ToList();

        Assert.True(vistas.Count >= 5, $"Se esperaban las cinco cabeceras que cargan Manrope; hay {vistas.Count}.");
        var hrefs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (ruta, texto) in vistas)
        {
            var precargas = Precarga.Matches(texto);
            Assert.True(precargas.Count == 1, $"{Path.GetFileName(ruta)}: {precargas.Count} precargas del .woff2 de Manrope, se esperaba una.");
            Assert.True(
                precargas[0].Index < texto.IndexOf(HojaDeManrope, StringComparison.Ordinal),
                $"{Path.GetFileName(ruta)}: la precarga va antes de la hoja, para pedir la fuente desde el primer byte.");
            hrefs.Add(precargas[0].Groups["href"].Value);
        }

        Assert.Single(hrefs);
    }

    private static string RepoDir(params string[] parts)
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && dir is not null; i++)
        {
            var candidate = Path.Combine(new[] { dir }.Concat(parts).ToArray());
            if (Directory.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }

        throw new DirectoryNotFoundException($"No se encontró {string.Join('/', parts)} subiendo desde {AppContext.BaseDirectory}");
    }
}
