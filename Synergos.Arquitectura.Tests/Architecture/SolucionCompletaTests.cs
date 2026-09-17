using System.Text.RegularExpressions;

namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// Todo proyecto que existe en disco está en la solución, y todo el que la solución lista existe
/// (#133).
/// </summary>
/// <remarks>
/// <para><b>Qué pasó.</b> La <c>.sln</c> listaba <b>23</b> proyectos y en disco había <b>32</b>.
/// Los nueve que faltaban —<c>Consent</c>, <c>Engagement</c>, <c>Fulfillment</c>, <c>Geo</c>,
/// <c>Inventory</c>, <c>Messaging</c>, <c>Moderation</c>, <c>Signing</c> y <c>Workflow</c>— son
/// capacidades que <c>Synergos.CMS.Tests</c> <b>sí referencia</b>, así que abrir la solución en
/// Visual Studio daba <b>nueve errores NU1105</b>, uno por cada una.</para>
///
/// <para><b>Y por qué no lo vio nadie: el comando con el que se verifica es el que lo tapa.</b>
/// <c>dotnet build</c> resuelve un <c>ProjectReference</c> <b>por ruta</b>, no por pertenencia a la
/// solución, así que MSBuild los compilaba transitivamente y <c>dotnet test Synergos.CMS.sln</c>
/// daba 3.257 en verde con los nueve fuera. <c>CLAUDE.md</c> §7 manda correr exactamente eso.</para>
///
/// <para>Es la forma de #126 y #92 —la herramienta con la que se desarrolla esconde lo que la de
/// al lado ve— con los papeles cambiados: aquí el que ve es el IDE y el que tapa es la CLI. Y
/// crecía sola, porque las capacidades entraron de a una a lo largo de muchas HU y la <c>.sln</c>
/// se actualizó las primeras once veces.</para>
///
/// <para><b>Se cruza en los DOS sentidos y las dos listas salen del disco.</b> Un proyecto que
/// existe y la solución no lista es el defecto de arriba; uno que la solución lista y no existe es
/// el mismo con el signo cambiado —una entrada muerta que rompe la carga de la solución— y una
/// lista escrita a mano acá sería la tercera copia de algo que ya está en dos sitios.</para>
/// </remarks>
public sealed class SolucionCompletaTests
{
    private static string RutaDelRepo([System.Runtime.CompilerServices.CallerFilePath] string f = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(f)!, "..", ".."));

    private static string Solucion() => Path.Combine(RutaDelRepo(), "Synergos.CMS.sln");

    /// <summary>Los <c>.csproj</c> que la solución declara, por nombre de fichero.</summary>
    private static IReadOnlyCollection<string> EnLaSolucion()
        => Regex.Matches(File.ReadAllText(Solucion()), @"""([^""]+\.csproj)""")
            .Select(m => Path.GetFileName(m.Groups[1].Value.Replace('\\', Path.DirectorySeparatorChar)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Los <c>.csproj</c> que existen de verdad. Se excluyen <c>bin/</c> y <c>obj/</c>: ahí MSBuild
    /// deja copias de los proyectos al restaurar, y contarlas haría que el gate midiera el estado
    /// del build en vez del del árbol.
    /// </summary>
    private static IReadOnlyCollection<string> EnDisco()
        => Directory.EnumerateFiles(RutaDelRepo(), "*.csproj", SearchOption.AllDirectories)
            .Where(p => !p.Split(Path.DirectorySeparatorChar).Any(
                s => s.Equals("bin", StringComparison.OrdinalIgnoreCase)
                  || s.Equals("obj", StringComparison.OrdinalIgnoreCase)))
            .Select(Path.GetFileName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase)!;

    [Fact]
    public void Todo_proyecto_del_disco_esta_en_la_solucion()
    {
        var disco = EnDisco();
        var sln = EnLaSolucion();

        // Red de seguridad: si uno de los dos descubrimientos deja de ver, el cruce pasaría en
        // verde sin mirar nada — el hueco que #118 dejó escrito para el cruce de DocTypes.
        Assert.True(disco.Count > 20, $"sólo se encontraron {disco.Count} .csproj en disco");
        Assert.True(sln.Count > 20, $"la solución sólo declara {sln.Count} proyectos");

        var faltan = disco.Except(sln, StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();

        Assert.True(faltan.Count == 0,
            "Estos proyectos existen y la solución NO los lista:\n  " + string.Join("\n  ", faltan)
            + "\n\nDesde la CLI no se nota —`dotnet build` sigue el ProjectReference por RUTA y los "
            + "compila igual— pero Visual Studio da un NU1105 por cada uno (#133). Se añaden con "
            + "`dotnet sln Synergos.CMS.sln add <ruta>`.");
    }

    [Fact]
    public void Todo_proyecto_de_la_solucion_existe_en_disco()
    {
        var sobran = EnLaSolucion().Except(EnDisco(), StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x).ToList();

        Assert.True(sobran.Count == 0,
            "La solución lista proyectos que no existen: " + string.Join(", ", sobran)
            + ". Una entrada muerta rompe la carga de la solución en el IDE — es el defecto de "
            + "arriba con el signo cambiado. Se quitan con `dotnet sln Synergos.CMS.sln remove`.");
    }

    // ── Y desde el #135 hay CUATRO soluciones ────────────────────────────────────────────────

    /// <summary>
    /// Las cuatro soluciones del repo, derivadas del disco.
    /// </summary>
    /// <remarks>
    /// <b>Derivadas y no enumeradas</b>: una lista a mano acá sería el defecto de #133 otra vez,
    /// un nivel más arriba — alguien añade <c>Synergos.Academy.sln</c> y este gate no la mira.
    /// </remarks>
    private static IReadOnlyList<string> Soluciones()
        => Directory.EnumerateFiles(RutaDelRepo(), "*.sln", SearchOption.TopDirectoryOnly)
            .OrderBy(x => x, StringComparer.Ordinal).ToList();

    private static IReadOnlyCollection<string> ProyectosDe(string sln)
        => Regex.Matches(File.ReadAllText(sln), @"""([^""]+\.csproj)""")
            .Select(m => m.Groups[1].Value.Replace('\\', Path.DirectorySeparatorChar))
            .Select(r => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sln)!, r)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyCollection<string> ReferenciasDe(string csproj)
        => Regex.Matches(File.ReadAllText(csproj), @"ProjectReference\s+Include=""([^""]+)""")
            .Select(m => m.Groups[1].Value.Replace('\\', Path.DirectorySeparatorChar))
            .Select(r => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(csproj)!, r)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Toda solución es CERRADA bajo <c>ProjectReference</c>: si lista un proyecto, lista también
    /// aquello a lo que ese proyecto apunta.
    /// </summary>
    /// <remarks>
    /// <para><b>Es la regla de #133 generalizada, y hace falta justo ahora.</b> Aquel defecto fue
    /// una sola solución a la que le faltaban nueve referencias; con cuatro, la probabilidad de
    /// repetirlo se multiplica — y el síntoma vuelve a ser un NU1105 que sólo ve el IDE, porque
    /// <c>dotnet build</c> resuelve por RUTA y compila igual.</para>
    ///
    /// <para><b>Y es lo que decide qué puede vivir en una solución parcial.</b>
    /// <c>Synergos.Servicios.Tests</c> referencia capacidades Y orquestadores, así que no cabe ni
    /// en <c>Synergos.Apis.sln</c> ni en <c>Synergos.Bff.sln</c> sin arrastrar al otro grupo: vive
    /// en la integradora, y este gate es el que impide que alguien lo meta «para poder correr los
    /// tests desde ahí» y deje la solución rota para Visual Studio.</para>
    /// </remarks>
    [Fact]
    public void Toda_solucion_es_cerrada_bajo_sus_referencias()
    {
        var soluciones = Soluciones();

        // Red de seguridad: si el descubrimiento deja de ver, el cruce pasa en verde sin mirar.
        Assert.True(soluciones.Count >= 4,
            $"Se encontraron {soluciones.Count} soluciones y hay cuatro: revisar este gate.");

        var rotas = new List<string>();

        foreach (var sln in soluciones)
        {
            var listados = ProyectosDe(sln);
            Assert.True(listados.Count > 0, $"{Path.GetFileName(sln)} no declara ningún proyecto.");

            foreach (var proyecto in listados)
            {
                foreach (var referencia in ReferenciasDe(proyecto))
                {
                    if (!listados.Contains(referencia))
                    {
                        rotas.Add($"{Path.GetFileName(sln)}: {Path.GetFileNameWithoutExtension(proyecto)} "
                            + $"→ {Path.GetFileNameWithoutExtension(referencia)}");
                    }
                }
            }
        }

        Assert.True(rotas.Count == 0,
            "Estas soluciones listan un proyecto sin listar aquello a lo que apunta:\n  "
            + string.Join("\n  ", rotas.OrderBy(x => x, StringComparer.Ordinal))
            + "\n\nDesde la CLI no se nota —MSBuild sigue el ProjectReference por RUTA— y Visual "
            + "Studio da un NU1105 por cada uno. Es el #133 con cuatro soluciones en vez de una.");
    }

    /// <summary>
    /// La integradora es la ÚNICA que lo lista todo; las otras tres son estrictamente parciales.
    /// </summary>
    /// <remarks>
    /// Sin esto, la forma barata de pasar el gate de arriba sería meterlo todo en las cuatro — y
    /// entonces las tres parciales dejarían de servir para lo único que existen: abrir el producto
    /// web sin arrastrar el backend, y al revés. Se comprueba en los dos sentidos por eso.
    /// </remarks>
    [Fact]
    public void Las_tres_parciales_son_estrictamente_menores_que_la_integradora()
    {
        var todo = ProyectosDe(Solucion());

        foreach (var sln in Soluciones().Where(s => !s.Equals(Solucion(), StringComparison.OrdinalIgnoreCase)))
        {
            var suyos = ProyectosDe(sln);
            var nombre = Path.GetFileName(sln);

            var fuera = suyos.Except(todo, StringComparer.OrdinalIgnoreCase).ToList();
            Assert.True(fuera.Count == 0,
                $"{nombre} lista proyectos que la integradora no tiene: "
                + string.Join(", ", fuera.Select(Path.GetFileNameWithoutExtension))
                + ". Synergos.CMS.sln tiene que seguir siendo la que lo lanza todo junto.");

            Assert.True(suyos.Count < todo.Count,
                $"{nombre} lista los mismos {suyos.Count} proyectos que la integradora. Una "
                + "solución parcial que no es parcial no sirve para lo único que existe: abrir "
                + "un árbol sin arrastrar el otro.");
        }
    }
}
