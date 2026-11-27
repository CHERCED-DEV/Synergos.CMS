namespace Synergos.CMS.Tests.Services;

/// <summary>
/// «Equipo» de Healthcare (D-003-V2): el relleno de desarrollo compone el directorio del catálogo de Salud, como la
/// página real, y no tres médicos fijos con trayectorias que nada respalda.
/// </summary>
public sealed class EquipoDeSaludTests
{
    private static string Raiz()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Synergos.CMS.sln"))) { dir = dir.Parent; }
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    [Fact]
    public void El_relleno_de_Equipo_compone_el_catalogo_de_Salud()
    {
        var fuente = File.ReadAllText(Path.Combine(Raiz(), "Synergos.CMS.Web", "Services", "DevContentFiller.cs"));
        var inicio = fuente.IndexOf("private string BuildHealthcareEquipo()", StringComparison.Ordinal);
        var fin = fuente.IndexOf("private string BuildHealthcareHome()", inicio, StringComparison.Ordinal);
        Assert.True(inicio >= 0 && fin > inicio);
        var metodo = fuente[inicio..fin];

        Assert.Contains("AddCatalogList(b, \"salud\"", metodo, StringComparison.Ordinal);
        Assert.DoesNotContain("FeatureGridAuto", metodo, StringComparison.Ordinal);
        Assert.DoesNotContain("Más de 10 años", metodo, StringComparison.Ordinal);
    }
}
