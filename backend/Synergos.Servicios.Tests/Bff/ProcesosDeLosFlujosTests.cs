using Synergos.Bff.Core.Flow;
using Synergos.CMS.Tests.Architecture;

namespace Synergos.CMS.Tests.Bff;

/// <summary>
/// El gate de repositorio del lote 7 (NG-4 de 009): todo paso de procesos de un flujo de cualquier orquestador cruza con
/// la definición del proceso versionada en <c>tools/provisionar.procesos.json</c> —el proceso existe, la transición está
/// en él y el rol que declara es uno de los que exige—, la misma regla que el validador aplica al arrancar.
/// </summary>
/// <remarks>
/// <para><b>Hoy ningún flujo de un orquestador usa procesos</b>: los usará el trámite de 009 por la puerta (fase C). Por
/// eso el gate mira que vea lo que tiene que ver —el catálogo con sus procesos y los flujos de todos los orquestadores— y
/// prueba la regla con la definición real.</para>
/// </remarks>
public sealed class ProcesosDeLosFlujosTests
{
    private static string Raiz()
    {
        var dir = new DirectoryInfo(Proyectos.Dir("Synergos.Servicios.Tests"));
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Synergos.CMS.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static CatalogoDeProcesos DelRepositorio()
        => CatalogoDeProcesos.Leer(File.ReadAllText(Path.Combine(Raiz(), "tools", "provisionar.procesos.json")));

    private static IReadOnlyList<(string Fuente, FlujoDef Flujo)> FlujosDeLosOrquestadores()
        => Directory.EnumerateFiles(Path.Combine(Raiz(), "backend", "orquestadores"), "*.json", SearchOption.AllDirectories)
            .Where(f => Path.GetFileName(Path.GetDirectoryName(f)) == "flujos" && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .Select(f => (Path.GetRelativePath(Raiz(), f), FlujoDef.Leer(File.ReadAllText(f))))
            .ToList();

    [Fact]
    public void Cada_paso_de_procesos_de_un_orquestador_cruza_con_la_definicion_versionada()
    {
        var catalogo = DelRepositorio();
        var flujos = FlujosDeLosOrquestadores();

        Assert.True(catalogo.Claves.Count >= 2, $"El catálogo del repositorio trae {catalogo.Claves.Count} procesos: el lector dejó de ver.");
        Assert.True(flujos.Count >= 9, $"Se leyeron {flujos.Count} flujos de los orquestadores: el descubrimiento dejó de ver.");

        var malos = flujos
            .SelectMany(f => f.Flujo.Pasos.Values.SelectMany(p => catalogo.Revisar(p)).Select(e => $"{f.Fuente}: {e}"))
            .ToList();
        Assert.True(malos.Count == 0, string.Join(Environment.NewLine, malos));
    }

    [Fact]
    public void La_regla_con_el_tramite_real_de_009()
    {
        var catalogo = DelRepositorio();
        PasoDef Paso(string proceso, string? transicion = null, IReadOnlyList<string>? transiciones = null, string? rol = null)
            => new("p", "workflow.disparar", [], [], null, null, null, null, null,
                Proceso: proceso, Transicion: transicion, Transiciones: transiciones, Rol: rol);

        Assert.Empty(catalogo.Revisar(Paso("gov.tramite.v2", "take", rol: "funcionario")));
        Assert.Empty(catalogo.Revisar(Paso("gov.tramite.v2", transiciones: ["approve", "reject", "request-info"], rol: "funcionario")));
        Assert.Empty(catalogo.Revisar(Paso("gov.tramite.v2", "resubmit", rol: "ciudadano")));

        Assert.Contains("que no está en el catálogo de procesos", Assert.Single(catalogo.Revisar(Paso("gov.tramite.v3", "take", rol: "funcionario"))), StringComparison.Ordinal);
        Assert.Contains("dispara «take», y «gov.tramite» no la tiene", Assert.Single(catalogo.Revisar(Paso("gov.tramite", "take", rol: "funcionario"))), StringComparison.Ordinal);
        Assert.Contains("dispara «resubmit» como «funcionario»", Assert.Single(catalogo.Revisar(Paso("gov.tramite.v2", "resubmit", rol: "funcionario"))), StringComparison.Ordinal);
        Assert.Contains("dispara «approve» como «ciudadano»", Assert.Single(
            catalogo.Revisar(Paso("gov.tramite.v2", transiciones: ["approve", "resubmit"], rol: "ciudadano"))), StringComparison.Ordinal);
    }
}
