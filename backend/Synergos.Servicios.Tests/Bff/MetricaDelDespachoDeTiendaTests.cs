using System.Text.RegularExpressions;
using Synergos.CMS.Tests.Architecture;
using Synergos.CMS.Tests.Bff.FlujoDeMuestra;
using Xunit.Abstractions;

namespace Synergos.CMS.Tests.Bff;

/// <summary>
/// La medida del despacho (013-seller-operaciones, SC-007, T022): declarar enviar y entregar en <c>Bff.Tienda</c> cuesta,
/// fuera de sus definiciones, a lo sumo 60 líneas de pegamento en 4 ficheros —la meta del núcleo—.
/// </summary>
/// <remarks>
/// <para><b>Es el segundo método</b>: sobre el árbol, contado como el informe 81 (sin comentarios ni líneas en blanco). El
/// primero lee git (<c>specs/013-seller-operaciones/medicion/medir-despacho.mjs</c>).</para>
///
/// <para><b>Con NS-1 y NS-2 en el núcleo</b> (T095), enviar y entregar se declaran sobre sus pasos comunes —
/// <c>fulfillment.seguir</c> con el estado como dato, y la lectura <c>detalle</c> con <c>fulfillment.leer</c>— y sus
/// interinos se retiraron: los dos pasos del despacho y la ruta que leía el envío. <b>Definición</b> = los dos flujos.
/// <b>Pegamento</b> = lo demás del despacho en el arranque: una línea que lo nombre cuenta, y un fichero nuevo del
/// orquestador sin atribuir lo para <see cref="MetricaDeLaCompraDeTiendaTests"/>.</para>
/// </remarks>
public sealed partial class MetricaDelDespachoDeTiendaTests(ITestOutputHelper salida)
{
    private static readonly string[] Definiciones = ["flujos/tienda.envio.json", "flujos/tienda.entrega.json"];

    /// <summary>Lo del despacho que es pegamento, fichero entero: hoy ninguno.</summary>
    private static readonly string[] Pegamento = [];

    private static string Leer(string fichero) => File.ReadAllText(Path.Combine(Proyectos.Dir("Synergos.Bff.Tienda"), fichero));

    private static int Lineas(IEnumerable<string> ficheros) => ficheros.Sum(f => MetricaDelFlujoDeMuestraTests.LineasDeCodigo(Leer(f)));

    [GeneratedRegex(@"Despacho|Envio\b|tienda\.(envio|entrega)")]
    private static partial Regex DelDespacho();

    /// <summary>Las líneas de código del arranque que son del despacho: todas cuentan como pegamento.</summary>
    internal static IReadOnlyList<string> DelArranque(string programa)
        => programa.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith("//", StringComparison.Ordinal) && DelDespacho().IsMatch(l))
            .ToList();

    [Fact]
    public void Declarar_enviar_y_entregar_cuesta_a_lo_sumo_60_lineas_de_pegamento_en_4_ficheros()
    {
        // El control: el despacho está declarado sobre los pasos del núcleo. Si no, «0 de pegamento» no diría nada.
        Assert.Contains("\"fulfillment.seguir\"", Leer("flujos/tienda.envio.json"), StringComparison.Ordinal);
        Assert.Contains("\"detalle\"", Leer("flujos/tienda.envio.json"), StringComparison.Ordinal);
        Assert.Contains("\"fulfillment.seguir\"", Leer("flujos/tienda.entrega.json"), StringComparison.Ordinal);

        var sueltas = DelArranque(Leer("Program.cs"));
        var pegamento = sueltas.Count + Lineas(Pegamento);
        var ficheros = Pegamento.Length + (sueltas.Count > 0 ? 1 : 0);
        salida.WriteLine(
            $"MEDIDA: definiciones {Lineas(Definiciones)} líneas en {Definiciones.Length} ficheros (no cuentan); "
            + $"pegamento {pegamento} líneas en {ficheros} ficheros.");
        Assert.True(pegamento <= 60, $"El pegamento del despacho son {pegamento} líneas y la meta es ≤ 60: {string.Join(" | ", sueltas)}");
        Assert.True(ficheros <= 4, $"El despacho tocó {ficheros} ficheros de pegamento y la meta es ≤ 4.");
    }

    [Fact]
    public void Una_linea_del_arranque_que_nombra_el_despacho_cuenta_como_pegamento()
    {
        const string programa = """
            // Las reglas del despacho.
            builder.Services.AddSingleton<ReglasDelDespacho>();
            app.MapTiendaEndpoints();
            """;

        Assert.Equal(["builder.Services.AddSingleton<ReglasDelDespacho>();"], DelArranque(programa));
    }
}
