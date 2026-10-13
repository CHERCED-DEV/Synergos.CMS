using System.Text.RegularExpressions;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// La octava ruta de render —el modo oscuro del sistema sin <c>data-theme</c>— espeja el tema
/// oscuro ENTERO (ADR 0140, D4).
/// </summary>
/// <remarks>
/// <para><b>El defecto.</b> El bloque <c>@media (prefers-color-scheme: dark)</c> de
/// <c>syn-tokens.css</c> espejaba 40 de los 83 custom properties del bloque
/// <c>[data-theme="dark"]</c>. Lo que faltaba lo heredaba de <c>:root</c>, que es el tema CLARO:
/// el relleno de <c>action-primary</c> (brand-600) daba 2,39:1 sobre el lienzo oscuro, y toda la
/// familia <c>action-*</c>, <c>text-accent</c> y las sombras con él. Cada arreglo anterior espejó
/// la familia que alguien vio rota, nunca el bloque.</para>
///
/// <para><b>Por qué vive en el CMS.</b> La fuente es de este repo y el gate de contraste (G-5,
/// <c>audit-themes</c>) sólo corre en los PRs del UI: un PR del CMS que rompiera el espejo pasaba
/// todos sus gates. La paridad se vigila donde vive la fuente y el PR que la rompe; G-5 mide el
/// contraste.</para>
///
/// <para>La extracción es la de <c>tools/audit-themes.mjs</c> del UI: sin comentarios, bloques
/// <c>selector { cuerpo }</c> sin llaves dentro, y el valor con el espacio colapsado.</para>
/// </remarks>
public sealed class AutoDarkEspejoDeDarkTests
{
    private const string SelectorDark = ":root[data-theme=\"dark\"]";
    private const string SelectorAutoDark = ":root:not([data-theme])";

    /// <summary>
    /// Lo que auto-dark puede declarar distinto del tema oscuro, con su razón. VACÍO: hoy es un
    /// espejo exacto. Una entrada que deja de aplicar es rojo (ver el último test).
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> Excepciones =
        new Dictionary<string, string>(StringComparer.Ordinal);

    private static readonly TimeSpan Limite = TimeSpan.FromSeconds(5);

    [Fact]
    public void Todo_lo_que_declara_el_tema_oscuro_lo_declara_auto_dark()
    {
        var (dark, auto) = Bloques();

        var faltan = dark.Keys
            .Where(k => !auto.ContainsKey(k) && !Excepciones.ContainsKey(k))
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();

        Assert.True(faltan.Count == 0,
            $"auto-dark no declara {faltan.Count} custom propert(ies) del tema oscuro y las hereda "
            + "de :root, que es el tema CLARO: " + string.Join(", ", faltan));
    }

    [Fact]
    public void Auto_dark_no_declara_nada_que_el_tema_oscuro_no_declare_ni_con_otro_valor()
    {
        var (dark, auto) = Bloques();

        var desviadas = auto
            .Where(p => !Excepciones.ContainsKey(p.Key)
                        && (!dark.TryGetValue(p.Key, out var valor) || valor != p.Value))
            .Select(p => dark.TryGetValue(p.Key, out var valor)
                ? $"{p.Key}: dark «{valor}» / auto-dark «{p.Value}»"
                : $"{p.Key}: sólo en auto-dark")
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        Assert.True(desviadas.Count == 0,
            "auto-dark se aparta del tema oscuro: " + string.Join(" · ", desviadas));
    }

    [Fact]
    public void Cada_excepcion_del_censo_sigue_aplicando()
    {
        var (dark, auto) = Bloques();

        var sobran = Excepciones.Keys
            .Where(k => dark.TryGetValue(k, out var d) && auto.TryGetValue(k, out var a) && d == a)
            .Concat(Excepciones.Keys.Where(k => !dark.ContainsKey(k) && !auto.ContainsKey(k)))
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();

        Assert.True(sobran.Count == 0,
            "Excepciones que ya no aplican (auto-dark espeja ese token, o ninguno de los dos lo "
            + "declara): " + string.Join(", ", sobran) + ". Una excepción vieja tapa la próxima rotura.");
    }

    [Fact]
    public void El_extractor_ve_los_dos_bloques()
    {
        var (dark, auto) = Bloques();

        Assert.True(dark.Count >= 80,
            $"El bloque {SelectorDark} sólo tiene {dark.Count} custom properties: el extractor dejó "
            + "de verlo, y un espejo de nada da verde vacío.");
        Assert.True(auto.Count > 0, $"No se encontró el bloque {SelectorAutoDark}.");
    }

    private static (IReadOnlyDictionary<string, string> Dark, IReadOnlyDictionary<string, string> Auto) Bloques()
    {
        var css = Regex.Replace(LeerTokens(), @"/\*[\s\S]*?\*/", string.Empty, RegexOptions.None, Limite);
        var dark = new Dictionary<string, string>(StringComparer.Ordinal);
        var auto = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (Match bloque in Regex.Matches(css, @"([^{}]+)\{([^{}]*)\}", RegexOptions.None, Limite))
        {
            var selectores = bloque.Groups[1].Value.Split(',').Select(s => s.Trim()).ToList();
            var destino = selectores.Contains(SelectorDark, StringComparer.Ordinal) ? dark
                : selectores.Contains(SelectorAutoDark, StringComparer.Ordinal) ? auto
                : null;
            if (destino is null)
            {
                continue;
            }

            foreach (var declaracion in bloque.Groups[2].Value.Split(';'))
            {
                var dosPuntos = declaracion.IndexOf(':', StringComparison.Ordinal);
                if (dosPuntos < 0)
                {
                    continue;
                }

                var nombre = declaracion[..dosPuntos].Trim();
                if (nombre.StartsWith("--", StringComparison.Ordinal))
                {
                    destino[nombre] = Regex.Replace(declaracion[(dosPuntos + 1)..].Trim(), @"\s+", " ", RegexOptions.None, Limite);
                }
            }
        }

        return (dark, auto);
    }

    private static string LeerTokens()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && dir is not null; i++)
        {
            var candidato = Path.Combine(dir, "Synergos.CMS.Web", "wwwroot", "css", "syn-tokens.css");
            if (File.Exists(candidato))
            {
                return File.ReadAllText(candidato);
            }

            dir = Path.GetDirectoryName(dir);
        }

        throw new FileNotFoundException("No se encontró syn-tokens.css subiendo desde " + AppContext.BaseDirectory);
    }
}
