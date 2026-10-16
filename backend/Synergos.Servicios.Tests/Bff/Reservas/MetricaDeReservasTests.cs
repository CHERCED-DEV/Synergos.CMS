using Synergos.CMS.Tests.Architecture;
using Synergos.CMS.Tests.Bff.FlujoDeMuestra;
using Xunit.Abstractions;

namespace Synergos.CMS.Tests.Bff.Reservas;

/// <summary>
/// La medida del caso común en un flujo de PRODUCTO (011-booking-reserva, SC-002; lote 4, 011-P1): lo que
/// <c>Bff.Reservas</c> escribe para declarar <c>reservas.servicio</c>, contado como el informe 81 (sin comentarios
/// ni líneas en blanco), separado de lo que NO es del caso común y está atribuido a su necesidad.
/// </summary>
/// <remarks>
/// <para><b>Flujo</b> = el arranque, la definición, la configuración y el proyecto: la meta es ≤ 60 líneas de C# y
/// ≤ 4 ficheros. <b>Interino</b> = los pasos de dominio que esperan una pieza del núcleo (NR-1, NB-1). <b>Oferta</b> =
/// el código del vertical que la oferta genérica (P12, NB-8) retirará. Un fichero nuevo del orquestador que no
/// esté en ninguna de las tres listas es rojo: no puede crecer sin decir de quién es.</para>
/// </remarks>
public sealed class MetricaDeReservasTests(ITestOutputHelper salida)
{
    private static readonly string[] DelFlujo =
        ["Program.cs", "flujos/reservas.servicio.json", "appsettings.json", "Synergos.Bff.Reservas.csproj"];

    private static readonly string[] Interinos = ["Domain/PasosDeReservas.cs"];

    private static readonly string[] DeLaOferta =
        ["Domain/OfertaDeReservas.cs", "Endpoints/OfertasEndpoints.cs", "Clients/CapacidadesDeLaOferta.cs", "Contracts/OfertaContracts.cs"];

    private static string Raiz() => Proyectos.Dir("Synergos.Bff.Reservas");

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

        Assert.Equal(DelFlujo.Concat(Interinos).Concat(DeLaOferta).Order(StringComparer.Ordinal), enDisco);
    }

    [Fact]
    public void Declarar_la_reserva_cuesta_como_mucho_60_lineas_y_4_ficheros_y_lo_demas_queda_atribuido()
    {
        var flujo = Lineas(DelFlujo);
        var interino = Lineas(Interinos);
        var oferta = Lineas(DeLaOferta);

        salida.WriteLine($"MEDIDA: flujo {flujo} líneas en {DelFlujo.Length} ficheros; interino (NR-1, NB-1) {interino}; oferta (P12) {oferta}.");
        Assert.True(flujo <= 60, $"El C# propio del flujo son {flujo} líneas y la meta es ≤ 60.");
        Assert.True(DelFlujo.Length <= 4, $"Declarar el flujo tocó {DelFlujo.Length} ficheros y la meta es ≤ 4.");
    }
}
