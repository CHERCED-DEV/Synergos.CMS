using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit.Sdk;

namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// Las cifras POR ÁREA que <c>CLAUDE.md</c> §2 declara sobre los tests, contadas contra la suite.
/// </summary>
/// <remarks>
/// <para><b>Qué añade sobre <c>SuiteCountTests</c>.</b> Aquél vigila el <b>total</b>. Esta línea
/// de §2 desglosa: «segregación (17) + molde (9) + capas (8) + imagen de contenedor (6) + compose
/// (10) + despliegue (14)», más «la compensación cruzada» del directorio <c>Bff/</c>. El total
/// puede cuadrar perfectamente con tres de esos siete desglosados equivocados, porque nada obliga
/// a que las partes sumen — de hecho no suman: la línea destaca seis ficheros de veintisiete, no
/// los inventaría.</para>
///
/// <para><b>Y estaban equivocados tres de siete</b> cuando se escribió esto: «la compensación
/// cruzada (48)» describía un directorio con <b>132</b>, «compose (8)» tenía 10 y «capas (10)»
/// tenía 8.</para>
///
/// <para><b>El 48 es el que vale la pena guardar: era EXACTO el día que se escribió.</b> En
/// <c>863317f</c>, <c>Bff/</c> tenía dos ficheros —<c>CompensationTests</c> (31) y
/// <c>PurchaseCompensationTests</c> (17)—. 31 + 17 = 48. Nadie se equivocó al escribirlo; se
/// equivocó al no volver, siete ficheros después. «capas (10)», en cambio, <b>nunca</b> fue
/// verdad: el día que entró ya eran 4 + 4.</para>
///
/// <para><b>Se cuenta por reflexión, con la misma regla que <c>SuiteCountTests</c>, y no es un
/// detalle.</b> La primera versión de este gate contaba <c>[Fact]</c> y <c>[InlineData]</c>
/// leyendo el fichero, y esa regla <b>no da el número que reporta el runner</b>: las filas de un
/// <c>[MemberData]</c> no se conocen sin ejecutar el miembro que las produce. Dos gates del mismo
/// repo con dos reglas de conteo distintas acaban pidiendo dos números distintos para la misma
/// cosa, y el más débil obliga a escribir en la guía una cifra que el otro considera falsa.</para>
///
/// <para>La cifra de endpoints vive en <c>ApiMoldTests</c> porque se cuenta desde las capacidades
/// y no desde los tests.</para>
/// </remarks>
public sealed class CifrasDeClaudeMdTests
{
    /// <summary>
    /// Cada cifra de la sección, con su frase exacta y de dónde sale.
    /// </summary>
    /// <remarks>
    /// La frase es el ancla: si alguien la reescribe, el gate se cae y le obliga a mirar el
    /// número — que es exactamente cuando conviene mirarlo. Las seis primeras son una clase de
    /// tests; la séptima es un directorio entero, y por eso se resuelve por namespace.
    /// </remarks>
    private static readonly (string Plantilla, string[] Clases, string? Namespace)[] Cifras =
    {
        ("segregación ({0})", new[] { "BackendSegregationTests" }, null),
        ("molde ({0})", new[] { "ApiMoldTests" }, null),
        ("capas ({0})", new[] { "LayerRuleTests", "ArchitectureTests" }, null),
        ("imagen de contenedor ({0})", new[] { "ContainerBuildTests" }, null),
        ("compose ({0})", new[] { "ComposeStackTests" }, null),
        ("despliegue ({0}, ADR 0133)", new[] { "DeployPipelineTests" }, null),
        ("la compensación cruzada ({0})", Array.Empty<string>(), "Synergos.CMS.Tests.Bff"),
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

    /// <summary>
    /// Los casos de prueba de un tipo, igual que los cuenta el runner.
    /// </summary>
    /// <remarks>
    /// <c>TheoryAttribute</c> hereda de <c>FactAttribute</c>, así que se distingue por el tipo y
    /// se le piden las filas a cada <c>DataAttribute</c>. Es la regla de <c>SuiteCountTests</c>,
    /// a propósito: dos reglas distintas para la misma cosa acaban pidiendo dos números.
    /// </remarks>
    private static int Casos(Type tipo)
    {
        var total = 0;

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

        return total;
    }

    /// <summary>
    /// Las tres cifras que <c>CLAUDE.md</c> §8 declara sobre el Layout Composer, contadas
    /// contra el schema uSync.
    /// </summary>
    /// <remarks>
    /// <para><b>Las dos primeras estaban desviadas</b> cuando se escribió esto, y por bastante:
    /// «los <b>156</b> element types tienen compDom*» cuando son 173, y «(<b>148</b> blocks)»
    /// cuando el Block Grid declara 159 de contenido. §8 se presenta como «el feature más maduro»,
    /// que es justo la sección que nadie vuelve a medir.</para>
    ///
    /// <para><b>Por qué acá y no en <c>tools/usync-audit.mjs</c>.</b> Aquel gate sólo corre
    /// cuando el PR toca <c>uSync/v9/**</c> o el propio script; un cambio que sólo toque
    /// <c>CLAUDE.md</c> no lo enciende. Y el desvío que se vigila vive **en CLAUDE.md**, así que
    /// tiene que colgar de un gate que corra siempre.</para>
    ///
    /// <para><b>Las tres se leen del schema, no de una lista</b>: los element types del disco y
    /// los bloques declarados por el propio <c>DTBlockGridSections</c>. La de 14 presets cuadra
    /// hoy y va igual: es la que separa «lo que se dropea al root» de «lo que va dentro de un
    /// area», o sea la frase entera de arriba.</para>
    /// </remarks>
    [Fact]
    public void Las_cifras_del_layout_composer_se_cuentan_contra_el_schema()
    {
        var raiz = RepoRoot();
        var tipos = Path.Combine(raiz, "Synergos.CMS.Web", "uSync", "v9", "ContentTypes");

        // Los element types que llevan los CUATRO compDom*, que es lo que la frase afirma.
        var conCompDom = Directory.EnumerateFiles(tipos, "*.config")
            .Select(File.ReadAllText)
            .Count(x => x.Contains("<IsElement>true</IsElement>", StringComparison.OrdinalIgnoreCase)
                        && new[] { "compDomClass", "compDomVariant", "compDomVisibility", "compDomAttributes" }
                            .All(p => x.Contains(p, StringComparison.OrdinalIgnoreCase)));

        // Los bloques que el Block Grid de secciones declara, partidos por dónde se pueden soltar.
        var grid = File.ReadAllText(Path.Combine(
            raiz, "Synergos.CMS.Web", "uSync", "v9", "DataTypes", "DTBlockGridSections.config"));

        var cdata = Regex.Match(grid, @"<Config><!\[CDATA\[(.*?)\]\]></Config>", RegexOptions.Singleline);
        Assert.True(cdata.Success, "No se pudo leer la configuración de DTBlockGridSections: revisar este gate.");

        using var json = JsonDocument.Parse(cdata.Groups[1].Value);
        var bloques = json.RootElement.GetProperty("Blocks").EnumerateArray().ToList();

        var alRoot = bloques.Count(b => b.TryGetProperty("allowAtRoot", out var v) && v.ValueKind == JsonValueKind.True);
        var enAreas = bloques.Count - alRoot;

        // Red de seguridad: un descubrimiento roto dejaría los asserts comparando contra cero.
        Assert.True(conCompDom > 100 && bloques.Count > 100,
            $"Se contaron {conCompDom} element types y {bloques.Count} bloques: el descubrimiento está roto.");

        var guia = Regex.Replace(File.ReadAllText(Path.Combine(raiz, "CLAUDE.md")), @"\s+", " ");

        foreach (var frase in new[]
                 {
                     $"los {conCompDom} element types tienen compDomClass",
                     $"de contenido ({enAreas} blocks) dentro de las areas",
                     $"**{alRoot} Layout Preset ElementTypes**",
                 })
        {
            Assert.True(guia.Contains(frase, StringComparison.Ordinal),
                $"CLAUDE.md §8 no dice «{frase}». En el schema hay {conCompDom} element types con los "
                + $"cuatro compDom*, y el Block Grid declara {bloques.Count} bloques ({alRoot} al root, "
                + $"{enAreas} dentro de areas). Estas cifras se cuentan, no se recuerdan: decían 156 y 148.");
        }
    }

    [Fact]
    public void Las_cifras_por_area_de_la_seccion_2_se_cuentan_contra_la_suite()
    {
        var tipos = typeof(CifrasDeClaudeMdTests).Assembly.GetTypes();
        var guia = File.ReadAllText(Path.Combine(RepoRoot(), "CLAUDE.md"));
        var malas = new List<string>();

        foreach (var (plantilla, clases, espacio) in Cifras)
        {
            var elegidos = espacio is null
                ? tipos.Where(t => clases.Contains(t.Name, StringComparer.Ordinal)).ToList()
                : tipos.Where(t => t.Namespace == espacio).ToList();

            // Sin esto, una clase renombrada dejaría el conteo en cero y el build se arreglaría
            // escribiendo «(0)» en la guía — el mismo fallo silencioso que ya vigilan los demás.
            Assert.True(elegidos.Count > 0,
                $"No se encontró ninguna clase para «{plantilla}» "
                + $"({(espacio ?? string.Join(" + ", clases))}). Si se renombró, mové también su "
                + "cifra en CLAUDE.md §2 — y este gate.");

            var cuantos = elegidos.Sum(Casos);
            var frase = string.Format(System.Globalization.CultureInfo.InvariantCulture, plantilla, cuantos);

            if (!guia.Contains(frase, StringComparison.Ordinal))
            {
                malas.Add($"§2 no dice «{frase}» — contados en la suite: {cuantos}");
            }
        }

        Assert.True(malas.Count == 0,
            string.Join(Environment.NewLine, malas)
            + Environment.NewLine
            + "Estas cifras se cuentan, no se recuerdan (#52). «la compensación cruzada (48)» era "
            + "exacta el día que se escribió y siguió ahí siete ficheros después.");
    }
}
