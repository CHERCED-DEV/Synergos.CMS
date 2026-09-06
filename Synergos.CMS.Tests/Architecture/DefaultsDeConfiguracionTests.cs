using System.Text.RegularExpressions;

namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// El default que un setting DICE es el que tiene (defecto #95).
/// </summary>
/// <remarks>
/// <para><b>El <c>&lt;summary&gt;</c> de un POCO de configuración es la interfaz que lee quien
/// configura un despliegue</b>, y se edita por separado del inicializador. Veinte de los
/// veintiún <c>bool</c> de <c>Configuration/</c> nombran su default en la prosa; uno lo nombraba
/// al revés —«False por privacy/compliance default» con el default en <c>true</c>— y llevaba así
/// sin que nada lo cruzara.</para>
///
/// <para><b>Es la forma de las cifras de <c>CLAUDE.md</c> antes de <c>CifrasDeClaudeMdTests</c></b>:
/// un dato afirmado en prosa, con su fuente de verdad a dos líneas, y nada que los enfrente. Y
/// duele más acá, porque quien lee ese comentario lo está leyendo <b>para decidir si tiene que
/// configurar algo</b> — el de privacidad concluía que ya estaba apagado.</para>
///
/// <para><b>No exige que la prosa nombre el default.</b> Un setting puede no decirlo y estar bien;
/// lo que no puede es decirlo mal. El gate sólo mira los que sí lo afirman.</para>
/// </remarks>
public sealed class DefaultsDeConfiguracionTests
{
    private static readonly Regex Declaracion = new(
        @"(?<doc>(?:[ \t]*///[^\n]*\n)+)[ \t]*public bool (?<nombre>\w+) \{ get; init; \} = (?<valor>true|false);",
        RegexOptions.Compiled);

    [Fact]
    public void Lo_que_el_comentario_dice_del_default_es_lo_que_el_default_es()
    {
        var carpeta = Path.Combine(RepoRoot(), "Synergos.CMS.Application", "Configuration");
        var malas = new List<string>();
        var conClaim = 0;
        var total = 0;

        foreach (var fichero in Directory.EnumerateFiles(carpeta, "*.cs"))
        {
            foreach (Match m in Declaracion.Matches(File.ReadAllText(fichero)))
            {
                total++;
                var prosa = Limpia(m.Groups["doc"].Value);
                var afirmado = DefaultAfirmado(prosa);
                if (afirmado is null) continue;

                conClaim++;
                var real = m.Groups["valor"].Value;

                if (!string.Equals(afirmado, real, StringComparison.Ordinal))
                {
                    malas.Add($"{Path.GetFileName(fichero)} · {m.Groups["nombre"].Value}: "
                        + $"el comentario dice que el default es `{afirmado}` y es `{real}`."
                        + Environment.NewLine + $"      «{Recorta(prosa)}»");
                }
            }
        }

        // Sin estos dos, un cambio de formato dejaría el gate en verde vigilando cero — que es el
        // modo de fallo que ya costó una corrección en el gate del bridge (#89).
        Assert.True(total >= 20, $"Sólo se leyeron {total} bools de Configuration/: el descubrimiento está roto.");
        Assert.True(conClaim >= 15,
            $"Sólo {conClaim} de {total} bools afirman un default reconocible. Antes eran veinte: "
            + "o cambió cómo se redactan, o este gate dejó de entenderlos y ya no vigila nada.");

        Assert.True(malas.Count == 0,
            "Un setting dice en su comentario un default distinto del que tiene (#95). Ese "
            + "`<summary>` es la interfaz que lee quien configura un despliegue, y se edita por "
            + "separado del inicializador — el que estaba mal afirmaba una postura de PRIVACIDAD "
            + "contraria a la real, así que se leía como «ya está apagado»."
            + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", malas));
    }

    /// <summary>
    /// El default que la prosa afirma, o <c>null</c> si no afirma ninguno.
    /// </summary>
    /// <remarks>
    /// Las formas son las que hay escritas de verdad, no las que uno imaginaría: <c>Default true.</c>,
    /// <c>Si true (default false)</c>, <c>Si false (default),</c> y la que estaba mal,
    /// <c>False por … default</c>. El orden importa — <c>Si true (default false)</c> tiene los dos
    /// valores, y el que manda es el del paréntesis.
    /// </remarks>
    private static string? DefaultAfirmado(string prosa)
    {
        // 1) «(default false)» o «(default true)» — el paréntesis gana sobre el «Si X» de delante.
        var m = Regex.Match(prosa, @"\(\s*default\s+(true|false)\s*\)", RegexOptions.IgnoreCase);
        if (m.Success) return m.Groups[1].Value.ToLowerInvariant();

        // 2) «Si false (default),» — el valor va ANTES y el paréntesis sólo marca cuál es.
        m = Regex.Match(prosa, @"\b(?:Si|Cuando|When|If)\s+(true|false)\s*\(\s*default\s*\)", RegexOptions.IgnoreCase);
        if (m.Success) return m.Groups[1].Value.ToLowerInvariant();

        // 3) «Default true.» / «default false»
        m = Regex.Match(prosa, @"\bdefaults?\s+(?:es\s+|is\s+|a\s+)?(true|false)\b", RegexOptions.IgnoreCase);
        if (m.Success) return m.Groups[1].Value.ToLowerInvariant();

        // 4) «False por privacy/compliance default» — el valor delante y «default» detrás. Es la
        //    forma del defecto #95, y se reconoce a propósito: si el gate no la entendiera, el
        //    único caso que existía no lo habría cazado.
        m = Regex.Match(prosa, @"\b(true|false)\b[^.]{0,60}?\bdefault\b", RegexOptions.IgnoreCase);
        if (m.Success) return m.Groups[1].Value.ToLowerInvariant();

        return null;
    }

    private static string Limpia(string doc)
    {
        var texto = string.Join(' ', doc.Split('\n')
            .Select(l => l.Trim().TrimStart('/').Trim())
            .Where(l => l.Length > 0));
        return Regex.Replace(texto, "<[^>]+>", " ").Replace("  ", " ").Trim();
    }

    private static string Recorta(string s) => s.Length <= 130 ? s : s[..130] + "…";

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Synergos.CMS.sln")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
