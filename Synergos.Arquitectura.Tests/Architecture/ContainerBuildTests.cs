using System.Text.Json;
using System.Text.RegularExpressions;

namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// Que las 24 imágenes se puedan construir con <b>un solo</b> <c>Dockerfile.service</c>, y que
/// la matriz que las enumera no se quede corta (HU #17).
/// </summary>
/// <remarks>
/// <para><b>Los tres supuestos que hace la imagen, y que acá se vigilan.</b> Construir 24
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
    /// <summary>Una fila del JSON de <c>tools/service-matrix.mjs</c>.</summary>
    /// <remarks>
    /// Lleva la RUTA además del nombre desde el #136: el backend ya no está plano, así que
    /// <c>Dockerfile.service</c> necesita las dos y las cruza (<c>basename ruta == nombre</c>)
    /// antes de construir — un descuadre produciría una imagen con el nombre de un servicio y
    /// la fuente de otro, que arranca y contesta <c>/health</c>.
    /// </remarks>
    private sealed record ServicioDeLaMatriz(string nombre, string ruta);

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
        => Proyectos.Directorios()
            .Select(Path.GetFileName)
            .Where(n => n!.StartsWith("Synergos.Api.", StringComparison.Ordinal)
                     || n.StartsWith("Synergos.Bff.", StringComparison.Ordinal))
            .Where(n => File.Exists(Proyectos.Dir(n!, "Program.cs")))
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
        Assert.False(File.Exists(Proyectos.Dir("Synergos.Bff.Core", "Program.cs")),
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
        // El JSON de `service-matrix.mjs` lleva la RUTA además del nombre desde el #136:
        // `Dockerfile.service` necesita las dos y las cruza antes de construir.
        var delScript = JsonSerializer.Deserialize<ServicioDeLaMatriz[]>(salida)!
            .Select(s => s.nombre).ToArray();

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
            var csproj = Proyectos.Dir(s, $"{s}.csproj");
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
        // acordarse de veinticuatro sitios — el que se olvide no rompe el build, rompe después.
        Assert.True(File.Exists(Path.Combine(RepoRoot(), "Dockerfile.service")),
            "Falta Dockerfile.service — es el único que construye las 24.");

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

    /// <summary>Los ficheros que escriben en prosa cuántas imágenes de servicio hay.</summary>
    private static readonly string[] ProsaConLaCifra =
    [
        "Dockerfile.service",
        "tools/respaldo.sh",
        "tools/restaurar.sh",
        "tools/prueba-restauracion.sh",
        "tools/service-matrix.mjs",
        "Synergos.Arquitectura.Tests/Architecture/ContainerBuildTests.cs",
        "Synergos.Arquitectura.Tests/Architecture/PoliticaDeBuildEnLaImagenTests.cs",
        "Synergos.Arquitectura.Tests/Architecture/RespaldoTests.cs",
        "Synergos.Arquitectura.Tests/Architecture/RuntimesDeLosDosArbolesTests.cs",
    ];

    /// <summary>
    /// «las N», con N de dos cifras. «las 21 opciones de almacenamiento» es otra cuenta y se salta
    /// por nombre.
    /// </summary>
    private static readonly Regex CifraEnLaProsa = new(
        @"\b(?:las|los|LAS|LOS)\s+(?:<b>)?(\d{2})(?:</b>)?\b(?!\s+opciones)", RegexOptions.Compiled, TimeSpan.FromSeconds(5));

    /// <summary>
    /// La cifra de imágenes que escribe la prosa es la de la matriz.
    /// </summary>
    /// <remarks>
    /// <para><b>El defecto.</b> La matriz construye veinticuatro imágenes desde que existen los cuatro
    /// orquestadores (antes de la F2 de la ADR 0140), y una decena de comentarios seguía diciendo
    /// veintidós: el número de cuando no estaban. La F2 lo copió en tres sitios más, uno de ellos en
    /// <c>RuntimesDeLosDosArbolesTests</c>, que a la vez afirmaba «hay 24». <c>images.yml</c> no lleva
    /// la cifra (#114); estos ficheros sí, porque la frase se lee mejor con ella, así que se cuenta
    /// contra <see cref="Servicios"/>.</para>
    /// </remarks>
    [Fact]
    public void La_cifra_de_imagenes_que_escribe_la_prosa_es_la_de_la_matriz()
    {
        var hay = Servicios().Count;
        var vistas = 0;
        var malas = new List<string>();

        foreach (var fichero in ProsaConLaCifra)
        {
            var lineas = File.ReadAllLines(Path.Combine(RepoRoot(), fichero));
            for (var i = 0; i < lineas.Length; i++)
            {
                foreach (Match m in CifraEnLaProsa.Matches(lineas[i]))
                {
                    vistas++;
                    if (int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) != hay)
                    {
                        malas.Add($"  {fichero}:{i + 1}: «{m.Value}» y la matriz construye {hay}.");
                    }
                }
            }
        }

        Assert.True(vistas >= 10, $"Sólo se encontraron {vistas} cifras en la prosa: el patrón dejó de ver.");
        Assert.True(malas.Count == 0,
            $"La prosa cuenta otras imágenes que las que construye la matriz ({hay}): una cifra escrita a " +
            $"mano que nadie cuadra se pudre (#114).{Environment.NewLine}{string.Join(Environment.NewLine, malas)}");
    }

    [Fact]
    public void La_red_de_seguridad_del_script_se_dispara_cuando_no_descubre_nada()
    {
        // El script tiene escrito «un gate que no puede fallar es peor que no tener gate», y su
        // red —salir con 1 si la lista sale vacía— vivía DENTRO del bloque de entrada CLI. Ese
        // bloque no corría en Windows, así que la red era código muerto: salida vacía y exit 0,
        // que es exactamente el «verde construyendo cero imágenes» que existe para impedir.
        //
        // Este gate la ejerce de verdad: corre el script sobre un árbol donde NO hay servicios.
        // Si alguien vuelve a romper el guard de entrada, acá se ve — y se ve con el mensaje del
        // script, no con un error de parseo tres capas más arriba.
        var script = Path.Combine(RepoRoot(), "tools", "service-matrix.mjs");
        var vacio = Path.Combine(Path.GetTempPath(), "syn-matriz-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(vacio);

        try
        {
            var (codigo, salida, error) = CorrerNodeEn(script, vacio);

            Assert.True(codigo == 1,
                $"El script salió con {codigo} sobre un árbol SIN servicios. Tiene que salir con 1: " +
                $"un descubrimiento roto que sale con 0 deja el workflow construyendo cero imágenes. " +
                $"stdout={salida.Length} bytes, stderr={error}");

            Assert.Contains("no se encontró NINGÚN servicio", error, StringComparison.Ordinal);
            Assert.True(string.IsNullOrWhiteSpace(salida),
                "Sin servicios no hay JSON que imprimir; algo salió por stdout igual.");
        }
        finally
        {
            Directory.Delete(vacio, recursive: true);
        }
    }

    private static string CorrerNode(string script)
    {
        var (codigo, salida, error) = CorrerNodeEn(script, RepoRoot());

        Assert.True(codigo == 0, $"service-matrix.mjs salió con {codigo}: {error}");

        // Se comprueba ANTES de deserializar y por separado. Con la salida vacía, el
        // `JsonException` que salta después no nombra nada: dice «the input does not contain any
        // JSON tokens» y deja al que lo lee buscando un fichero corrupto que no existe. La causa
        // real —el script no llegó a imprimir— tiene que decirla el gate.
        Assert.False(string.IsNullOrWhiteSpace(salida),
            "service-matrix.mjs salió con 0 pero no imprimió nada. Casi siempre es el guard de " +
            "entrada CLI: si no reconoce que se lo invocó a él, no corre su bloque —ni su red de " +
            "seguridad— y devuelve 0 como si todo estuviera bien.");

        return salida;
    }

    private static (int Codigo, string Salida, string Error) CorrerNodeEn(string script, string dir)
    {
        using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "node",
            ArgumentList = { script },
            WorkingDirectory = dir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            // Node escribe UTF-8; sin esto, en Windows se lee con la página de la consola (#170).
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
        })!;

        var salida = p.StandardOutput.ReadToEnd();
        var error = p.StandardError.ReadToEnd();
        p.WaitForExit();

        return (p.ExitCode, salida, error);
    }
}
