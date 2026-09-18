using System.Text.RegularExpressions;

namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// Cada anotación nullable de una vista declara su contexto <b>justo encima</b>, porque el
/// compilador de runtime no se lo da — y sin él la página contesta 500 con el build en verde.
/// </summary>
/// <remarks>
/// <para><b>Qué pasó.</b> Las vistas se compilan <b>en caliente</b>
/// (<c>RazorCompileOnBuild=false</c>, obligado por <c>ModelsMode=InMemoryAuto</c>) y ese
/// compilador lee sus opciones del <c>deps.json</c>, donde viaja <c>warningsAsErrors: true</c>
/// desde el #134 — pero <b>no viaja <c>nullable</c></b>. Así que un <c>object?</c> da
/// <c>CS8669</c>, el trinquete lo asciende a error, y la página revienta. Medido el 2026-09-17:
/// el sitio público entero en 500.</para>
///
/// <para><b>Y la directiva es POR REGIÓN, no por fichero.</b> Ésa fue la segunda vuelta, y la
/// primera versión de este gate la erró: pedía que el fichero contuviera un
/// <c>#nullable enable</c> en alguna parte, y con eso se quedó <b>verde sobre
/// <c>ModerationComments.cshtml</c></b>, que lo tenía en la línea 9 y reventaba en la 44. Razor
/// cierra cada región de código con su propio <c>#nullable restore</c>, así que una directiva
/// sólo cubre el bloque donde está. Lo destapó <c>ssr-dom-check</c> pidiendo la página, no este
/// gate. Por eso ahora la regla es <b>línea a línea</b>: mecánica, sin parsear regiones de Razor,
/// y suficiente — la directiva cae siempre en la misma región que la sentencia de debajo.</para>
///
/// <para><b>Por qué no basta con el build.</b> <c>dotnet build</c> <i>sí</i> tiene contexto
/// nullable (<c>Directory.Build.props</c> pone <c>Nullable=enable</c>), así que ahí no se ve
/// jamás: las cuatro soluciones compilaban en 0 avisos y las tres suites en verde con la portada
/// caída.</para>
///
/// <para><b>Y no se arregla en un punto único.</b> Probado: un <c>#nullable enable</c> en
/// <c>_ViewImports.cshtml</c> se emite como <b>texto literal</b> y no llega al C# generado. Y el
/// <c>deps.json</c> no admite <c>nowarn</c> ni <c>nullable</c>: su único interruptor es
/// <c>warningsAsErrors</c>, que es todo o nada y vive en un fichero cuya cabecera exige ADR.</para>
/// </remarks>
public sealed class NullableEnVistasTests
{
    private const string Directiva = "#nullable enable";

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

    private static readonly Regex Comentario =
        new(@"@\*.*?\*@|/\*.*?\*/|//[^\n]*|<!--.*?-->", RegexOptions.Singleline | RegexOptions.Compiled);

    /// <summary>
    /// Las líneas de la vista con el CONTENIDO de los comentarios en blanco — conservando
    /// posiciones, para poder seguir hablando de números de línea reales.
    /// </summary>
    /// <remarks>
    /// Obligatorio, y no es teórico: las vistas y el propio <c>_ViewImports.cshtml</c>
    /// <b>nombran</b> <c>object?</c> en la prosa que explica este defecto. Un corte que no quite
    /// los comentarios se engaña con su propia explicación
    /// (<c>feedback_a_gate_that_parses_source_needs_its_own_mutations</c>).
    /// </remarks>
    private static string[] LineasSinComentarios(string texto)
        => Comentario
            .Replace(texto, m => Regex.Replace(m.Value, "[^\n]", " "))
            .Replace("\r\n", "\n")
            .Split('\n');

    private static readonly Regex Anotacion = new(
        @"\b(?:object|string|int|decimal|bool|double|long|Guid|DateTime|DateTimeOffset|" +
        @"IPublishedContent|IPublishedElement|IHtmlContent|JsonElement)\?(?![?.\w])",
        RegexOptions.Compiled);

    /// <summary>Las directivas de Razor, que se emiten FUERA del cuerpo: en la clase.</summary>
    private static readonly Regex DirectivaRazor = new(
        @"^\s*@(using|model|inherits|inject|addTagHelper|namespace|attribute|implements|section)\b",
        RegexOptions.Compiled);

    [Fact]
    public void Toda_anotacion_nullable_lleva_la_directiva_JUSTO_encima()
    {
        var huerfanas = new List<string>();

        foreach (var vista in Vistas())
        {
            var ls = LineasSinComentarios(File.ReadAllText(vista));

            for (var i = 0; i < ls.Length; i++)
            {
                if (!Anotacion.IsMatch(ls[i]) || DirectivaRazor.IsMatch(ls[i])) continue;

                var j = i - 1;
                while (j >= 0 && ls[j].Trim().Length == 0) j--;

                if (j >= 0 && ls[j].Trim() == Directiva) continue;

                huerfanas.Add($"{Path.GetRelativePath(RutaDelRepo(), vista)}:{i + 1}  {ls[i].Trim()}");
            }
        }

        Assert.True(huerfanas.Count == 0,
            $"Estas anotaciones nullable no llevan `{Directiva}` en la línea de encima. Razor cierra "
            + "cada región de código con su propio `#nullable restore`, así que una directiva puesta "
            + "en otro bloque NO las cubre: en caliente son CS8669 ascendido a error, la página "
            + "contesta 500 y el build sigue verde.\n  "
            + string.Join("\n  ", huerfanas));
    }

    [Fact]
    public void Ninguna_directiva_de_Razor_lleva_una_anotacion_nullable()
    {
        // Éstas no las salva NINGUNA directiva escrita en el cuerpo: Razor emite `@model`,
        // `@inherits` y compañía en la DECLARACIÓN DE LA CLASE, antes de todo lo demás. Se
        // encontraron cuatro —tres `@inherits` y un `@model` de tupla— y ninguna la vio el
        // barrido: las vio compilar las vistas con el contexto nullable apagado.
        var culpables = new List<string>();

        foreach (var vista in Vistas())
        {
            var ls = LineasSinComentarios(File.ReadAllText(vista));

            for (var i = 0; i < ls.Length; i++)
            {
                if (DirectivaRazor.IsMatch(ls[i]) && Anotacion.IsMatch(ls[i]))
                {
                    culpables.Add($"{Path.GetRelativePath(RutaDelRepo(), vista)}:{i + 1}  {ls[i].Trim()}");
                }
            }
        }

        Assert.True(culpables.Count == 0,
            "Una directiva de Razor con anotación nullable revienta la vista en caliente y NINGÚN "
            + $"`{Directiva}` escrito dentro llega a tiempo. Quitá el `?` del argumento de tipo: no "
            + "se pierde nada, las anotaciones se borran en ejecución y estas vistas no se compilan "
            + "en el build.\n  "
            + string.Join("\n  ", culpables));
    }
}
