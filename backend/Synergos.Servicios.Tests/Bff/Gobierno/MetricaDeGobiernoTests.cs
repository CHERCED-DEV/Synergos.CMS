using Synergos.CMS.Tests.Architecture;
using Synergos.CMS.Tests.Bff.FlujoDeMuestra;
using Xunit.Abstractions;

namespace Synergos.CMS.Tests.Bff.Gobierno;

/// <summary>
/// La medida del caso común en la radicación (009-gobierno-tramite T025): lo que <c>Bff.Gobierno</c> escribe para
/// declarar <c>gobierno.radicacion</c>, contado como el informe 81 (sin comentarios ni líneas en blanco), separado de lo
/// que NO es del caso común y está atribuido a su necesidad.
/// </summary>
/// <remarks>
/// <para><b>Flujo</b> = el arranque, la definición, la configuración y el proyecto: la meta es ≤ 60 líneas de C# y ≤ 4
/// ficheros. <b>Oferta</b> = publicar y retirar la tasa de un trámite: el código del vertical que la oferta genérica del
/// núcleo (001-P11, lote 11) dejará listo para retirar. <b>Sin interinos</b>: la radicación usa sólo pasos comunes —los
/// de procesos son del núcleo (lote 7)—, así que ninguna pieza propia espera una necesidad. Un fichero nuevo del
/// orquestador que no esté en ninguna de las listas es rojo: no puede crecer sin decir de quién es.</para>
/// </remarks>
public sealed class MetricaDeGobiernoTests(ITestOutputHelper salida)
{
    private static readonly string[] DelFlujo =
        ["Program.cs", "flujos/gobierno.radicacion.json", "appsettings.json", "Synergos.Bff.Gobierno.csproj"];

    private static readonly string[] DeLaOferta =
        ["Domain/OfertaDeGobierno.cs", "Endpoints/OfertasEndpoints.cs", "Clients/CapacidadesDeLaOferta.cs", "Contracts/OfertaContracts.cs"];

    private static string Raiz() => Proyectos.Dir("Synergos.Bff.Gobierno");

    private static List<(string Fichero, int Lineas)> Medir(IEnumerable<string> ficheros)
        => ficheros.Where(f => f.EndsWith(".cs", StringComparison.Ordinal))
            .Select(f => (f, MetricaDelFlujoDeMuestraTests.LineasDeCodigo(File.ReadAllText(Path.Combine(Raiz(), f)))))
            .ToList();

    private static string Detalle(IEnumerable<(string Fichero, int Lineas)> medida)
        => string.Join(", ", medida.Select(m => $"{m.Fichero} {m.Lineas}"));

    [Fact]
    public void Todo_fichero_del_orquestador_esta_atribuido()
    {
        var enDisco = Directory.EnumerateFiles(Raiz(), "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(Raiz(), f).Replace('\\', '/'))
            .Where(f => !f.StartsWith("bin/", StringComparison.Ordinal) && !f.StartsWith("obj/", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(DelFlujo.Concat(DeLaOferta).Order(StringComparer.Ordinal), enDisco);
    }

    [Fact]
    public void Declarar_la_radicacion_cuesta_como_mucho_60_lineas_y_4_ficheros_y_la_oferta_queda_atribuida()
    {
        var flujo = Medir(DelFlujo);
        var oferta = Medir(DeLaOferta);
        var lineas = flujo.Sum(m => m.Lineas);

        salida.WriteLine($"MEDIDA: flujo {lineas} líneas en {DelFlujo.Length} ficheros ({Detalle(flujo)}); oferta (001-P11) {oferta.Sum(m => m.Lineas)} ({Detalle(oferta)}).");
        Assert.True(lineas <= 60, $"El C# propio del flujo son {lineas} líneas ({Detalle(flujo)}) y la meta es ≤ 60.");
        Assert.True(DelFlujo.Length <= 4, $"Declarar el flujo tocó {DelFlujo.Length} ficheros ({string.Join(", ", DelFlujo)}) y la meta es ≤ 4.");
    }
}
