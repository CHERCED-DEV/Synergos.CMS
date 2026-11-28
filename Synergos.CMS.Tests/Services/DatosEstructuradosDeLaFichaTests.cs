namespace Synergos.CMS.Tests.Services;

/// <summary>
/// El JSON-LD de una ficha dice la moneda que la ficha muestra (D-016-Z3): la de <c>CartSettings.Currency</c>, igual que
/// <c>_ProductDetailCore</c>. Antes caía a «USD» y anunciaba en dólares un precio en pesos.
/// </summary>
public sealed class DatosEstructuradosDeLaFichaTests
{
    [Fact]
    public void La_moneda_del_JSON_LD_es_la_que_muestra_la_ficha()
    {
        var vista = File.ReadAllText(Path.Combine(RepoRoot(), "Synergos.CMS.Web", "Views", "Shared", "_SeoStructuredData.cshtml"));

        Assert.Contains("CartSettingsOpt.Value.Currency", vista, StringComparison.Ordinal);
        Assert.DoesNotContain("\"USD\"", vista, StringComparison.Ordinal);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Synergos.CMS.sln")))
        {
            dir = dir.Parent;
        }
        return dir!.FullName;
    }
}
