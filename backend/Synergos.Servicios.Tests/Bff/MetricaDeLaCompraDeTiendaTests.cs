using Synergos.CMS.Tests.Architecture;
using Synergos.CMS.Tests.Bff.FlujoDeMuestra;
using Xunit.Abstractions;

namespace Synergos.CMS.Tests.Bff;

/// <summary>
/// La medida del caso común sobre un orquestador que ya tenía motor (004-tienda-compra, SC-010, T045): lo que
/// <c>Bff.Tienda</c> escribe para declarar <c>tienda.compra</c>, contado como el informe 81 (sin comentarios ni
/// líneas en blanco), separado de lo que NO es del caso común y está atribuido a su dueño.
/// </summary>
/// <remarks>
/// <para><b>Flujo</b> = la región del modo declarado en el arranque (la lectura del modo, el bloque que registra el
/// host y la línea que mapea la puerta), la definición y el proyecto: la meta del núcleo es ≤ 60 líneas de C# y
/// ≤ 4 ficheros. El arranque es también el del motor imperativo, así que del <c>Program.cs</c> sólo cuenta esa
/// región, con los dos interinos que se registran dentro.</para>
///
/// <para><b>Dominio</b> = el paso propio de la tienda. <b>Interino</b> = lo que espera una pieza del núcleo: el tipo
/// de las líneas del pedido (N-12) y el freno de códigos de cupón (N-11). <b>Oferta</b> = el código del vertical que
/// la oferta genérica retirará (R-9). <b>Motor imperativo</b> = lo que ya estaba y sigue sirviendo
/// <c>/v1/purchases</c>, más lo que la oferta le sumó en sus mismos ficheros. Un fichero nuevo del orquestador que
/// no esté en ninguna lista es rojo: no puede crecer sin decir de quién es.</para>
/// </remarks>
public sealed class MetricaDeLaCompraDeTiendaTests(ITestOutputHelper salida)
{
    private static readonly string[] DelFlujo = ["Program.cs", "flujos/tienda.compra.json", "Synergos.Bff.Tienda.csproj"];

    private static readonly string[] DeDominio = ["Domain/PasoRevisarLineas.cs"];

    /// <summary>La devolución (US 7): un flujo POSTERIOR del personal, declarado entero en su JSON (núcleo P6 y P8).</summary>
    private static readonly string[] DeLaDevolucion = ["flujos/tienda.devolucion.json"];

    private static readonly string[] Interinos = ["Domain/TipoLineasDePedido.cs", "Domain/IntentosDeCodigo.cs", "Endpoints/CuponDeLaCompra.cs"];

    private static readonly string[] DeLaOferta = ["Domain/OfertaDeTienda.cs"];

    private static readonly string[] DelMotorImperativo =
    [
        "Domain/PurchaseFlow.cs", "Domain/Saga.cs", "Domain/TiendaCompensationExecutor.cs", "Clients/TiendaCapabilities.cs",
        "Clients/TiendaDtos.cs", "Contracts/TiendaContracts.cs", "Endpoints/TiendaEndpoints.cs",
    ];

    private static string Raiz() => Proyectos.Dir("Synergos.Bff.Tienda");

    private static int Lineas(IEnumerable<string> ficheros)
        => ficheros.Sum(f => MetricaDelFlujoDeMuestraTests.LineasDeCodigo(File.ReadAllText(Path.Combine(Raiz(), f))));

    /// <summary>
    /// La región del modo declarado en el arranque: desde <c>var declarado =</c> hasta que cierra su bloque, la
    /// línea que mapea la puerta y lo que la región usa de arriba (sus dos <c>using</c> y el vocabulario compartido).
    /// </summary>
    internal static string RegionDeclarada(string programa)
    {
        var lineas = programa.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var desde = Array.FindIndex(lineas, l => l.StartsWith("var declarado =", StringComparison.Ordinal));
        if (desde < 0) return string.Empty;
        var hasta = desde;
        for (int i = desde, profundidad = 0; i < lineas.Length; i++)
        {
            profundidad += lineas[i].Count(c => c == '{') - lineas[i].Count(c => c == '}');
            if (i > desde + 1 && profundidad == 0 && lineas[i].Trim() == "}")
            {
                hasta = i;
                break;
            }
        }

        var deArriba = lineas.Where(l => l is "using Synergos.Bff.Core.Flow;" or "using Synergos.Bff.Pasos;"
                                         || l.StartsWith("var vocabulario =", StringComparison.Ordinal));
        var mapeo = lineas.Where(l => l.StartsWith("if (declarado) app.", StringComparison.Ordinal));
        return string.Join('\n', deArriba.Concat(lineas[desde..(hasta + 1)]).Concat(mapeo));
    }

    [Fact]
    public void Todo_fichero_del_orquestador_esta_atribuido()
    {
        var enDisco = Directory.EnumerateFiles(Raiz(), "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(Raiz(), f).Replace('\\', '/'))
            .Where(f => !f.StartsWith("bin/", StringComparison.Ordinal) && !f.StartsWith("obj/", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(DelFlujo.Concat(DeDominio).Concat(DeLaDevolucion).Concat(Interinos).Concat(DeLaOferta).Concat(DelMotorImperativo).Order(StringComparer.Ordinal), enDisco);
    }

    [Fact]
    public void Declarar_la_compra_cuesta_como_mucho_60_lineas_y_4_ficheros_y_lo_demas_queda_atribuido()
    {
        var region = RegionDeclarada(File.ReadAllText(Path.Combine(Raiz(), "Program.cs")));
        Assert.Contains("AddOrquestadorDeclarado", region, StringComparison.Ordinal);   // la región se ve: el control
        Assert.Contains("MapFlujosDeclarados", region, StringComparison.Ordinal);

        var flujo = MetricaDelFlujoDeMuestraTests.LineasDeCodigo(region);
        salida.WriteLine($"MEDIDA: flujo {flujo} líneas (con los registros de los interinos) en {DelFlujo.Length} ficheros; "
                         + $"dominio {Lineas(DeDominio)}; devolución {Lineas(DeLaDevolucion.Where(f => f.EndsWith(".cs", StringComparison.Ordinal)))} líneas de C# en {DeLaDevolucion.Length} fichero; interinos (N-12, N-11) {Lineas(Interinos)}; oferta (R-9) {Lineas(DeLaOferta)}; "
                         + $"motor imperativo {Lineas(DelMotorImperativo)}.");
        Assert.True(flujo <= 60, $"El C# propio del flujo son {flujo} líneas y la meta es ≤ 60.");
        Assert.True(DelFlujo.Length <= 4, $"Declarar el flujo tocó {DelFlujo.Length} ficheros y la meta es ≤ 4.");
    }

    [Fact]
    public void La_region_es_la_del_modo_declarado_y_nada_mas()
    {
        const string programa = """
            using Synergos.Bff.Core;
            using Synergos.Bff.Core.Flow;
            var vocabulario = new SagaVocabulary("x", "y");
            builder.Services.AddSingleton<Otra>();
            var declarado = Leer() == Declarado;
            if (declarado)
            {
                // un comentario
                builder.AddOrquestadorDeclarado(vocabulario);
                builder.Services.AddSingleton(sp =>
                {
                    return new Interino();
                });
            }

            var app = builder.Build();
            if (declarado) app.MapFlujosDeclarados();
            app.Run();
            """;

        Assert.Equal(12, MetricaDelFlujoDeMuestraTests.LineasDeCodigo(RegionDeclarada(programa)));
    }
}
