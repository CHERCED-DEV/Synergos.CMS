using System.Text.RegularExpressions;

namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// Ningún gate construye la ruta de un proyecto: la pregunta por nombre (#136).
/// </summary>
/// <remarks>
/// <para><b>Qué pasó.</b> El backend estaba plano en la raíz, así que la ruta de un proyecto ERA
/// su nombre y los gates escribían <c>Path.Combine(RepoRoot(), "Synergos.Api.Booking",
/// "Domain")</c>. Al mover el backend a <c>backend/{nucleo,capacidades,orquestadores}/</c> eso
/// pasó a ser <b>treinta y ocho rutas equivocadas</b> de golpe. Se arreglaron todas y apareció
/// <see cref="Proyectos"/>, que resuelve un nombre a su directorio buscándolo en el disco.</para>
///
/// <para><b>Por qué hace falta un gate y no basta con haberlo arreglado.</b> Porque el arreglo
/// es invisible desde el sitio donde se comete el error: quien escriba mañana
/// <c>Path.Combine(RepoRoot(), "Synergos.Api.Nueva")</c> no ve nada raro, y <b>el gate que
/// escriba pasará en verde</b> el día que lo escriba —la ruta existe si la capacidad está en
/// <c>capacidades/</c>— y se pondrá rojo la próxima vez que alguien reorganice. O sea que el
/// coste no lo paga quien lo comete: lo paga quien mueve una carpeta dos olas después, con
/// treinta y ocho rojos sin relación aparente entre sí.</para>
///
/// <para><b>Y el modo de fallo peor no es el rojo, es el VERDE.</b>
/// <c>Directory.EnumerateDirectories(RepoRoot())</c> era cómo cinco gates descubrían los
/// servicios: enumerar la raíz y filtrar por prefijo. Tras el movimiento eso devuelve UNA
/// carpeta —<c>backend</c>— el filtro no casa con nada, y el gate <b>pasa sin mirar un solo
/// proyecto</b>. Cinco gates de arquitectura dando verde sobre el vacío es peor que no
/// tenerlos, y es exactamente lo que este repo ya tiene escrito tres veces (#118, #133, la red
/// de seguridad del molde).</para>
///
/// <para><b>Se parsea la fuente SIN comentarios</b>, porque los <c>&lt;remarks&gt;</c> de
/// <see cref="Proyectos"/> y de este mismo fichero citan las formas prohibidas para explicar
/// qué se prohíbe — y un gate que se engaña con su propia explicación es el defecto que
/// <c>feedback_a_gate_that_parses_source_needs_its_own_mutations</c> describe.</para>
/// </remarks>
public sealed class RutasDeProyectoTests
{
    /// <summary>
    /// Las tres suites. Se derivan del disco: una lista a mano acá caducaría con la cuarta.
    /// </summary>
    private static IReadOnlyList<string> Suites() => Proyectos.Nombres("Synergos.")
        .Where(n => n.EndsWith("Tests", StringComparison.Ordinal))
        .Select(n => Proyectos.Dir(n))
        .ToList();

    /// <summary>
    /// <c>Path.Combine(&lt;raíz&gt;, "Synergos.Api…")</c> y sus variantes.
    /// </summary>
    /// <remarks>
    /// Sólo las familias que se MOVIERON: <c>Synergos.CMS.*</c> sigue en la raíz, así que
    /// nombrarla ahí no es un defecto — prohibirlo sería un gate que pide un cambio que no
    /// arregla nada, y ésos se desactivan.
    /// </remarks>
    private static readonly Regex RutaLiteral = new(
        @"Path\.Combine\(\s*(?:RepoRoot\(\)|RutaDelRepo\(\)|RaizDelRepo\(\)|Proyectos\.Raiz\(\)|raiz|Raiz|root|repoRoot)\s*,\s*"
        + @"""Synergos\.(?:Api|Bff|Core|Shared)[A-Za-z.]*""",
        RegexOptions.Compiled);

    /// <summary>
    /// Enumerar la raíz para descubrir proyectos. Da CERO desde el #136.
    /// </summary>
    private static readonly Regex EnumeraLaRaiz = new(
        @"Directory\.(?:Enumerate|Get)Directories\(\s*(?:RepoRoot\(\)|RutaDelRepo\(\)|RaizDelRepo\(\)|Proyectos\.Raiz\(\)|raiz|Raiz|root|repoRoot)\s*[,)]",
        RegexOptions.Compiled);

    /// <summary>
    /// Un glob de proyectos contra un directorio: asume que están todos al mismo nivel.
    /// </summary>
    private static readonly Regex GlobDeProyectos = new(
        @"Directory\.(?:Enumerate|Get)Directories\([^)]*""Synergos\.(?:Api|Bff)\.\*""",
        RegexOptions.Compiled);

    /// <summary>La fuente sin comentarios de línea ni de bloque ni XML.</summary>
    private static string SinComentarios(string ruta)
    {
        var texto = File.ReadAllText(ruta);
        texto = Regex.Replace(texto, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
        return string.Join('\n', texto.Split('\n')
            .Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));
    }

    private static IReadOnlyList<(string Fichero, string Fuente)> Fuentes()
        => Suites()
            .SelectMany(d => Directory.EnumerateFiles(d, "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Split(Path.DirectorySeparatorChar).Any(
                s => s.Equals("obj", StringComparison.OrdinalIgnoreCase)
                  || s.Equals("bin", StringComparison.OrdinalIgnoreCase)))
            // `Proyectos.cs` ES el resolutor: su cuerpo tiene que poder recorrer el árbol.
            .Where(f => Path.GetFileName(f) != "Proyectos.cs")
            .Select(f => (f, SinComentarios(f)))
            .ToList();

    [Fact]
    public void Ningun_gate_construye_la_ruta_de_un_proyecto_del_backend()
    {
        var fuentes = Fuentes();

        // Red de seguridad: si el descubrimiento deja de ver, el gate pasa sin mirar nada — que
        // es literalmente el defecto que este gate existe para cazar, cometido por él mismo.
        Assert.True(fuentes.Count > 250,
            $"Sólo se leyeron {fuentes.Count} ficheros de test: revisar este gate antes que nada.");

        var malos = fuentes
            .Where(x => RutaLiteral.IsMatch(x.Fuente))
            .Select(x => Path.GetFileName(x.Fichero))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        Assert.True(malos.Count == 0,
            "Estos ficheros construyen la ruta de un proyecto del backend a mano:\n  "
            + string.Join("\n  ", malos)
            + "\n\nEl backend vive en backend/{nucleo,capacidades,orquestadores}/, así que su "
            + "ruta NO es su nombre. Se pregunta por nombre: `Proyectos.Dir(\"Synergos.Api.X\", "
            + "\"Domain\")`. Hoy funcionaría igual —la carpeta existe— y se rompería la próxima "
            + "vez que alguien reorganice, con el coste pagado por quien mueva y no por quien "
            + "lo escribió (#136).");
    }

    [Fact]
    public void Ningun_gate_descubre_proyectos_enumerando_la_raiz()
    {
        var fuentes = Fuentes();
        Assert.True(fuentes.Count > 250, $"Sólo se leyeron {fuentes.Count} ficheros de test.");

        var malos = fuentes
            .Where(x => EnumeraLaRaiz.IsMatch(x.Fuente) || GlobDeProyectos.IsMatch(x.Fuente))
            .Select(x => Path.GetFileName(x.Fichero))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        Assert.True(malos.Count == 0,
            "Estos ficheros descubren proyectos enumerando la raíz (o con un glob que los "
            + "supone todos al mismo nivel):\n  " + string.Join("\n  ", malos)
            + "\n\nDesde el #136 eso devuelve UNA carpeta —`backend`— y el filtro por prefijo no "
            + "casa con nada: el gate PASA EN VERDE sin mirar un solo proyecto, que es el peor de "
            + "los dos modos de fallo. Se usa `Proyectos.Directorios()` (todos) o "
            + "`Proyectos.Todos(\"Synergos.Api.\")` (una familia).");
    }
}
