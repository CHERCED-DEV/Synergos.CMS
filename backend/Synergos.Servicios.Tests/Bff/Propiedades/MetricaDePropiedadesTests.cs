using Synergos.CMS.Tests.Architecture;
using Synergos.CMS.Tests.Bff.FlujoDeMuestra;
using Xunit.Abstractions;

namespace Synergos.CMS.Tests.Bff.Propiedades;

/// <summary>
/// La medida del caso común en la visita (008-propiedades-visita, SC-006; la regla del hito, 003-P1): lo que
/// <c>Bff.Propiedades</c> escribe para declarar <c>propiedades.visita</c>, contado como el informe 81 (sin comentarios
/// ni líneas en blanco), separado de lo interino y atribuido a su necesidad.
/// </summary>
/// <remarks>
/// <para><b>Flujo</b> = el arranque, la definición, la configuración y el proyecto: la meta es ≤ 60 líneas de C# y ≤ 4
/// ficheros («superado»). <b>Interino</b> = los pasos de dominio que esperan piezas del núcleo: la duración y la
/// modalidad (NB-1, NR-4) y el aviso sin importe (NR-3). Con ellos, el tramo es «superado con deuda» mientras sumen
/// menos de 150. Un fichero nuevo del orquestador que no esté en ninguna lista es rojo: no crece sin decir de quién
/// es.</para>
/// </remarks>
public sealed class MetricaDePropiedadesTests(ITestOutputHelper salida)
{
    private static readonly string[] DelFlujo =
        ["Program.cs", "flujos/propiedades.visita.json", "appsettings.json", "Synergos.Bff.Propiedades.csproj"];

    private static readonly string[] Interinos = ["Domain/PasosDePropiedades.cs"];

    /// <summary>Lo posterior al cierre (historia 8, 008-P5): otro flujo, sólo su definición, sin C#.</summary>
    private static readonly string[] DeLaCancelacion = ["flujos/propiedades.visita-cancelacion.json"];

    private static string Raiz() => Proyectos.Dir("Synergos.Bff.Propiedades");

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

        Assert.Equal(DelFlujo.Concat(Interinos).Concat(DeLaCancelacion).Order(StringComparer.Ordinal), enDisco);
    }

    [Fact]
    public void Declarar_la_visita_cuesta_como_mucho_60_lineas_y_4_ficheros_y_lo_interino_queda_por_debajo_de_150()
    {
        var flujo = Lineas(DelFlujo);
        var interino = Lineas(Interinos);

        salida.WriteLine($"MEDIDA: flujo {flujo} líneas en {DelFlujo.Length} ficheros; interino (NB-1, NR-3, NR-4) {interino}.");
        Assert.True(flujo <= 60, $"El C# propio del flujo son {flujo} líneas y la meta es ≤ 60.");
        Assert.True(DelFlujo.Length <= 4, $"Declarar el flujo tocó {DelFlujo.Length} ficheros y la meta es ≤ 4.");
        Assert.True(flujo + interino < 150, $"Con lo interino son {flujo + interino} líneas: el tramo «con deuda» llega a 150.");
    }
}
