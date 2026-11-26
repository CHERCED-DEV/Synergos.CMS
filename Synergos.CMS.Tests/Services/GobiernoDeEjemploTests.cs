using Synergos.CMS.Application.Services.Impl;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Nadie debe poder creer que tramita de verdad en el portal de Gobierno de la demostración (016, D-016-C25): no nombra
/// entidades reales, cada ventanilla dice que es de ejemplo, y los formularios piden sólo datos de prueba, sin nada
/// sensible en texto libre.
/// </summary>
public sealed class GobiernoDeEjemploTests
{
    /// <summary>Entidades y EPS reales que el portal nombraba.</summary>
    private static readonly string[] Reales =
    [
        "Registraduría", "Cancillería", "Secretaría de Movilidad", "Secretaría de Salud", "Alcaldía Municipal",
        "Cámara de Comercio", "Ministerio de Vivienda", "Nueva EPS", "Coosalud", "Mutual SER", "SISBÉN",
    ];

    /// <summary>Los campos que son datos personales: dicen que son de prueba.</summary>
    private static readonly HashSet<string> Personales =
        ["nombreCompleto", "cedula", "correo", "direccion", "razonSocial", "nit", "representante"];

    private static string Web()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Synergos.CMS.sln"))) { dir = dir.Parent; }
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "Synergos.CMS.Web");
    }

    [Fact]
    public async Task Cada_tramite_lo_atiende_una_ventanilla_de_ejemplo()
    {
        var catalogo = new StubTramiteCatalogProvider();
        var tramites = await catalogo.SearchAsync(null, null);

        Assert.NotEmpty(tramites);
        foreach (var t in tramites)
        {
            Assert.EndsWith("(ejemplo)", t.Agency, StringComparison.Ordinal);
            var detalle = (await catalogo.GetAsync(t.Id))!;
            var texto = string.Join(" ", new[] { t.Name, t.Summary, t.Agency, detalle.Description }
                .Concat(detalle.Eligibility).Concat(detalle.Required)
                .Concat(detalle.FormDefinition!.Sections.SelectMany(s => s.Fields)
                    .SelectMany(f => new[] { f.Label, f.Help }.Concat(f.Options.Select(o => o.Label)))));
            foreach (var real in Reales)
            {
                Assert.DoesNotContain(real, texto, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public async Task Los_formularios_piden_solo_datos_de_prueba_y_nada_sensible_en_texto_libre()
    {
        var catalogo = new StubTramiteCatalogProvider();
        foreach (var t in await catalogo.SearchAsync(null, null))
        {
            var campos = (await catalogo.GetAsync(t.Id))!.FormDefinition!.Sections.SelectMany(s => s.Fields).ToList();

            Assert.DoesNotContain(campos, f => f.Id is "fechaNacimiento" or "telefono");
            foreach (var f in campos.Where(f => Personales.Contains(f.Id)))
            {
                Assert.Contains("(de prueba)", f.Label, StringComparison.Ordinal);
            }
            foreach (var f in campos.Where(f => f.Id is "sisben" or "ingresos"))
            {
                Assert.Equal("select", f.Type);
                Assert.NotEmpty(f.Options);
            }
        }
    }

    [Fact]
    public void La_portada_de_Gobierno_no_nombra_entidades_reales()
    {
        var portada = File.ReadAllText(Path.Combine(Web(), "uSync", "v9", "Content", "gobierno.config"));

        foreach (var real in Reales)
        {
            Assert.DoesNotContain(real, portada, StringComparison.OrdinalIgnoreCase);
        }
        Assert.Contains("Ventanillas de ejemplo que atienden aquí", portada, StringComparison.Ordinal);
    }
}
