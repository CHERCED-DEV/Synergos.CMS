using Synergos.Bff.Core.Flow;
using Synergos.CMS.Tests.Architecture;

namespace Synergos.CMS.Tests.Bff.Gobierno;

/// <summary>
/// El cruce de los actos del trámite con su proceso (009-gobierno-tramite T036, la parte del vertical de NG-4): cada flujo
/// de <c>Bff.Gobierno</c> que dispara una transición lo hace con el rol del acceso de su operación, sobre el proceso que
/// arranca la radicación, y entre todos cubren las transiciones de la v2.
/// </summary>
/// <remarks>
/// <para><b>Lo genérico ya lo vigila el núcleo</b> (lote 7): que el proceso exista en <c>tools/provisionar.procesos.json</c>,
/// que la transición esté en él y que el rol declarado sea uno de los que exige, al arrancar y en
/// <c>ProcesosDeLosFlujosTests</c>. Lo que el núcleo no puede saber es qué rol corresponde a cada acceso: en el trámite, el
/// personal actúa como <c>funcionario</c> y el dueño como <c>ciudadano</c>. Sin este cruce, un flujo del personal que
/// disparara «resubmit» como «ciudadano» pasaría el del núcleo, y un funcionario subsanaría en nombre del ciudadano.</para>
/// </remarks>
public sealed class CruceFlujoProcesoTests
{
    private static readonly IReadOnlyDictionary<bool, string> RolDelAcceso = new Dictionary<bool, string>
    {
        [true] = "funcionario",
        [false] = "ciudadano",
    };

    private static IReadOnlyList<FlujoDef> FlujosDeGobierno()
        => Directory.EnumerateFiles(Path.Combine(Proyectos.Dir("Synergos.Bff.Gobierno"), "flujos"), "*.json")
            .Order(StringComparer.Ordinal)
            .Select(f => FlujoDef.Leer(File.ReadAllText(f)))
            .ToList();

    private static IEnumerable<(FlujoDef Flujo, PasoDef Paso)> Disparos(IEnumerable<FlujoDef> flujos)
        => flujos.SelectMany(f => f.Pasos.Values
            .Where(p => p.Proceso is not null && (p.Transicion is not null || p.Transiciones is not null))
            .Select(p => (f, p)));

    private static IEnumerable<string> Nombradas(PasoDef paso) => paso.Transiciones ?? [paso.Transicion!];

    [Fact]
    public void Cada_acto_dispara_con_el_rol_de_su_acceso()
    {
        var disparos = Disparos(FlujosDeGobierno()).ToList();
        Assert.True(disparos.Count >= 3, $"Se vieron {disparos.Count} pasos que disparan: el descubrimiento dejó de ver.");

        var malos = disparos
            .Where(d => !string.Equals(d.Paso.Rol, RolDelAcceso[d.Flujo.EsDePersonal], StringComparison.Ordinal))
            .Select(d => $"«{d.Flujo.Clave}» ({(d.Flujo.EsDePersonal ? "del personal" : "del dueño")}): el paso «{d.Paso.Id}» dispara "
                + $"{string.Join(", ", Nombradas(d.Paso))} como «{d.Paso.Rol}», y su acceso actúa como «{RolDelAcceso[d.Flujo.EsDePersonal]}».")
            .ToList();
        Assert.True(malos.Count == 0, string.Join(Environment.NewLine, malos));
    }

    [Fact]
    public void Los_actos_actuan_sobre_el_proceso_que_arranca_la_radicacion_y_cubren_sus_transiciones()
    {
        var flujos = FlujosDeGobierno();
        var arranque = Assert.Single(flujos.Single(f => f.Clave == "gobierno.radicacion").Pasos.Values, p => p.Tipo == "workflow.arrancar");
        var catalogo = CatalogoDeProcesos.Leer(File.ReadAllText(RadicacionReal.Procesos));
        var disparos = Disparos(flujos).ToList();

        Assert.All(disparos, d => Assert.True(d.Paso.Proceso == arranque.Proceso,
            $"«{d.Flujo.Clave}» actúa sobre «{d.Paso.Proceso}» y la radicación arranca «{arranque.Proceso}»: la capacidad lo rechazaría (flow.proceso_ajeno)."));
        Assert.All(disparos, d => Assert.Equal("gobierno.radicacion", d.Flujo.PosteriorA));
        Assert.Contains(arranque.Proceso!, catalogo.Claves);

        var delProceso = ProcesoDelCatalogo(arranque.Proceso!);
        Assert.Equal(delProceso.Order(StringComparer.Ordinal),
            disparos.SelectMany(d => Nombradas(d.Paso)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
    }

    /// <summary>Las transiciones de un proceso, leídas del catálogo de la provisión.</summary>
    private static IReadOnlyList<string> ProcesoDelCatalogo(string clave)
    {
        using var catalogo = System.Text.Json.JsonDocument.Parse(File.ReadAllText(RadicacionReal.Procesos));
        return catalogo.RootElement.EnumerateArray()
            .Single(d => d.TryGetProperty("key", out var k) && k.GetString() == clave)
            .GetProperty("transitions").EnumerateArray()
            .Select(t => t.GetProperty("name").GetString()!)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }
}
