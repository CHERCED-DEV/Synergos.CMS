namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Alquiler dice lo que sabe y como se lee (016, D-016-C25): sus textos no llevan plurales a medias («unidad(es)»,
/// «día(s)»), la tarjeta no presenta la flota como disponibilidad, y ningún equipo promete una certificación sin dato.
/// </summary>
public sealed class AlquilerDeEjemploTests
{
    private static string Usync()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Synergos.CMS.sln"))) { dir = dir.Parent; }
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "Synergos.CMS.Web", "uSync", "v9");
    }

    [Fact]
    public void Los_textos_de_Alquiler_no_llevan_plurales_a_medias_ni_la_flota()
    {
        var claves = Directory.GetFiles(Path.Combine(Usync(), "Dictionary"), "alquiler.*.config");

        Assert.NotEmpty(claves);
        foreach (var clave in claves)
        {
            var texto = File.ReadAllText(clave);
            foreach (var medio in new[] { "(s)", "(es)" })
            {
                Assert.False(texto.Contains(medio, StringComparison.Ordinal), $"{Path.GetFileName(clave)} dice «{medio}».");
            }
        }
        var tarjeta = File.ReadAllText(Path.Combine(Usync(), "Dictionary", "alquiler.catalog.units.config"));
        Assert.DoesNotContain("{unidades}", tarjeta, StringComparison.Ordinal);
    }

    [Fact]
    public void Ningun_equipo_promete_una_certificacion()
    {
        var equipos = Directory.GetFiles(Path.Combine(Usync(), "Content"), "*.config")
            .Select(File.ReadAllText)
            .Where(x => x.Contains("<ContentType>equipmentPage</ContentType>", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(equipos);
        Assert.All(equipos, x => Assert.DoesNotContain("certificad", x, StringComparison.OrdinalIgnoreCase));
    }
}
