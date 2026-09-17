using System.Text.Json;
using System.Text.RegularExpressions;

namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// Que las 22 imágenes se puedan construir con <b>un solo</b> <c>Dockerfile.service</c>, y que
/// la matriz que las enumera no se quede corta (HU #17).
/// </summary>
/// <remarks>
/// <para><b>Los tres supuestos que hace la imagen, y que acá se vigilan.</b> Construir 22
/// servicios con un fichero parametrizado sólo funciona mientras el molde se cumpla; el día que
/// uno se salga, la imagen se construye igual y <b>falla al arrancar</b> — que es el peor sitio
/// donde enterarse.</para>
///
/// <para><b>Por qué esto es un gate y no una comprobación en el workflow.</b> El workflow sólo
/// corre cuando alguien empuja algo que lo dispara; este test corre siempre, con la suite. Y el
/// fallo que previene —una capacidad nueva que simplemente <i>no existe</i> en producción— no
/// rompe nada: <b>falta</b>. Es más caro que romper.</para>
/// </remarks>
public sealed class ContainerBuildTests
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

    /// <summary>Los servicios que la matriz debería producir, calculados igual que el script.</summary>
    private static IReadOnlyList<string> Servicios()
        => Directory.EnumerateDirectories(RepoRoot())
            .Select(Path.GetFileName)
            .Where(n => n!.StartsWith("Synergos.Api.", StringComparison.Ordinal)
                     || n.StartsWith("Synergos.Bff.", StringComparison.Ordinal))
            .Where(n => File.Exists(Path.Combine(RepoRoot(), n!, "Program.cs")))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList()!;

    [Fact]
    public void El_gate_ve_los_servicios_que_existen()
    {
        // Sin esto, un descubrimiento roto dejaría TODOS los asserts de abajo en verde sobre una
        // lista vacía.
        var nombres = Servicios();

        Assert.Contains("Synergos.Api.Booking", nombres);
        Assert.Contains("Synergos.Bff.Tienda", nombres);
        Assert.True(nombres.Count >= 20, $"Se esperaban al menos 20 servicios y se vieron {nombres.Count}.");
    }

    [Fact]
    public void La_maquina_de_sagas_NO_se_construye_como_servicio()
    {
        // `Synergos.Bff.Core` empieza igual que los orquestadores y es una BIBLIOTECA. Excluirla
        // por nombre exigiría acordarse de la excepción; se excluye por «no tiene punto de
        // entrada», que es una propiedad que se mantiene sola.
        Assert.DoesNotContain("Synergos.Bff.Core", Servicios());
        Assert.False(File.Exists(Path.Combine(RepoRoot(), "Synergos.Bff.Core", "Program.cs")),
            "Si Bff.Core llegara a tener Program.cs, dejaría de ser una biblioteca y este gate " +
            "estaría mintiendo sobre por qué la excluye.");
    }

    [Fact]
    public void El_script_de_la_matriz_produce_EXACTAMENTE_los_servicios_que_hay()
    {
        // El workflow deriva la matriz de este script. Si el script y la realidad se separan, se
        // construyen imágenes de más (ruido) o de menos (una capacidad que no llega a producción
        // y nadie lo nota, porque nada se pone rojo).
        var script = Path.Combine(RepoRoot(), "tools", "service-matrix.mjs");
        Assert.True(File.Exists(script), $"Falta {script}: el workflow deriva la matriz de ahí.");

        var salida = CorrerNode(script);
        var delScript = JsonSerializer.Deserialize<string[]>(salida)!;

        Assert.Equal(Servicios(), delScript);
    }

    [Fact]
    public void Ningun_servicio_renombra_su_ensamblado()
    {
        // El ENTRYPOINT de la imagen es `dotnet <NombreDeLaCarpeta>.dll`. Un <AssemblyName>
        // distinto construye una imagen perfecta que NO ARRANCA — y el error no dice «renombraste
        // el ensamblado», dice que no encuentra un fichero.
        var malos = new List<string>();

        foreach (var s in Servicios())
        {
            var csproj = Path.Combine(RepoRoot(), s, $"{s}.csproj");
            if (!File.Exists(csproj)) { malos.Add($"{s} → falta {s}.csproj"); continue; }

            var m = Regex.Match(File.ReadAllText(csproj), @"<AssemblyName>\s*([^<]+?)\s*</AssemblyName>");
            if (m.Success && m.Groups[1].Value != s)
            {
                malos.Add($"{s} → el ensamblado se llama '{m.Groups[1].Value}' y la imagen buscaría '{s}.dll'");
            }
        }

        Assert.True(malos.Count == 0, string.Join(Environment.NewLine, malos));
    }

    [Fact]
    public void El_Dockerfile_de_servicio_existe_y_es_UNO_solo()
    {
        // Veinte servicios con veinte formas de construirse es peor que un monolito: el monolito
        // al menos es consistente. Y el día que haya que cambiar la imagen base, hay que
        // acordarse de veintidós sitios — el que se olvide no rompe el build, rompe después.
        Assert.True(File.Exists(Path.Combine(RepoRoot(), "Dockerfile.service")),
            "Falta Dockerfile.service — es el único que construye las 22.");

        var sueltos = Servicios()
            .Where(s => File.Exists(Path.Combine(RepoRoot(), s, "Dockerfile")))
            .ToList();

        Assert.True(sueltos.Count == 0,
            "Estos servicios tienen Dockerfile propio y no deberían — se construyen con " +
            $"Dockerfile.service:{Environment.NewLine}{string.Join(Environment.NewLine, sueltos)}");
    }

    [Fact]
    public void La_imagen_declara_volumen_para_el_almacen()
    {
        // Sin volumen, cada despliegue borra los datos de la capacidad. No falla, no avisa: el
        // contenedor nuevo arranca con el directorio vacío y la capacidad se comporta como recién
        // instalada. Es el modo de fallo más caro de esta imagen, y el más silencioso.
        var texto = File.ReadAllText(Path.Combine(RepoRoot(), "Dockerfile.service"));

        // Y la ruta EXACTA importa: las 21 opciones de almacenamiento del árbol tienen el mismo
        // default —AppContext.BaseDirectory/data/<nombre>, o sea /app/data/<nombre> dentro de la
        // imagen—. Montar `/data` a secas, que es lo que uno escribe por costumbre, no persiste
        // nada: el servicio escribe en /app/data y el volumen queda vacío al lado.
        Assert.Contains("VOLUME /app/data", texto, StringComparison.Ordinal);
    }

    private static string CorrerNode(string script)
    {
        using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "node",
            ArgumentList = { script },
            WorkingDirectory = RepoRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;

        var salida = p.StandardOutput.ReadToEnd();
        var error = p.StandardError.ReadToEnd();
        p.WaitForExit();

        Assert.True(p.ExitCode == 0, $"service-matrix.mjs salió con {p.ExitCode}: {error}");
        return salida;
    }
}
