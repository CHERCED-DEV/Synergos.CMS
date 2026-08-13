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
/// <para><b>Y la cifra de tests se acababa de desviar otra vez</b>, en la ola que cableó el
/// carrito de viaje: la suite pasó de 2492 a 2512 y la prosa se quedó donde estaba, en los
/// cuatro sitios. Esa ola escribió el gate del carrito y olvidó el número — que es
/// exactamente el argumento de este ticket, ocurriendo mientras se escribía.</para>
///
/// <para><b>La cifra se queda en la prosa a propósito.</b> El valor de <c>CLAUDE.md</c> es
/// que se lee de corrido —«2512 tests, gates en verde» le dice a un agente el tamaño de la
/// red de seguridad en una línea— y sacarla a un fichero generado la haría cierta y nadie la
/// leería. El trato es el de #52: se queda escrita a mano y se le pone un gate detrás.</para>
///
/// <para><b>Por qué por reflexión y no contando <c>[Fact]</c> con un grep.</b> Un grep cuenta
/// 2217 <c>[Fact]</c> + 262 <c>[InlineData]</c> = 2479, y la suite reporta 2512: los 33 que
/// faltan son las filas de cuatro <c>[MemberData]</c>, que <b>no se pueden contar sin
/// ejecutar el miembro que las produce</b>. Un criterio que no da el número que reporta el
/// runner no es un criterio — es otro número a mano con más ceremonia.</para>
/// </remarks>
public sealed class SuiteCountTests
{
    /// <summary>
    /// Los sitios de <c>CLAUDE.md</c> donde la cifra aparece.
    /// </summary>
    /// <remarks>
    /// Se exigen <b>todos</b>, no «alguno»: el defecto de #52 fue exactamente que una de las
    /// dos frases se movió y la otra no. Un gate que compare un número suelto no ve eso.
    /// </remarks>
    private static readonly Regex Menciones = new(
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

    /// <summary>
    /// Lo que dice la prosa es lo que hay.
    /// </summary>
    [Fact]
    public void La_cifra_de_tests_de_CLAUDE_md_se_cuenta_contra_la_suite()
    {
        var cuantos = Cuantos();

        // Red de seguridad, y no es ceremonia: si el descubrimiento se rompe, esto contaría
        // CERO y el gate compararía la prosa contra cero — quedando verde en cuanto alguien
        // escribiera «0 tests». Es el mismo fallo silencioso que ya vigila el gate del molde.
        Assert.True(cuantos > 2000,
            $"Se descubrieron {cuantos} tests, que no puede ser: revisar este gate antes que la prosa.");

        var guia = File.ReadAllText(Path.Combine(RepoRoot(), "CLAUDE.md"));
        var menciones = Menciones.Matches(guia)
            .Select(m => (Texto: m.Value.Trim(), Numero: int.Parse(m.Groups[1].Value)))
            .ToList();

        Assert.True(menciones.Count >= 4,
            $"CLAUDE.md sólo menciona la cifra de tests {menciones.Count} vez/veces. Eran cuatro: "
            + "borrar una mención no es una forma válida de pasar este gate.");

        var desviadas = menciones.Where(m => m.Numero != cuantos).ToList();
        Assert.True(desviadas.Count == 0,
            $"La suite tiene {cuantos} tests y CLAUDE.md dice otra cosa en "
            + $"{desviadas.Count} sitio(s): {string.Join(" · ", desviadas.Select(d => d.Texto))}. "
            + "La cifra se cuenta, no se recuerda — quien la actualiza de memoria arrastra el "
            + "desfase anterior, y así llevaban 18 commits los endpoints (#52).");
    }
}
