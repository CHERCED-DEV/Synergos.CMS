using System.Text.RegularExpressions;
using Synergos.CMS.Tests.Architecture;
using Synergos.CMS.Tests.Bff.FlujoDeMuestra;
using Xunit.Abstractions;

namespace Synergos.CMS.Tests.Bff;

/// <summary>
/// La medida del despacho (013-seller-operaciones, SC-007, T022): declarar enviar y entregar en <c>Bff.Tienda</c> cuesta,
/// fuera de sus definiciones, a lo sumo 60 líneas de pegamento en 4 ficheros —la meta del núcleo—, sin contar los
/// interinos atribuidos a sus necesidades: NS-1 (seguir un envío) y NS-2 (leer el envío de una compra).
/// </summary>
/// <remarks>
/// <para><b>Es el segundo método</b>: sobre el árbol, contado como el informe 81 (sin comentarios ni líneas en blanco). El
/// primero lee git (<c>specs/013-seller-operaciones/medicion/medir-despacho.mjs</c>).</para>
///
/// <para><b>Definición</b> = los dos flujos. <b>NS-1</b> = los pasos interinos y su cableado en el arranque; <b>NS-2</b> =
/// la lectura del envío. <b>Pegamento</b> = lo demás del despacho: hoy nada, porque con los dos interinos declarar un acto
/// del personal es sólo su definición. Una línea nueva del despacho en el arranque que no sea de ninguno de los dos
/// cuenta como pegamento, y un fichero nuevo del orquestador sin atribuir lo para <see cref="MetricaDeLaCompraDeTiendaTests"/>.</para>
/// </remarks>
public sealed partial class MetricaDelDespachoDeTiendaTests(ITestOutputHelper salida)
{
    private static readonly string[] Definiciones = ["flujos/tienda.envio.json", "flujos/tienda.entrega.json"];

    /// <summary>Lo del despacho que es pegamento, fichero entero: hoy ninguno.</summary>
    private static readonly string[] Pegamento = [];

    private static readonly string[] DeNs1 = ["Domain/PasosDelEnvio.cs"];

    private static string Leer(string fichero) => File.ReadAllText(Path.Combine(Proyectos.Dir("Synergos.Bff.Tienda"), fichero));

    private static int Lineas(IEnumerable<string> ficheros) => ficheros.Sum(f => MetricaDelFlujoDeMuestraTests.LineasDeCodigo(Leer(f)));

    [GeneratedRegex(@"\b(envios|EnviosDelDespacho|PasosDelDespacho)\b")]
    private static partial Regex DeNs1EnElArranque();

    [GeneratedRegex(@"\bMapLecturaDelEnvio\b")]
    private static partial Regex DeNs2EnElArranque();

    [GeneratedRegex(@"Despacho|Envio\b|tienda\.(envio|entrega)")]
    private static partial Regex DelDespacho();

    /// <summary>Las líneas de código del despacho en el arranque, con su dueño: NS-1, NS-2 o nadie (pegamento).</summary>
    internal static IReadOnlyList<(string Linea, string? Dueno)> DelArranque(string programa)
        => programa.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith("//", StringComparison.Ordinal))
            .Where(l => DeNs1EnElArranque().IsMatch(l) || DeNs2EnElArranque().IsMatch(l) || DelDespacho().IsMatch(l))
            .Select(l => (l, DeNs1EnElArranque().IsMatch(l) ? "NS-1" : DeNs2EnElArranque().IsMatch(l) ? "NS-2" : (string?)null))
            .ToList();

    [Fact]
    public void Declarar_enviar_y_entregar_cuesta_a_lo_sumo_60_lineas_de_pegamento_en_4_ficheros()
    {
        var arranque = DelArranque(Leer("Program.cs"));
        // El control: la medida ve las líneas del despacho; si no viera ninguna, «0 de pegamento» no diría nada.
        Assert.Contains(arranque, l => l.Dueno == "NS-1");
        Assert.Contains(arranque, l => l.Dueno == "NS-2");

        var sueltas = arranque.Where(l => l.Dueno is null).ToList();
        var pegamento = sueltas.Count + Lineas(Pegamento);
        var ficheros = Pegamento.Length + (sueltas.Count > 0 ? 1 : 0);
        salida.WriteLine(
            $"MEDIDA: definiciones {Lineas(Definiciones)} líneas en {Definiciones.Length} ficheros (no cuentan); "
            + $"NS-1 {Lineas(DeNs1)} líneas de pasos y {arranque.Count(l => l.Dueno == "NS-1")} del arranque; "
            + $"NS-2 {arranque.Count(l => l.Dueno == "NS-2")} del arranque (y su ruta en el motor); "
            + $"pegamento {pegamento} líneas en {ficheros} ficheros.");
        Assert.True(pegamento <= 60, $"El pegamento del despacho son {pegamento} líneas y la meta es ≤ 60: {string.Join(" | ", sueltas.Select(l => l.Linea))}");
        Assert.True(ficheros <= 4, $"El despacho tocó {ficheros} ficheros de pegamento y la meta es ≤ 4.");
    }

    [Fact]
    public void Una_linea_del_despacho_que_no_es_de_un_interino_cuenta_como_pegamento()
    {
        const string programa = """
            // Los pasos del despacho (interinos de NS-1).
            var envios = new EnviosDelDespacho();
            builder.AddOrquestadorDeclarado(vocabulario, typeof(Program).Assembly, [.. PasosDelDespacho.Propios(envios)]);
            envios.Conectar(app.Services.GetRequiredService<TiendaCapabilities>());
            app.MapLecturaDelEnvio();
            builder.Services.AddSingleton<ReglasDelDespacho>();
            app.MapTiendaEndpoints();
            """;

        Assert.Equal(["NS-1", "NS-1", "NS-1", "NS-2", null], DelArranque(programa).Select(l => l.Dueno));
    }
}
