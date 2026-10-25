using Microsoft.Extensions.Options;
using NSubstitute;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Web.Services.Catalog;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.PublishedCache;
using Umbraco.Cms.Core.Web;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// «¿El catálogo ve ya esta ficha en la tienda?», preguntado sin log (004-tienda-compra; el rojo del CI de 2026-10-22):
/// por el MISMO recorrido que el catálogo, desde las raíces de la caché publicada hasta el <c>siteRoot</c> con el
/// <c>brandKey</c> de la tienda, y la ficha entre sus descendientes.
/// </summary>
public sealed class TiendaEnElContenidoTests
{
    private static readonly Guid Ficha = Guid.Parse("0b1c2d3e-4f50-6172-8394-a5b6c7d8e9f0");

    private readonly IPublishedContentCache _cache = Substitute.For<IPublishedContentCache>();

    private static IPublishedContent Nodo(int id, string alias, string? marca = null, Guid? llave = null, params IPublishedContent[] hijos)
    {
        var tipo = Substitute.For<IPublishedContentType>();
        tipo.Alias.Returns(alias);
        tipo.Variations.Returns(ContentVariation.Nothing);
        var nodo = Substitute.For<IPublishedContent>();
        nodo.Id.Returns(id);
        nodo.Key.Returns(llave ?? Guid.NewGuid());
        nodo.ContentType.Returns(tipo);
        nodo.ChildrenForAllCultures.Returns(hijos);
        if (marca is not null)
        {
            var propiedad = Substitute.For<IPublishedProperty>();
            propiedad.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(marca);
            nodo.GetProperty("brandKey").Returns(propiedad);
        }
        return nodo;
    }

    private static IPublishedContent LaFicha() => Nodo(1020, "productPage", llave: Ficha);

    private void EnLaRaiz(params IPublishedContent[] raices)
        => _cache.GetAtRoot(Arg.Any<string?>()).Returns(raices);

    private TiendaEnElContenido Montar(string? alcance = "ecommerce", bool conContexto = true)
    {
        var contexto = Substitute.For<IUmbracoContext>();
        contexto.Content.Returns(_cache);
        var accesor = Substitute.For<IUmbracoContextAccessor>();
        accesor.TryGetUmbracoContext(out Arg.Any<IUmbracoContext>()!).Returns(x =>
        {
            x[0] = conContexto ? contexto : null!;
            return conContexto;
        });
        var ajustes = Substitute.For<IOptionsMonitor<CatalogSettings>>();
        var scopes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (alcance is not null) scopes["Shop"] = alcance;
        ajustes.CurrentValue.Returns(new CatalogSettings { Scopes = scopes });
        return new TiendaEnElContenido(accesor, ajustes, Substitute.For<IVariationContextAccessor>());
    }

    /// <summary>La tienda (1000, <paramref name="marca"/>) en la raíz, una categoría (1010) y la ficha (1020) bajo ella.</summary>
    private void ConLaTienda(string marca = "ecommerce")
    {
        // Cada nodo se arma ANTES de su Returns: armar un sustituto dentro de otro Returns lo rompe.
        var ficha = LaFicha();
        var categoria = Nodo(1010, "productCategoryPage", hijos: ficha);
        var tienda = Nodo(1000, "siteRoot", marca, hijos: categoria);
        EnLaRaiz(tienda);
    }

    [Fact]
    public void Una_ficha_publicada_bajo_el_siteRoot_de_la_tienda_se_ve()
    {
        ConLaTienda();

        Assert.True(Montar().Ve(Ficha));
    }

    [Fact]
    public void La_tienda_se_encuentra_aunque_no_sea_una_raiz_y_su_marca_traiga_espacios_o_mayusculas()
    {
        var ficha = LaFicha();
        var tienda = Nodo(1000, "siteRoot", " Ecommerce ", hijos: ficha);
        var inicio = Nodo(900, "homePage", hijos: tienda);
        EnLaRaiz(inicio);

        Assert.True(Montar().Ve(Ficha));
    }

    [Fact]
    public void Si_el_recorrido_todavia_no_alcanza_la_tienda_no_se_ve_aunque_la_cache_la_tenga_por_id()
    {
        // El caso medido en el import del árbol entero: por id la caché ya tiene la ficha y su siteRoot,
        // y el recorrido desde las raíces, que es el del catálogo, todavía no.
        var ficha = LaFicha();
        var tienda = Nodo(1000, "siteRoot", "ecommerce", hijos: ficha);
        _cache.GetById(Ficha).Returns(ficha);
        _cache.GetById(1000).Returns(tienda);
        EnLaRaiz();

        Assert.False(Montar().Ve(Ficha));
    }

    [Fact]
    public void Si_la_tienda_se_ve_y_la_ficha_todavia_no_esta_entre_sus_descendientes_no_se_ve()
    {
        var ficha = LaFicha();
        var categoria = Nodo(1010, "productCategoryPage");
        var tienda = Nodo(1000, "siteRoot", "ecommerce", hijos: categoria);
        _cache.GetById(Ficha).Returns(ficha);
        EnLaRaiz(tienda);

        Assert.False(Montar().Ve(Ficha));
    }

    [Fact]
    public void Una_ficha_bajo_el_siteRoot_de_otra_marca_no_es_de_la_tienda()
    {
        ConLaTienda(marca: "propiedades");

        Assert.False(Montar().Ve(Ficha));
    }

    [Fact]
    public void Una_ficha_de_otro_sitio_no_es_de_la_tienda_aunque_la_tienda_se_vea()
    {
        var ficha = LaFicha();
        var otroSitio = Nodo(2000, "siteRoot", "propiedades", hijos: ficha);
        var tienda = Nodo(1000, "siteRoot", "ecommerce", hijos: Nodo(1030, "productPage"));
        EnLaRaiz(otroSitio, tienda);

        Assert.False(Montar().Ve(Ficha));
    }

    /// <summary>Una ficha con su SKU y, si se da, la marca reservable de 011 (<c>compReservable</c>).</summary>
    private static IPublishedContent FichaConSku(int id, string sku, object? reservable)
    {
        var ficha = Nodo(id, "productPage");
        var propiedadSku = Substitute.For<IPublishedProperty>();
        propiedadSku.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(sku);
        ficha.GetProperty("productSku").Returns(propiedadSku);
        if (reservable is not null)
        {
            var marca = Substitute.For<IPublishedProperty>();
            marca.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(reservable);
            ficha.GetProperty("reservable").Returns(marca);
        }
        return ficha;
    }

    [Fact]
    public void Los_SKU_reservables_son_los_de_las_fichas_marcadas_de_la_tienda_y_ninguno_de_otro_sitio()
    {
        // FR-026: un servicio por cita no se republica como producto. La marca se lee con los valores que acepta 011.
        var cita = FichaConSku(1101, "SVC-CITA-30", true);
        var taza = FichaConSku(1102, "TAZA-001", false);
        var vieja = FichaConSku(1103, "SVC-VIEJA", "1");
        var plana = FichaConSku(1104, "LIBRETA-001", null);
        var deOtroSitio = FichaConSku(2101, "SVC-ASESORIA-30", true);
        var categoria = Nodo(1010, "productCategoryPage", hijos: [cita, taza, vieja, plana]);
        var tienda = Nodo(1000, "siteRoot", "ecommerce", hijos: categoria);
        var otroSitio = Nodo(2000, "siteRoot", "propiedades", hijos: deOtroSitio);
        EnLaRaiz(otroSitio, tienda);

        Assert.Equal(new[] { "SVC-CITA-30", "SVC-VIEJA" }, Montar().SkusReservables().Order(StringComparer.Ordinal));
        Assert.Empty(Montar(alcance: null).SkusReservables());
    }

    [Fact]
    public void Sin_el_alcance_de_la_tienda_o_sin_contexto_no_se_ve_nada()
    {
        ConLaTienda();

        Assert.False(Montar(alcance: null).Ve(Ficha));
        Assert.False(Montar(conContexto: false).Ve(Ficha));
    }
}
