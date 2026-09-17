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
}
