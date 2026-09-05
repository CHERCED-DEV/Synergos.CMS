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

    /// <summary>
    /// Cada cifra que §2 y §4.1 dicen del SCHEMA, con su frase exacta y qué se cuenta.
    /// </summary>
    /// <remarks>
    /// La frase es el ancla, igual que arriba: reescribirla rompe el gate y obliga a mirar el
    /// número, que es justo cuando conviene mirarlo.
    /// </remarks>
    private static readonly (string Plantilla, string Carpeta, string Patron)[] Schema =
    {
        ("Compositions ({0} archivos)", "Synergos.CMS.Web/uSync/v9/ContentTypes", "*.config"),
        ("{0} archivos (", "Synergos.CMS.Web/uSync/v9/DataTypes", "*.config"),
        ("({0} DTSelect*)", "Synergos.CMS.Web/uSync/v9/DataTypes", "DTSelect*.config"),
        ("en-US ({0} keys)", "Synergos.CMS.Web/uSync/v9/Dictionary", "*.config"),
        ("Razor template registry ({0})", "Synergos.CMS.Web/uSync/v9/Templates", "*.config"),
        ("LAS {0} CAPACIDADES", ".", "Synergos.Api.*"),
    };

    /// <summary>
    /// Las cifras del SCHEMA que <c>CLAUDE.md</c> declara, contadas contra el disco.
    /// </summary>
    /// <remarks>
    /// <para><b>El defecto que evita, y por qué se escribe estando en verde.</b> Las ocho cifras
    /// que §2 y §4.1 dan del schema —los ficheros de cada carpeta de <c>uSync/v9/</c>, los iconos
    /// del stock, los ADR, las capacidades— <b>cuadraban todas</b> el día que se escribió este
    /// fact. No se arregla nada acá: se les pone el vigilante que no tenían.</para>
    ///
    /// <para><b>«Cuadra hoy» no es un estado, es una foto.</b> Lo acaba de demostrar la cifra de
    /// códigos de rechazo: era exacta, tenía gate, y aun así §11 acabó diciendo 234 en un sitio y
    /// 235 en otro porque el gate miraba sólo uno de los dos. Y antes que ésa, «la compensación
    /// cruzada (48)» fue exacta el día que se escribió y siguió ahí siete ficheros después. Una
    /// cifra a mano sin gate no se degrada despacio: se queda quieta mientras el repo se mueve.
    /// </para>
    ///
    /// <para><b>Los ADR se cuentan además contra su propio enunciado.</b> La línea no dice sólo
    /// cuántos hay: dice «0001-0133, sin 0016», que es una afirmación comprobable —el mayor es el
    /// 133, el 16 no está, y por tanto son 132—. Un rango que no cuadra con el conteo es la señal
    /// de que se añadió un ADR y se tocó una de las dos mitades.</para>
    /// </remarks>
    [Fact]
    public void Las_cifras_del_schema_de_CLAUDE_md_se_cuentan_contra_el_disco()
    {
        var raiz = RepoRoot();
        var guia = File.ReadAllText(Path.Combine(raiz, "CLAUDE.md"));
        var malas = new List<string>();

        foreach (var (plantilla, carpeta, patron) in Schema)
        {
            var dir = Path.Combine(raiz, carpeta.Replace('/', Path.DirectorySeparatorChar));

            var cuantos = patron.EndsWith(".config", StringComparison.Ordinal)
                ? Directory.EnumerateFiles(dir, patron).Count()
                : Directory.EnumerateDirectories(dir, patron).Count();

            // Sin esto, una carpeta movida dejaría el conteo en cero y el build se arreglaría
            // escribiendo «(0)» en la guía.
            Assert.True(cuantos > 0,
                $"No se encontró nada para «{plantilla}» en {carpeta} ({patron}). Si se movió, "
                + "mové también su cifra en CLAUDE.md §2 — y este gate.");

            var frase = string.Format(System.Globalization.CultureInfo.InvariantCulture, plantilla, cuantos);

            if (!guia.Contains(frase, StringComparison.Ordinal))
            {
                malas.Add($"CLAUDE.md no dice «{frase}» — en {carpeta} hay {cuantos} ({patron})");
            }
        }

        var iconos = File.ReadAllLines(Path.Combine(raiz, "tools", "umbraco13-icons-stock.txt"))
            .Count(l => l.Length > 0);

        Assert.True(iconos > 0, "El fichero de iconos del stock está vacío.");

        if (!guia.Contains($"({iconos} iconos, versionado en el repo)", StringComparison.Ordinal))
        {
            malas.Add($"CLAUDE.md §4.1 no dice «({iconos} iconos, versionado en el repo)» — "
                + $"tools/umbraco13-icons-stock.txt tiene {iconos}. Es la lista contra la que §4.1 "
                + "manda verificar un icono antes de escribirlo: si la cifra miente, quien la lea "
                + "creerá que verificó contra otra cosa.");
        }

        var adrs = Directory.EnumerateFiles(Path.Combine(raiz, "Synergos.CMS.Web", "docs", "adr"), "*.md")
            .Select(f => Regex.Match(Path.GetFileName(f), @"^(\d{4})-"))
            .Where(m => m.Success)
            .Select(m => int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture))
            .ToHashSet();

        Assert.True(adrs.Count > 0, "No se encontró ningún ADR con nombre NNNN-*.md.");

        var faltan = Enumerable.Range(1, adrs.Max()).Where(n => !adrs.Contains(n)).ToList();

        // El enunciado de la línea —«0001-0133, sin 0016»— es tan comprobable como la cifra, y
        // las dos mitades se tocan por separado: se puede añadir un ADR y arreglar sólo una.
        var hueco = faltan.Count == 1
            ? $"sin {faltan[0]:0000}"
            : string.Join(" y ", faltan.Select(n => $"{n:0000}"));

        var linea = $"{adrs.Count} ADRs (0001-{adrs.Max():0000}, {hueco})";

        if (!guia.Contains(linea, StringComparison.Ordinal))
        {
            malas.Add($"CLAUDE.md §2 no dice «{linea}» — en docs/adr/ hay {adrs.Count} ficheros, "
                + $"el mayor es {adrs.Max():0000} y falta{(faltan.Count == 1 ? "" : "n")} "
                + $"{string.Join(", ", faltan.Select(n => $"{n:0000}"))}.");
        }

        Assert.True(malas.Count == 0,
            string.Join(Environment.NewLine, malas)
            + Environment.NewLine
            + "Estas cifras se cuentan, no se recuerdan (#52). Cuadraban todas el día que se "
            + "escribió este gate: lo que se les añadió no fue una corrección, fue el vigilante.");
    }

    /// <summary>
    /// La cuenta de elementos del CDN aparece <b>en un solo sitio</b> del repo.
    /// </summary>
    /// <remarks>
    /// <para><b>El defecto que evita</b> (#86). Esa cifra estaba copiada en seis sitios y decía
    /// tres cosas distintas — una en la guía, el doc de despliegue y dos comentarios de código;
    /// otra en un tercer comentario del <b>mismo fichero</b> que uno de aquéllos; y una tercera en
    /// el catálogo de una skill. Ninguna coincidía con lo que el CDN servía.</para>
    ///
    /// <para><b>Y es la única cifra de la guía que NO se puede cruzar contra el disco</b>, porque
    /// depende de la red y del repo hermano — que CI no clona. Por eso fue la que se desvió: los
    /// demás gates de cifras cuentan contra algo que está acá. Éste no puede comprobar que sea
    /// correcta; comprueba <b>que no esté copiada</b>, que es la forma exacta en que se rompió.
    /// Una cifra que no se puede verificar y vive en seis ficheros se desvía en seis ficheros.</para>
    ///
    /// <para><b>Por eso los comentarios de código la perdieron y no la corrigieron.</b> Uno
    /// explicaba que todas las entradas del CDN sirven <c>main.js</c>; el otro, que recorrer el
    /// catálogo entero dentro de <c>/_health</c> saldría caro. En los dos, lo que sostiene el
    /// argumento es <b>todas</b> y <b>entero</b>, no cuántas sean. Un número que no hace falta es
    /// un número que sólo puede envejecer.</para>
    ///
    /// <para><b>Qué mira y qué no, y no es una lista de exenciones.</b> Mira lo que describe el
    /// estado <b>de hoy</b>: la guía, el doc de despliegue y los comentarios de código. Deja fuera
    /// los ADR y los inventarios fechados, que son <b>historia</b>: un ADR que dice cuántos
    /// elementos había el día de esa decisión no está desviado, está fechado, y reescribirlo sería
    /// falsificarlo. La frontera es «¿esto afirma el presente?», no una lista de rutas.</para>
    ///
    /// <para>Fuera queda también el catálogo de <c>synergos-architect</c>, que se declara
    /// <c>AUTO-GENERATED</c>: a mano no se toca. Y ahí hay un hallazgo aparte, en #86 — su
    /// generador vive en el repo hermano y escribe a <c>&lt;padre&gt;/.claude/skills/…</c>, no al
    /// que este repo versiona, así que en la disposición que §7 prescribe <b>no puede
    /// refrescarlo</b>. Un fichero rotulado auto-generado que nadie puede regenerar se lee como
    /// fresco y no lo está.</para>
    /// </remarks>
    [Fact]
    public void La_cuenta_de_elementos_del_CDN_vive_en_un_solo_sitio()
    {
        var raiz = RepoRoot();

        // Una cifra pegada a la palabra que la nombra. No se busca el número suelto: «130» sale
        // en cualquier parte y buscarlo así daría falsos positivos sin fin.
        var patron = new Regex(@"\b(\d{2,4})\s+(?:elementos|entradas|bundles)\b", RegexOptions.IgnoreCase);

        var mirados = new[]
        {
            "CLAUDE.md",
            Path.Combine("docs", "despliegue"),
            Path.Combine("Synergos.CMS.Web", "Services"),
        };
        var hallazgos = new List<string>();

        foreach (var entrada in mirados)
        {
            var ruta = Path.Combine(raiz, entrada);

            var ficheros = File.Exists(ruta)
                ? new[] { ruta }
                : Directory.EnumerateFiles(ruta, "*.*", SearchOption.AllDirectories)
                    .Where(f => f.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                             || f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                    .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                             && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                    .ToArray();

            foreach (var fichero in ficheros)
            {
                foreach (var linea in File.ReadAllLines(fichero))
                {
                    // Sólo cuenta si la línea habla del CDN o del registry: «14 elementos» de
                    // otra cosa no tiene nada que ver.
                    if (!linea.Contains("CDN", StringComparison.OrdinalIgnoreCase)
                        && !linea.Contains("registry", StringComparison.OrdinalIgnoreCase)) continue;

                    var m = patron.Match(linea);
                    if (!m.Success) continue;

                    hallazgos.Add($"{Path.GetRelativePath(raiz, fichero)}: «{m.Value.Trim()}»");
                }
            }
        }

        // Sin esto el fact pasaría en verde vigilando el vacío: si el patrón dejara de encontrar
        // nada —porque alguien reescribe la frase de §11 de otra forma— «cero copias» se cumpliría
        // solo, y la próxima copia entraría sin que nadie la viera.
        Assert.True(hallazgos.Count >= 1,
            "No se encontró NINGUNA mención de la cuenta de elementos del CDN. Tiene que haber "
            + "exactamente una, en CLAUDE.md §11 y con el comando para medirla al lado: si se "
            + "reescribió de otra forma, este gate dejó de vigilar (#86).");

        // La única que puede llevarla es la de §11, que trae el comando para medirla al lado.
        var conComando = hallazgos.Count(h => h.StartsWith("CLAUDE.md", StringComparison.Ordinal));

        Assert.True(hallazgos.Count == conComando && conComando == 1,
            "La cuenta de elementos del CDN volvió a estar copiada, y es la única cifra de este "
            + "repo que no se puede cruzar contra el disco —depende de la red y del repo hermano—, "
            + "así que copiarla es garantizar que se desvíe. Llegó a decir 139 en cuatro sitios, "
            + "130 en uno y 122 en cinco más, con el CDN sirviendo 130 (#86). Va en CLAUDE.md §11 "
            + "y en ningún otro lado, con el comando para medirla al lado."
            + Environment.NewLine
            + string.Join(Environment.NewLine, hallazgos));
    }
}
