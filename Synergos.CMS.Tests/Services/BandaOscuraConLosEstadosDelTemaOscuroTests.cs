using System.Text.RegularExpressions;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Los avisos de un texto sobre la banda oscura (<c>.syn-preset--theme-dark</c>) usan los colores de estado del tema
/// oscuro (012-blogs-social, vuelta de valor).
/// </summary>
/// <remarks>
/// <para><b>El defecto.</b> La banda aclaraba los tokens de texto, pero no los de estado: en los temas claros, el aviso
/// «Regla de oro» del artículo «Arquitectura por capas» quedaba con su texto oscuro sobre el fondo oscuro (contraste de
/// 1,3 a 2,5). <c>syn-layout.css</c> espeja ahora los doce tokens de estado del bloque <c>[data-theme="dark"]</c> de
/// <c>syn-tokens.css</c>.</para>
/// <para>Un espejo se desvía en silencio cuando cambia el original: esto lo compara, con la extracción de
/// <see cref="AutoDarkEspejoDeDarkTests"/>.</para>
/// </remarks>
public sealed class BandaOscuraConLosEstadosDelTemaOscuroTests
{
    private const string SelectorDark = ":root[data-theme=\"dark\"]";
    private const string ReglaDeLaBanda = ".syn-preset--theme-dark .syn-callout";

    private static readonly string[] Estados = ["info", "success", "warning", "danger"];
    private static readonly string[] Partes = ["surface", "border", "text"];
    private static readonly TimeSpan Limite = TimeSpan.FromSeconds(5);

    [Fact]
    public void Los_avisos_de_la_banda_oscura_llevan_los_doce_colores_de_estado_del_tema_oscuro()
    {
        var dark = Declaraciones(Leer("syn-tokens.css"), selectores => selectores.Contains(SelectorDark, StringComparer.Ordinal));
        var banda = Declaraciones(Leer("syn-layout.css"), selectores => selectores.Any(s => s.StartsWith(ReglaDeLaBanda, StringComparison.Ordinal)));

        var esperadas = Estados.SelectMany(e => Partes.Select(p => $"--syn-color-state-{e}-{p}")).ToList();
        var faltan = esperadas.Where(t => !banda.ContainsKey(t)).ToList();
        Assert.True(faltan.Count == 0, $"La banda oscura no espeja {string.Join(", ", faltan)}: el aviso los toma del tema claro.");

        var desviadas = esperadas
            .Where(t => !dark.TryGetValue(t, out var valor) || valor != banda[t])
            .Select(t => $"{t}: tema oscuro «{(dark.TryGetValue(t, out var v) ? v : "—")}» / banda «{banda[t]}»")
            .ToList();
        Assert.True(desviadas.Count == 0, "La banda oscura se apartó del tema oscuro: " + string.Join(" · ", desviadas));
    }

    /// <summary>Las custom properties de los bloques cuyo selector cumple <paramref name="esElBloque"/>, sin comentarios.</summary>
    private static Dictionary<string, string> Declaraciones(string css, Func<IReadOnlyList<string>, bool> esElBloque)
    {
        var limpio = Regex.Replace(css, @"/\*[\s\S]*?\*/", string.Empty, RegexOptions.None, Limite);
        var declaraciones = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match bloque in Regex.Matches(limpio, @"([^{}]+)\{([^{}]*)\}", RegexOptions.None, Limite))
        {
            var selectores = bloque.Groups[1].Value.Split(',').Select(s => s.Trim()).ToList();
            if (!esElBloque(selectores))
            {
                continue;
            }

            foreach (var declaracion in bloque.Groups[2].Value.Split(';'))
            {
                var dosPuntos = declaracion.IndexOf(':', StringComparison.Ordinal);
                var nombre = dosPuntos < 0 ? string.Empty : declaracion[..dosPuntos].Trim();
                if (nombre.StartsWith("--", StringComparison.Ordinal))
                {
                    declaraciones[nombre] = Regex.Replace(declaracion[(dosPuntos + 1)..].Trim(), @"\s+", " ", RegexOptions.None, Limite);
                }
            }
        }

        return declaraciones;
    }

    private static string Leer(string hoja)
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && dir is not null; i++)
        {
            var candidato = Path.Combine(dir, "Synergos.CMS.Web", "wwwroot", "css", hoja);
            if (File.Exists(candidato))
            {
                return File.ReadAllText(candidato);
            }

            dir = Path.GetDirectoryName(dir);
        }

        throw new FileNotFoundException($"No se encontró {hoja} subiendo desde {AppContext.BaseDirectory}");
    }
}
