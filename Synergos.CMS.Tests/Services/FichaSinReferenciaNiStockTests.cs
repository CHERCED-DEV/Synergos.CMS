using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Synergos.CMS.Application.Configuration;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// La ficha de un servicio de Booking no muestra la referencia ni la disponibilidad (016, D-016-C24). El sitio sale del
/// <c>siteRoot</c> ancestro de la ficha y se identifica por su Key; la Tienda sigue igual.
/// </summary>
public sealed class FichaSinReferenciaNiStockTests
{
    private static readonly Guid Booking = Guid.Parse("37bf2ce2-76e7-402e-b3b0-f2b93c447c86");

    private static string Raiz()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Synergos.CMS.sln"))) { dir = dir.Parent; }
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    [Theory]
    [InlineData("37bf2ce2-76e7-402e-b3b0-f2b93c447c86", false)]
    [InlineData("4f9c7b1a-0000-4000-8000-000000000001", true)]
    [InlineData(null, true)]
    public void Solo_el_sitio_declarado_oculta_la_referencia_y_el_stock(string? sitio, bool muestra)
    {
        var tienda = new TiendaSettings { SitiosSinReferenciaNiStock = [Booking] };

        Assert.Equal(muestra, tienda.MuestraReferenciaYStock(sitio is null ? null : Guid.Parse(sitio)));
    }

    [Fact]
    public void La_configuracion_declara_la_raiz_de_Booking_por_su_Key()
    {
        var web = Path.Combine(Raiz(), "Synergos.CMS.Web");
        var tienda = new ConfigurationBuilder().AddJsonFile(Path.Combine(web, "appsettings.json")).Build()
            .GetSection("Synergos:Tienda").Get<TiendaSettings>()!;

        Assert.Equal([Booking], tienda.SitiosSinReferenciaNiStock);
        var raiz = Directory.GetFiles(Path.Combine(web, "uSync", "v9", "Content"), "*.config")
            .Select(File.ReadAllText)
            .Single(x => x.Contains($"<Content Key=\"{Booking}\"", StringComparison.Ordinal));
        Assert.Contains("<ContentType>siteRoot</ContentType>", raiz, StringComparison.Ordinal);
        Assert.Matches(new Regex("Alias=\"Booking\""), raiz);
    }

    [Fact]
    public void La_ficha_toma_el_sitio_de_su_siteRoot_ancestro()
    {
        var vista = File.ReadAllText(Path.Combine(Raiz(), "Synergos.CMS.Web", "Views", "Partials", "Elements", "Shop", "_ProductDetailCore.cshtml"));

        Assert.Contains("TiendaOpt.Value.MuestraReferenciaYStock(product.AncestorOrSelf(\"siteRoot\")?.Key)", vista, StringComparison.Ordinal);
        Assert.Contains("@if (conReferenciaYStock && !string.IsNullOrWhiteSpace(sku))", vista, StringComparison.Ordinal);
        Assert.DoesNotContain(".Root()", vista, StringComparison.Ordinal);
    }
}
