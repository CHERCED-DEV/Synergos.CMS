using Synergos.CMS.Tests.Architecture;
using Synergos.CMS.Tests.Bff.FlujoDeMuestra;
using Xunit.Abstractions;

namespace Synergos.CMS.Tests.Bff.Educacion;

/// <summary>
/// La medida del caso común en la matrícula (010-educacion-matricula T007, SC-007): lo que <c>Bff.Educacion</c>
/// escribe para declarar <c>educacion.matricula</c>, contado como el informe 81 (sin comentarios ni líneas en blanco),
/// separado de lo que NO es del caso común y está atribuido a su necesidad.
/// </summary>
/// <remarks>
/// <para><b>Flujo</b> = el arranque, la definición, la configuración y el proyecto: la meta es ≤ 60 líneas de C# y ≤ 4
/// ficheros. <b>Interino</b> = lo que espera la oferta genérica con variantes de precio (NM-1): el paso
/// <c>educacion.linea</c> y el nombre del sujeto, que comparte con la oferta. <b>Oferta</b> = el código del vertical que
/// esa misma pieza (P12) dejará listo para retirar. <b>Devolución</b> = el flujo posterior del personal (010-P2 B), sólo
/// declarado. Un fichero nuevo del orquestador que no esté en ninguna de las listas es rojo: no puede crecer sin decir
/// de quién es.</para>
/// </remarks>
public sealed class MetricaDeEducacionTests(ITestOutputHelper salida)
{
    private static readonly string[] DelFlujo =
        ["Program.cs", "flujos/educacion.matricula.json", "appsettings.json", "Synergos.Bff.Educacion.csproj"];

    private static readonly string[] Interinos = ["Domain/PasoLineaDelPlan.cs", "Domain/SujetoDelPlan.cs"];

    private static readonly string[] DeLaOferta =
        ["Domain/OfertaDeCursos.cs", "Endpoints/OfertasEndpoints.cs", "Clients/CapacidadesDeLaOferta.cs", "Contracts/OfertaContracts.cs"];

    /// <summary>La devolución del personal (lote 8, 010-P2 B): un flujo posterior declarado, sin una línea de C#.</summary>
    private static readonly string[] DeLaDevolucion = ["flujos/educacion.devolucion.json"];

    private static string Raiz() => Proyectos.Dir("Synergos.Bff.Educacion");

    private static int Lineas(IEnumerable<string> ficheros)
        => ficheros.Where(f => f.EndsWith(".cs", StringComparison.Ordinal))
            .Sum(f => MetricaDelFlujoDeMuestraTests.LineasDeCodigo(File.ReadAllText(Path.Combine(Raiz(), f))));

    [Fact]
    public void Todo_fichero_del_orquestador_esta_atribuido()
    {
        var enDisco = Directory.EnumerateFiles(Raiz(), "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(Raiz(), f).Replace('\\', '/'))
            .Where(f => !f.StartsWith("bin/", StringComparison.Ordinal) && !f.StartsWith("obj/", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(DelFlujo.Concat(Interinos).Concat(DeLaOferta).Concat(DeLaDevolucion).Order(StringComparer.Ordinal), enDisco);
    }

    [Fact]
    public void La_devolucion_del_personal_no_cuesta_una_linea_de_CSharp()
        => Assert.All(DeLaDevolucion, f => Assert.EndsWith(".json", f, StringComparison.Ordinal));

    [Fact]
    public void Declarar_la_matricula_cuesta_como_mucho_60_lineas_y_4_ficheros_y_lo_demas_queda_atribuido()
    {
        var flujo = Lineas(DelFlujo);
        var interino = Lineas(Interinos);
        var oferta = Lineas(DeLaOferta);

        salida.WriteLine($"MEDIDA: flujo {flujo} líneas en {DelFlujo.Length} ficheros; interino (NM-1) {interino}; oferta (P12, NM-1) {oferta}.");
        Assert.True(flujo <= 60, $"El C# propio del flujo son {flujo} líneas y la meta es ≤ 60.");
        Assert.True(DelFlujo.Length <= 4, $"Declarar el flujo tocó {DelFlujo.Length} ficheros y la meta es ≤ 4.");
    }
}
