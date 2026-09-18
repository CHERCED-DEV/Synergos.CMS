using System.Text.RegularExpressions;

namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// Una vista que usa anotaciones nullable declara su contexto, porque el compilador de runtime no
/// se lo da — y sin él la página contesta 500 con el build entero en verde.
/// </summary>
/// <remarks>
/// <para><b>Qué pasó.</b> Las vistas se compilan <b>en caliente</b>
/// (<c>RazorCompileOnBuild=false</c>, obligado por <c>ModelsMode=InMemoryAuto</c>) y ese
/// compilador lee sus opciones del <c>deps.json</c>, donde viaja <c>warningsAsErrors: true</c>
/// desde el #134 — pero <b>no viaja <c>nullable</c></b>. Así que un <c>object?</c> en una vista da
/// <c>CS8669</c> («anotación fuera de contexto nullable»), el trinquete lo asciende a error, y la
/// página revienta. Medido el 2026-09-17: <b>104 vistas</b> afectadas y el sitio público entero en
/// 500.</para>
///
/// <para><b>Por qué no basta con el build.</b> <c>dotnet build</c> <i>sí</i> tiene contexto
/// nullable (<c>Directory.Build.props</c> pone <c>Nullable=enable</c>), así que ahí no se ve
/// jamás: las cuatro soluciones compilaban en 0 avisos y las tres suites daban 3272 en verde con
/// la portada caída. Es la misma forma que <c>ImportMapEmissionTests</c> y que el <c>@using</c>
/// perdido del #119 — lo único que existe antes de que arranque el sitio es la fuente.</para>
///
/// <para><b>Por qué no se arregla en <c>_ViewImports.cshtml</c>.</b> Probado: un
/// <c>#nullable enable</c> ahí se emite como <b>texto literal</b> y no llega al C# generado. Y el
/// <c>deps.json</c> no admite <c>nowarn</c> ni <c>nullable</c>: su único interruptor es
/// <c>warningsAsErrors</c>, que es todo o nada y vive en un fichero cuya cabecera exige ADR para
/// tocarlo. Por eso la directiva va por vista y este gate existe para que la 105.ª no se olvide.
/// </para>
/// </remarks>
public sealed class NullableEnVistasTests
{
    private static string RutaDelRepo([System.Runtime.CompilerServices.CallerFilePath] string f = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(f)!, "..", ".."));

    /// <summary>Todas las vistas, descubiertas del disco. Nunca una lista escrita a mano.</summary>
    private static IReadOnlyList<string> Vistas()
    {
        var raiz = Path.Combine(RutaDelRepo(), "Synergos.CMS.Web", "Views");
        Assert.True(Directory.Exists(raiz), $"No existe {raiz}: el descubrimiento mira donde no es.");

        var todas = Directory.GetFiles(raiz, "*.cshtml", SearchOption.AllDirectories);

        // Red de seguridad. Un descubrimiento roto dejaría este gate en verde sobre CERO vistas,
        // que es el modo de fallo que este repo tiene medido (`ComposeStackTests` dio 12/12 sobre
        // una lista vacía, #136). Un gate que no puede fallar es peor que no tener gate.
        Assert.True(todas.Length > 50,
            $"Sólo se descubrieron {todas.Length} vistas bajo {raiz}. Había más de 200 cuando se " +
            "escribió esto: si el árbol se movió, este gate está pasando sin mirar nada.");

        return todas;
    }

    /// <summary>La fuente SIN comentarios: se juzga lo que ejecuta, no lo que se explica.</summary>
    /// <remarks>
    /// Obligatorio acá, y no es teórico: el <c>_ViewImports.cshtml</c> y las 104 vistas
    /// <b>nombran</b> <c>object?</c> en la prosa que cuenta este mismo defecto. Un corte que no
    /// quite los comentarios se engaña con su propia explicación
    /// (<c>feedback_a_gate_that_parses_source_needs_its_own_mutations</c>).
    /// </remarks>
    private static string SinComentarios(string texto)
    {
        texto = Regex.Replace(texto, @"@\*.*?\*@", " ", RegexOptions.Singleline);   // Razor
        texto = Regex.Replace(texto, @"/\*.*?\*/", " ", RegexOptions.Singleline);   // C# bloque
        texto = Regex.Replace(texto, @"//[^\n]*", " ");                              // C# línea
        texto = Regex.Replace(texto, @"<!--.*?-->", " ", RegexOptions.Singleline);   // HTML
        return texto;
    }

    private static readonly Regex Anotacion = new(
        @"\b(?:object|string|int|decimal|bool|double|long|Guid|DateTime|DateTimeOffset|" +
        @"IPublishedContent|IPublishedElement|IHtmlContent|JsonElement)\?(?![?.\w])",
        RegexOptions.Compiled);

    /// <summary>Los bloques <c>@{ }</c> y <c>@functions { }</c>, con las llaves equilibradas.</summary>
    private static IEnumerable<string> BloquesDeCodigo(string texto)
    {
        var i = 0;
        while (true)
        {
            var m = Regex.Match(texto[i..], @"@(functions\s*)?\{");
            if (!m.Success) yield break;

            var ini = i + m.Index + m.Length - 1;
            int prof = 0, j = ini;
            for (; j < texto.Length; j++)
            {
                if (texto[j] == '{') prof++;
                else if (texto[j] == '}' && --prof == 0) break;
            }

            yield return texto[ini..Math.Min(j, texto.Length)];
            i = Math.Min(j + 1, texto.Length);
            if (i >= texto.Length) yield break;
        }
    }

    [Fact]
    public void Toda_vista_con_anotaciones_nullable_declara_su_contexto()
    {
        var sinDirectiva = new List<string>();

        foreach (var vista in Vistas())
        {
            var texto = File.ReadAllText(vista);
            var codigo = SinComentarios(texto);

            if (!BloquesDeCodigo(codigo).Any(b => Anotacion.IsMatch(b))) continue;
            if (texto.Contains("#nullable enable", StringComparison.Ordinal)) continue;

            sinDirectiva.Add(Path.GetRelativePath(RutaDelRepo(), vista));
        }

        Assert.True(sinDirectiva.Count == 0,
            "Estas vistas usan anotaciones nullable y no declaran `#nullable enable` dentro de su "
            + "primer bloque de código. En caliente eso es CS8669 ascendido a error: la página "
            + "contesta 500 y el build sigue verde.\n  "
            + string.Join("\n  ", sinDirectiva));
    }

    [Fact]
    public void Ningun_inherits_lleva_una_anotacion_nullable()
    {
        // Éstas no las salva la directiva, y por eso van aparte: Razor emite `@inherits` en la
        // DECLARACIÓN DE LA CLASE, antes del cuerpo donde vive el `#nullable enable`. La
        // anotación llega sin contexto igual. Se descubrió con las tres que el barrido de las 104
        // NO arregló, y sin este segundo hecho el gate habría quedado verde sobre ellas.
        var culpables = new List<string>();

        foreach (var vista in Vistas())
        {
            var codigo = SinComentarios(File.ReadAllText(vista));

            foreach (Match m in Regex.Matches(codigo, @"^\s*@inherits\s+.*$", RegexOptions.Multiline))
            {
                if (Anotacion.IsMatch(m.Value))
                {
                    culpables.Add($"{Path.GetRelativePath(RutaDelRepo(), vista)}: {m.Value.Trim()}");
                }
            }
        }

        Assert.True(culpables.Count == 0,
            "Un `@inherits` con anotación nullable revienta la vista en caliente y NINGÚN "
            + "`#nullable enable` escrito dentro llega a tiempo. Quitá el `?` del argumento de "
            + "tipo: no se pierde nada, las anotaciones se borran en ejecución y estas vistas no "
            + "se compilan en el build.\n  "
            + string.Join("\n  ", culpables));
    }
}
