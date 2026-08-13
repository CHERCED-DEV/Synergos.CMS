using System.Text.RegularExpressions;

namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// Las cifras que <c>CLAUDE.md</c> §2 declara sobre los PROPIOS tests, contadas contra el disco.
/// </summary>
/// <remarks>
/// <para><b>Por qué existe</b> (#52). La cifra de endpoints llevaba dieciocho commits equivocada
/// por exactamente 2, y al ponerle gate apareció que no era la única: en la misma sección, «la
/// compensación cruzada (48)» describía un directorio que hoy tiene <b>132</b> tests. Y lo
/// instructivo es que <b>el 48 era exacto el día que se escribió</b> —31 de
/// <c>CompensationTests</c> más 17 de <c>PurchaseCompensationTests</c>, los dos únicos ficheros
/// que había—. Nadie se equivocó al escribirlo: se equivocó al no volver, siete ficheros
/// después.</para>
///
/// <para><b>Qué se gatea y qué no.</b> Se gatea toda cifra de esa sección que se pueda contar
/// leyendo ficheros. <b>El total de la suite NO</b>, y a propósito: sólo se sabe corriendo
/// <c>dotnet test</c>, así que un gate tendría que reimplementar el descubrimiento de xUnit para
/// contestar peor. Esa cifra la corrige quien lee el resumen de CI, y este comentario existe para
/// que la próxima persona sepa que la omisión es una decisión y no un olvido.</para>
///
/// <para><b>La regla de conteo, escrita para poder discutirla:</b> un test es un <c>[Fact]</c> o
/// un <c>[InlineData]</c>. Si un fichero gateado trae un <c>[Theory]</c> cuyos casos no están en
/// atributos —<c>[MemberData]</c>, <c>[ClassData]</c>— <b>el gate falla diciéndolo</b> en vez de
/// devolver un número de menos. Un contador que se equivoca en silencio es peor que ninguno:
/// obligaría a escribir en <c>CLAUDE.md</c> una cifra falsa para poner el build en verde.</para>
///
/// <para>La cifra de endpoints vive en <c>ApiMoldTests</c> y no aquí, porque se cuenta desde las
/// capacidades y no desde los tests.</para>
/// </remarks>
public sealed class CifrasDeClaudeMdTests
{
    /// <summary>Cada cifra de la sección, con la frase exacta y los ficheros que la sustentan.</summary>
    /// <remarks>
    /// El texto es el ancla: si alguien reescribe la frase, el gate se cae y le obliga a mirar
    /// el número — que es exactamente cuando conviene mirarlo.
    /// </remarks>
    private static readonly (string Plantilla, string[] Rutas)[] Cifras =
    {
        ("segregación ({0})", new[] { "Architecture/BackendSegregationTests.cs" }),
        ("molde ({0})", new[] { "Architecture/ApiMoldTests.cs" }),
        ("capas ({0})", new[] { "Architecture/LayerRuleTests.cs", "Architecture/ArchitectureTests.cs" }),
        ("imagen de contenedor ({0})", new[] { "Architecture/ContainerBuildTests.cs" }),
        ("compose ({0})", new[] { "Architecture/ComposeStackTests.cs" }),
        ("despliegue ({0}, ADR 0133)", new[] { "Architecture/DeployPipelineTests.cs" }),
        ("la compensación cruzada ({0})", new[] { "Bff" }),
    };

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

    /// <summary>Los <c>.cs</c> de una ruta, sea fichero o directorio.</summary>
    private static IEnumerable<string> Ficheros(string relativa)
    {
        var absoluta = Path.Combine(RepoRoot(), "Synergos.CMS.Tests", relativa.Replace('/', Path.DirectorySeparatorChar));

        if (File.Exists(absoluta))
        {
            return new[] { absoluta };
        }

        Assert.True(Directory.Exists(absoluta),
            $"«{relativa}» no existe. Si el fichero se renombró, mové también su cifra en CLAUDE.md §2.");
        return Directory.EnumerateFiles(absoluta, "*.cs", SearchOption.AllDirectories);
    }

    /// <summary>Cuenta los tests de un fichero, o grita si no puede contarlos.</summary>
    private static int Tests(string fichero)
    {
        var codigo = string.Join('\n', File.ReadLines(fichero)
            .Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));

        var facts = Regex.Matches(codigo, @"^\s*\[Fact\]", RegexOptions.Multiline).Count;
        var theories = Regex.Matches(codigo, @"^\s*\[Theory\]", RegexOptions.Multiline).Count;
        var inline = Regex.Matches(codigo, @"^\s*\[InlineData", RegexOptions.Multiline).Count;

        Assert.True(theories == 0 || inline > 0,
            $"{Path.GetFileName(fichero)} tiene un [Theory] sin [InlineData]: sus casos se resuelven "
            + "en tiempo de ejecución y este gate no sabe contarlos. Ampliá la regla de conteo — no "
            + "ajustes la cifra de CLAUDE.md para que cuadre, porque quedaría mintiendo.");

        return facts + inline;
    }

    [Fact]
    public void Las_cifras_de_la_seccion_2_se_cuentan_contra_el_disco()
    {
        var claude = File.ReadAllText(Path.Combine(RepoRoot(), "CLAUDE.md"));
        var malas = new List<string>();

        foreach (var (plantilla, rutas) in Cifras)
        {
            var cuantos = rutas.SelectMany(Ficheros).Sum(Tests);

            // Sin esto, una ruta que dejara de encontrar ficheros pediría «(0)» y el build se
            // arreglaría escribiendo un cero en la guía.
            Assert.True(cuantos > 0,
                $"Se contaron 0 tests para «{plantilla}»: el descubrimiento está roto.");

            var frase = string.Format(System.Globalization.CultureInfo.InvariantCulture, plantilla, cuantos);
            if (!claude.Contains(frase, StringComparison.Ordinal))
            {
                malas.Add($"CLAUDE.md §2 no dice «{frase}» — contados en disco: {cuantos} "
                          + $"({string.Join(" + ", rutas)})");
            }
        }

        Assert.True(malas.Count == 0,
            string.Join(Environment.NewLine, malas)
            + Environment.NewLine
            + "Estas cifras se cuentan, no se recuerdan (#52). Si añadiste tests, movelas en §2.");
    }
}
