using NSubstitute;
using Synergos.CMS.Web.Services;
using Umbraco.Cms.Core.Configuration.Models;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Strings;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// La vitrina del hub enlaza <c>/synergos/apps/&lt;slug&gt;</c> para cada vertical y arma la página
/// de cada recorrido desde otra tabla. Hoteles estaba en la primera y no en la segunda: dos páginas
/// lo enlazaban a un 404 (Synergos.CMS#187).
/// </summary>
public sealed class VitrinaDeAppsTests
{
    [Fact]
    public void Cada_vertical_que_la_vitrina_enlaza_tiene_su_recorrido_y_ningun_recorrido_sobra()
    {
        var enlaza = DevContentFiller.SlugsQueLaVitrinaEnlaza;
        var recorre = DevContentFiller.SlugsQueLaVitrinaRecorre;

        // Dos conjuntos vacíos son iguales: sin este piso, un descubrimiento roto pasaría en verde.
        Assert.NotEmpty(enlaza);
        Assert.Equal(
            enlaza.Order(StringComparer.Ordinal),
            recorre.Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// Soluciones enlaza <c>/{slug}</c> para cada vertical con sitio, y el sitio se busca por esa
    /// RUTA. Buscaba por <c>brandKey</c>, y Tienda (<c>ecommerce</c>) y Booking (<c>meridian</c>)
    /// salían sin enlace: el lanzador listaba 7 de 9, medido en vivo (#188).
    /// </summary>
    [Fact]
    public void Cada_vertical_que_la_vitrina_enlaza_encuentra_su_sitio_por_la_ruta_y_no_por_la_marca()
    {
        // Los siteRoots como los crea SeedVerticalSiteRoots: nombre (que da la ruta) y marca.
        var sitios = new[]
        {
            Sitio("Blogs", "blogs"), Sitio("Tienda", "ecommerce"), Sitio("Healthcare", "healthcare"),
            Sitio("Educacion", "educacion"), Sitio("Booking", "meridian"), Sitio("Eventos", "eventos"),
            Sitio("Propiedades", "propiedades"), Sitio("Hoteles", "hoteles"), Sitio("Gobierno", "gobierno"),
        };
        var urls = new DefaultShortStringHelper(new DefaultShortStringHelperConfig().WithDefault(new RequestHandlerSettings()));

        var sinSitio = DevContentFiller.SlugsQueLaVitrinaEnlaza
            .Where(slug => DevContentFiller.SitioEnLaRuta(sitios, slug, urls) is null)
            .ToList();

        Assert.NotEmpty(DevContentFiller.SlugsQueLaVitrinaEnlaza);
        Assert.True(sinSitio.Count == 0,
            "Estos verticales tienen sitio y la vitrina no lo encuentra, así que Soluciones no los "
            + "enlaza: " + string.Join(", ", sinSitio));
    }

    [Fact]
    public void Un_vertical_sin_sitio_no_se_enlaza()
    {
        var urls = new DefaultShortStringHelper(new DefaultShortStringHelperConfig().WithDefault(new RequestHandlerSettings()));

        Assert.Null(DevContentFiller.SitioEnLaRuta(new[] { Sitio("Blogs", "blogs") }, "tienda", urls));
    }

    private static IContent Sitio(string nombre, string marca)
    {
        var tipo = Substitute.For<ISimpleContentType>();
        tipo.Alias.Returns("siteRoot");
        var sitio = Substitute.For<IContent>();
        sitio.ContentType.Returns(tipo);
        sitio.Name.Returns(nombre);
        sitio.GetCultureName(Arg.Any<string?>()).Returns(nombre);
        sitio.GetValue<string>("brandKey").Returns(marca);
        return sitio;
    }
}
