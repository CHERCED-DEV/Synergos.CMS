using System.Text.RegularExpressions;

namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// Los comandos que <c>CLAUDE.md</c> manda teclear nombran ficheros que EXISTEN (#152).
/// </summary>
/// <remarks>
/// <para><b>Lo encontró teclear uno, no leerlo.</b> §7 lleva
/// <c>dotnet test Synergos.Servicios.Tests/Synergos.Servicios.Tests.csproj</c> desde que el
/// corte del #135 hizo útil correr una suite sola — y el #136 movió ese proyecto a
/// <c>backend/</c>. Desde entonces el comando contesta
/// <c>MSBUILD : error MSB1009: Project file does not exist</c>, o sea que la guía manda a
/// alguien a un sitio que no está y <b>nada se pone rojo</b>.</para>
///
/// <para><b>Es <c>feedback_a_path_is_not_a_name_and_a_flat_tree_hides_it</c> con un sujeto que
/// aquel ticket no barrió.</b> El #136 midió 38 rutas equivocadas en los gates, arregló el
/// Dockerfile, el generador del compose, la matriz de imágenes y el ensayo de restauración, y
/// cerró la puerta con <c>RutasDeProyectoTests</c> — que vigila la FUENTE de los gates. La guía
/// no es fuente de ningún gate, así que se quedó fuera: el commit que inventó la regla la
/// incumplió en el fichero que la enuncia.</para>
///
/// <para><b>Y el modo de fallo es el caro de los dos</b>, porque no lo paga quien escribe sino
/// quien llega. Un agente en un clon limpio teclea lo que §7 dice, recibe un error de MSBuild que
/// habla de un fichero y no de una reorganización, y concluye lo que el error sugiere: que la
/// suite no existe. Es el mismo daño que la línea que decía «el CMS habla con UNA capacidad»
/// durante once HU — la guía no se queda corta, <b>manda al sitio equivocado</b>.</para>
///
/// <para><b>Por qué es un trinquete absoluto y no una línea base</b>: medido al escribirlo,
/// <b>15 de 16</b> rutas ya existían, así que exigirlas todas cuesta una línea. Es el criterio
/// del #134 —un umbral absoluto sólo vale cuando el árbol YA lo cumple— y el del #140 al revés:
/// allá la deuda era 71,4 % y tocaba línea base.</para>
///
/// <para><b>Lo que esto NO comprueba, dicho para no mentir sobre su alcance</b>: que el comando
/// haga lo que la guía dice. Un <c>dotnet test</c> contra el proyecto correcto con el filtro
/// equivocado pasa por aquí. Lo único que se afirma es que el fichero que nombra está en el
/// disco, que es exactamente el defecto que se midió.</para>
/// </remarks>
public sealed class ComandosDeClaudeMdTests
{
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

    [Fact]
    public void Toda_ruta_que_la_guia_manda_ejecutar_existe_en_el_disco()
    {
        var raiz = RepoRoot();
        var guia = File.ReadAllText(Path.Combine(raiz, "CLAUDE.md"));

        // El primer argumento de un `dotnet test|build|run` o de un `node`. Se toma sólo el
        // argumento posicional: una bandera (`--filter`, `-v`) no es una ruta, y una variable de
        // shell (`$CMS`, `${...}`) no se puede resolver desde acá — nombrarlas sería pedir que la
        // guía deje de usarlas, que es un cambio que no arregla nada (#136).
        var rutas = Regex.Matches(
                guia,
                @"^(?:dotnet (?:test|build|run)|node) +(?<ruta>[^\s$-][^\s]*)",
                RegexOptions.Multiline,
                TimeSpan.FromSeconds(3))
            .Select(m => m.Groups["ruta"].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        // Red de seguridad. Si el extractor deja de ver —una valla de bloque que cambia, un
        // prefijo nuevo— la lista sale vacía y el gate pasa en verde sin mirar nada, que es el
        // modo de fallo que el #136 midió sobre `ComposeStackTests`: 12/12 sobre una lista vacía.
        Assert.True(
            rutas.Count >= 12,
            $"Sólo se extrajeron {rutas.Count} rutas de los comandos de CLAUDE.md y son 16. El " +
            "extractor está roto: sin esto el gate se pone verde sobre el vacío.");

        var muertas = rutas
            .Where(r => !File.Exists(Path.Combine(raiz, r)) && !Directory.Exists(Path.Combine(raiz, r)))
            .ToList();

        Assert.True(
            muertas.Count == 0,
            "Estas rutas las manda teclear CLAUDE.md y no existen:\n" +
            string.Join('\n', muertas.Select(r => $"  {r}")) +
            "\n\nQuien llegue nuevo teclea eso, recibe un error que habla de un fichero y no de " +
            "una reorganización, y concluye que la suite no existe. Si el proyecto se movió, la " +
            "guía se mueve en el MISMO commit — es la regla del #136 aplicada al fichero que la " +
            "enuncia.");
    }
}
