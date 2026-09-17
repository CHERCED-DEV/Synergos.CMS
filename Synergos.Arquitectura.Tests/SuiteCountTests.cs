using System.Reflection;
using System.Text.RegularExpressions;
using Xunit.Sdk;

namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// La cifra de tests de <c>CLAUDE.md</c> se CUENTA, no se recuerda (#52).
/// </summary>
/// <remarks>
/// <para><b>Es el mismo defecto que el mapa del cableado (#50) y que los endpoints
/// (#52).</b> Una cifra escrita a mano en prosa se arrastra: quien la actualiza copia el
/// desfase anterior en vez de contar, y a la tercera ola nadie sabe cuál era la buena. Los
/// endpoints llevaban <b>18 commits</b> equivocados por exactamente dos.</para>
///
/// <para><b>La cifra se queda en la prosa a propósito.</b> El valor de <c>CLAUDE.md</c> es
/// que se lee de corrido —«3259 tests, gates en verde» le dice a un agente el tamaño de la
/// red de seguridad en una línea— y sacarla a un fichero generado la haría cierta y nadie la
/// leería. El trato es el de #52: se queda escrita a mano y se le pone un gate detrás.</para>
///
/// <para><b>Por qué por reflexión y no contando <c>[Fact]</c> con un grep.</b> Un grep no
/// puede contar las filas de un <c>[MemberData]</c> sin ejecutar el miembro que las produce,
/// y un criterio que no da el número que reporta el runner no es un criterio — es otro número
/// a mano con más ceremonia.</para>
///
/// <para><b>Y desde el #135 hay TRES suites, así que este fichero vive UNA vez y se enlaza en
/// las tres.</b> Un <c>Assembly.GetTypes()</c> sólo ve lo suyo, así que la alternativa era
/// copiarlo tres veces — tres sitios que se desvían por separado, que es exactamente el
/// defecto que este gate existe para cerrar. Cada copia enlazada mide SU ensamblado contra SU
/// fila, y además cuadra el TOTAL contra la suma de las tres filas: así ninguna cifra de la
/// tabla queda sin comprobar, y las tres se comprueban desde tres sitios distintos.</para>
/// </remarks>
public sealed class SuiteCountTests
{
    /// <summary>
    /// Una fila de la tabla de suites: <c>| `Synergos.X.Tests` | 1234 |</c>.
    /// </summary>
    /// <remarks>
    /// El <c>^\s*</c> no es defensa de más: la tabla vive DENTRO del punto 9 de §0.A, o sea
    /// indentada tres espacios. Con <c>^\|</c> a secas no casaba ninguna fila y el gate se
    /// caía diciendo «la tabla tiene 0 filas» — un mensaje que apunta a la guía cuando el
    /// defecto estaba acá.
    /// </remarks>
    private static readonly Regex Fila = new(
        @"^[ \t]*\|\s*`(Synergos\.[A-Za-z.]*Tests)`\s*\|\s*(\d{2,5})\s*\|",
        RegexOptions.Multiline | RegexOptions.Compiled);

    /// <summary>
    /// Las menciones del TOTAL en la prosa que se lee de corrido.
    /// </summary>
    /// <remarks>
    /// Se exigen <b>todas</b>, no «alguna»: el defecto de #52 fue exactamente que una de las
    /// dos frases se movió y la otra no. Un gate que compare un número suelto no ve eso.
    /// </remarks>
    private static readonly Regex Total = new(
        @"(\d{3,5})\s*(?:tests?\b|passing\b)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

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

    /// <summary>
    /// Cuenta los casos de prueba de ESTE ensamblado, igual que los cuenta el runner.
    /// </summary>
    /// <remarks>
    /// Un <c>[Theory]</c> vale por sus filas, no por uno: <c>TheoryAttribute</c> hereda de
    /// <c>FactAttribute</c>, así que se distingue por el tipo y se le piden los datos a cada
    /// <c>DataAttribute</c> — que es lo único que sabe cuántas filas produce un
    /// <c>[MemberData]</c>.
    /// </remarks>
    private static int Cuantos()
    {
        var total = 0;

        foreach (var tipo in typeof(SuiteCountTests).Assembly.GetTypes())
        {
            foreach (var metodo in tipo.GetMethods(
                         BindingFlags.Public | BindingFlags.NonPublic
                         | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                var teoria = metodo.GetCustomAttribute<TheoryAttribute>();
                if (teoria is null)
                {
                    if (metodo.GetCustomAttribute<FactAttribute>() is not null) total++;
                    continue;
                }

                foreach (var datos in metodo.GetCustomAttributes<DataAttribute>())
                {
                    total += datos.GetData(metodo).Count();
                }
            }
        }

        return total;
    }

    private static IReadOnlyDictionary<string, int> TablaDeSuites(string guia)
        => Fila.Matches(guia).ToDictionary(
            m => m.Groups[1].Value,
            m => int.Parse(m.Groups[2].Value),
            StringComparer.Ordinal);

    /// <summary>
    /// Lo que la tabla de <c>CLAUDE.md</c> dice de ESTA suite es lo que hay.
    /// </summary>
    [Fact]
    public void La_fila_de_esta_suite_en_CLAUDE_md_se_cuenta_contra_el_ensamblado()
    {
        var mia = typeof(SuiteCountTests).Assembly.GetName().Name!;
        var cuantos = Cuantos();

        // Red de seguridad, y no es ceremonia: si el descubrimiento se rompe, esto contaría
        // CERO y el gate compararía la prosa contra cero — quedando verde en cuanto alguien
        // escribiera «0». Es el mismo fallo silencioso que ya vigila el gate del molde.
        Assert.True(cuantos > 50,
            $"Se descubrieron {cuantos} tests en {mia}, que no puede ser: "
            + "revisar este gate antes que la prosa.");

        var tabla = TablaDeSuites(File.ReadAllText(Path.Combine(RepoRoot(), "CLAUDE.md")));

        Assert.True(tabla.ContainsKey(mia),
            $"CLAUDE.md no tiene fila para `{mia}`. Las que tiene: "
            + $"{string.Join(", ", tabla.Keys)}. Borrar una fila no es una forma válida de "
            + "pasar este gate — es cómo se queda una suite sin nadie que cuadre su cifra.");

        Assert.True(tabla[mia] == cuantos,
            $"`{mia}` tiene {cuantos} tests y CLAUDE.md dice {tabla[mia]}. La cifra se cuenta, "
            + "no se recuerda — quien la actualiza de memoria arrastra el desfase anterior, y "
            + "así llevaban 18 commits los endpoints (#52).");
    }

    /// <summary>
    /// Y el TOTAL de la prosa es la suma de las tres filas.
    /// </summary>
    /// <remarks>
    /// Sin esto, el total sería la única cifra del fichero que nadie mide: cada suite cuadraría
    /// la suya y las cuatro frases que dicen «N tests» podrían decir cualquier cosa. Se
    /// comprueba desde las TRES suites —el fichero está enlazado— así que tocar una fila y
    /// olvidar el total pone rojas las tres, no una.
    /// </remarks>
    [Fact]
    public void El_total_de_la_prosa_es_la_suma_de_las_suites()
    {
        var guia = File.ReadAllText(Path.Combine(RepoRoot(), "CLAUDE.md"));
        var tabla = TablaDeSuites(guia);

        Assert.True(tabla.Count == 3,
            $"La tabla de suites de CLAUDE.md tiene {tabla.Count} fila(s) y hay tres proyectos "
            + "de test. Una suite sin fila es una cifra que nadie cuadra.");

        var suma = tabla.Values.Sum();

        // Las filas de la propia tabla llevan cifras y NO son menciones del total: se descartan
        // por su forma (`| `Synergos…` | N |`), no por su valor, porque dos suites pueden sumar
        // lo mismo que una tercera y un filtro por valor las perdería en silencio.
        var menciones = Total.Matches(Fila.Replace(guia, string.Empty))
            .Select(m => (Texto: m.Value.Trim(), Numero: int.Parse(m.Groups[1].Value)))
            .ToList();

        // TRES y no cuatro desde el #135, y el motivo está escrito para que nadie lo lea como
        // una rebaja: la cuarta mención vivía en el árbol de §2 («xUnit — 3259 tests passing»)
        // y ahí ya no cabe un número, porque el nodo pasó a ser TRES proyectos. Lo que la
        // reemplaza es la TABLA, que se comprueba mejor que una frase — fila por fila, contra
        // el ensamblado, por reflexión. Bajar este umbral sin mover una mención a un sitio más
        // comprobado sí sería una rebaja.
        Assert.True(menciones.Count >= 3,
            $"CLAUDE.md sólo menciona el total {menciones.Count} vez/veces. Eran tres: "
            + "borrar una mención no es una forma válida de pasar este gate.");

        var desviadas = menciones.Where(m => m.Numero != suma).ToList();
        Assert.True(desviadas.Count == 0,
            $"Las tres suites suman {suma} y CLAUDE.md dice otra cosa en {desviadas.Count} "
            + $"sitio(s): {string.Join(" · ", desviadas.Select(d => d.Texto))}.");
    }
}
