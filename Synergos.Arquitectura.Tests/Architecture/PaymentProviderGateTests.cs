using System.Text.RegularExpressions;

namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// Que un proveedor de mentira no llegue a producción, y que ningún secreto llegue al repo (HU #27).
/// </summary>
/// <remarks>
/// <para><b>El defecto que esto evita ya ocurrió</b>, y está escrito en el propio
/// <c>LoggingPaymentProvider</c>: en el CMS, <c>Provider=Wompi</c> servía el stub <b>en
/// silencio</b>. Nadie mintió a propósito — el nombre estaba puesto, la configuración parecía
/// correcta, y lo que corría no movía plata. Costó una investigación.</para>
///
/// <para>La defensa no es confiar en el nombre configurado: es que cada proveedor <b>declare</b>
/// si mueve plata, y que la selección no tenga ninguna rama que caiga al stub cuando alguien pidió
/// cobrar de verdad. O cobra, o dice a gritos que no puede.</para>
/// </remarks>
public sealed class PaymentProviderGateTests
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

    private static string Programa()
        => File.ReadAllText(Path.Combine(RepoRoot(), "Synergos.Api.Payments", "Program.cs"));

    [Fact]
    public void Todo_proveedor_declara_si_mueve_plata()
    {
        // Sin esta declaración, distinguir «cobra» de «finge» exigiría leer el nombre — que es
        // exactamente lo que falló la vez pasada.
        var fuentes = Directory
            .EnumerateFiles(Path.Combine(RepoRoot(), "Synergos.Api.Payments"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

        var sinDeclarar = new List<string>();
        foreach (var f in fuentes)
        {
            var codigo = File.ReadAllText(f);
            foreach (Match m in Regex.Matches(codigo, @"class\s+(\w+)\s*:\s*[^\{]*\bIPaymentProvider\b"))
            {
                if (!codigo.Contains("MuevePlata", StringComparison.Ordinal))
                {
                    sinDeclarar.Add(m.Groups[1].Value);
                }
            }
        }

        Assert.True(sinDeclarar.Count == 0,
            $"Estos IPaymentProvider no declaran MuevePlata: {string.Join(", ", sinDeclarar)}. "
            + "Sin esa declaración no hay forma de impedir que un proveedor de mentira quede en producción.");
    }

    [Fact]
    public void Pedir_un_proveedor_de_verdad_NUNCA_cae_al_stub()
    {
        // La rama prohibida: nombre puesto + adaptador ausente → stub en silencio. Tiene que
        // terminar en NotConfigured, que rechaza y grita.
        var programa = Programa();

        // Después de descartar el caso "logging", lo único que se puede construir es el que
        // rechaza. Si apareciera otro `new LoggingPaymentProvider` más abajo, esta cuenta sube.
        var vecesStub = Regex.Count(programa, @"new LoggingPaymentProvider\(");
        Assert.True(vecesStub == 1,
            $"LoggingPaymentProvider se construye {vecesStub} veces en la selección. Solo puede ser "
            + "una: la rama explícita de desarrollo. Cualquier otra es el stub sirviendo en silencio.");

        Assert.Contains("NotConfiguredPaymentProvider", programa, StringComparison.Ordinal);
    }

    [Fact]
    public void La_credencial_sale_de_CONFIGURACION_y_no_del_repo()
    {
        // Ningún secreto entra al repo. Ni uno.
        var programa = Programa();

        Assert.Contains("Payments:", programa, StringComparison.Ordinal);

        // Nada que parezca una llave de pasarela quemada. Los prefijos son los reales de Wompi
        // (test_/prod_) y de Stripe (sk_/pk_).
        var sospechosos = Regex.Matches(programa, @"""(test_|prod_|sk_live|sk_test|pk_live)[A-Za-z0-9_]{6,}""");
        Assert.True(sospechosos.Count == 0,
            $"Hay algo con pinta de credencial en Program.cs: {string.Join(", ", sospechosos.Select(m => m.Value))}. "
            + "Y si un secreto se filtró alguna vez: SE ROTA, no se borra el mensaje.");
    }

    [Fact]
    public void El_default_sigue_siendo_el_que_NO_cobra()
    {
        // Un clon limpio tiene que correr el flujo de compra sin cuenta de pasarela. Si el
        // default fuera un proveedor real, `git clone && dotnet run` pediría credenciales para
        // arrancar — y alguien las pondría de prueba en el repo.
        var programa = Programa();

        Assert.Matches(@"pedido\.Length == 0", programa);
        Assert.Contains("LoggingPaymentProvider", programa, StringComparison.Ordinal);
    }

    /// <summary>
    /// Que nadie salga del asíncrono a la fuerza (HU #27).
    /// </summary>
    /// <remarks>
    /// <para>La costura se hizo asíncrona porque una pasarela vive al otro lado de la red. El
    /// atajo que quedaba abierto era <c>.Result</c> o <c>GetAwaiter().GetResult()</c> dentro de
    /// un proveedor, y con el cerrojo del servicio alrededor eso bloquea un hilo del pool
    /// <b>durante toda la llamada</b>: unas pocas pasarelas lentas a la vez dejan el proceso sin
    /// hilos con los que contestar.</para>
    ///
    /// <para><b>No se nota como un problema de pagos.</b> Se nota como que el servicio «se puso
    /// lento», que es la peor pista posible — y es reversible de una línea, así que la
    /// prohibición tiene que estar escrita en algo que rompa el build.</para>
    /// </remarks>
    [Fact]
    public void Nadie_bloquea_un_hilo_esperando_a_la_pasarela()
    {
        var malas = new List<string>();

        foreach (var f in Fuentes())
        {
            var n = 0;
            foreach (var linea in SinComentarios(f).Split('\n'))
            {
                n++;
                if (Regex.IsMatch(linea, @"\.GetAwaiter\(\)\s*\.GetResult\(\)")
                    || Regex.IsMatch(linea, @"\)\s*\.Result\b")
                    || Regex.IsMatch(linea, @"\bTask\.(Wait|WaitAll|WaitAny)\b"))
                {
                    malas.Add($"{Path.GetFileName(f)}:{n} → {linea.Trim()}");
                }
            }
        }

        Assert.True(malas.Count == 0,
            "Api.Payments espera a la pasarela sin bloquear hilos. Un sync-over-async acá "
            + "vacía el pool mientras el cerrojo del servicio sigue tomado." + Environment.NewLine
            + string.Join(Environment.NewLine, malas));
    }

    /// <summary>Las fuentes de la capacidad, sin lo que genera el compilador.</summary>
    private static IEnumerable<string> Fuentes()
        => Directory
            .EnumerateFiles(Path.Combine(RepoRoot(), "Synergos.Api.Payments"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    /// <summary>
    /// Quita comentarios para no medir la prosa que documenta la regla.
    /// </summary>
    /// <remarks>
    /// El repo ya se tropezó dos veces con un gate que pasaba en verde porque la explicación
    /// citaba justo lo que él prohibía (#29 y el de #33a). Medir prosa es no medir nada.
    /// </remarks>
    private static string SinComentarios(string file)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var line in File.ReadLines(file))
        {
            var t = line.TrimStart();
            if (t.StartsWith("//", StringComparison.Ordinal)
                || t.StartsWith('*')
                || t.StartsWith("/*", StringComparison.Ordinal))
            {
                sb.AppendLine();
                continue;
            }

            var i = line.IndexOf("//", StringComparison.Ordinal);
            sb.AppendLine(i >= 0 ? line[..i] : line);
        }
        return sb.ToString();
    }
}
