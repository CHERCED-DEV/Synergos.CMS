using NSubstitute;
using Synergos.CMS.Interfaces.SynHost;
using Synergos.CMS.Web.Services.SynHost;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models.Blocks;
using Umbraco.Cms.Core.Models.PublishedContent;

namespace Synergos.CMS.Tests.Services.SynHost;

/// <summary>
/// Cubre <see cref="RutasDeLasVistas"/> (014-despiece-tienda, I3): en qué página del sitio vive cada vista de una app
/// lo dice cómo está compuesto el sitio —la página que coloca el bloque con esa <c>vista</c>—, no una ruta escrita en
/// el bloque ni en el código.
/// </summary>
public sealed class RutasDeLasVistasTests
{
    private const string Tienda = "elementSynStorefront";

    [Fact]
    public void Una_pagina_por_vista_con_el_inicio_para_el_bloque_sin_vista()
    {
        var paginas = new[]
        {
            Pagina("/tienda/", Bloque(Tienda)),
            Pagina("/tienda/catalogo/", Bloque(Tienda, "catalogo")),
            Pagina("/tienda/carrito/", Bloque(Tienda, " carrito ")),
        };

        Assert.Equal(
            [new RutaDeUnaVista("inicio", "/tienda/"), new RutaDeUnaVista("catalogo", "/tienda/catalogo/"), new RutaDeUnaVista("carrito", "/tienda/carrito/")],
            Rutas(paginas));
    }

    [Fact]
    public void Dos_paginas_con_la_misma_vista_enlazan_la_primera_del_arbol()
    {
        var paginas = new[]
        {
            Pagina("/tienda/carrito/", Bloque(Tienda, "carrito")),
            Pagina("/tienda/borrador-del-carrito/", Bloque(Tienda, "carrito")),
        };

        Assert.Equal([new RutaDeUnaVista("carrito", "/tienda/carrito/")], Rutas(paginas));
    }

    [Fact]
    public void Una_pagina_sin_ruta_no_enlaza_y_deja_pasar_a_la_siguiente()
    {
        var paginas = new[]
        {
            Pagina("#", Bloque(Tienda, "pago")),
            Pagina("/tienda/pago/", Bloque(Tienda, "pago")),
        };

        Assert.Equal([new RutaDeUnaVista("pago", "/tienda/pago/")], Rutas(paginas));
    }

    [Fact]
    public void Los_bloques_de_otra_app_y_las_paginas_sin_bloques_no_cuentan()
    {
        var paginas = new[]
        {
            Pagina("/tienda/vender/", Bloque("elementSynSeller", "panel")),
            Pagina("/tienda/sobre-nosotros/"),
            Pagina("/tienda/cuenta/", Bloque("elementCompHero"), Bloque(Tienda, "cuenta")),
        };

        Assert.Equal([new RutaDeUnaVista("cuenta", "/tienda/cuenta/")], Rutas(paginas));
    }

    /// <summary>
    /// Una página que acota su vista (012-blogs-social, I6) no es la página de esa vista, aunque vaya primero en el árbol:
    /// «Ofertas» no se lleva el enlace del catálogo, ni «#arquitectura» el de Explorar. Si es la única de su vista, la
    /// vista no tiene página.
    /// </summary>
    [Fact]
    public void Una_pagina_que_acota_su_vista_no_se_lleva_su_enlace()
    {
        var paginas = new[]
        {
            Pagina("/tienda/ofertas/", Bloque(Tienda, "catalogo", seleccion: new[] { "ofertas" })),
            Pagina("/tienda/catalogo/", Bloque(Tienda, "catalogo", seleccion: Array.Empty<string>())),
            Pagina("/tienda/arquitectura/", Bloque(Tienda, "ficha", seleccion: " arquitectura ")),
            Pagina("/tienda/carrito/", Bloque(Tienda, "carrito", seleccion: "  ")),
        };

        Assert.Equal(
            [new RutaDeUnaVista("catalogo", "/tienda/catalogo/"), new RutaDeUnaVista("carrito", "/tienda/carrito/")],
            Rutas(paginas));
    }

    [Fact]
    public void Un_desplegable_multiple_toma_su_primera_vista()
    {
        var bloque = Elemento(Tienda, new[] { "ficha", "carrito" });

        Assert.Equal("ficha", RutasDeLasVistas.VistaDe(bloque));
        Assert.Null(RutasDeLasVistas.VistaDe(Elemento(Tienda, "  ")));
    }

    private static IReadOnlyList<RutaDeUnaVista> Rutas(IEnumerable<IPublishedContent> paginas)
        => RutasDeLasVistas.Rutas(paginas, Tienda, "inicio", p => p.Name);

    /// <summary>Una página cuya «URL» es su nombre, con sus bloques en una rejilla.</summary>
    private static IPublishedContent Pagina(string url, params BlockGridItem[] bloques)
    {
        var pagina = Substitute.For<IPublishedContent>();
        pagina.Name.Returns(url);
        var rejilla = Substitute.For<IPublishedProperty>();
        rejilla.PropertyType.EditorAlias.Returns(Constants.PropertyEditors.Aliases.BlockGrid);
        rejilla.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(new BlockGridModel(bloques.ToList(), 12));
        pagina.Properties.Returns(bloques.Length > 0 ? new[] { rejilla } : Array.Empty<IPublishedProperty>());
        return pagina;
    }

    private static BlockGridItem Bloque(string alias, object? vista = null, object? seleccion = null)
        => new(new GuidUdi(Constants.UdiEntityType.Element, Guid.NewGuid()), Elemento(alias, vista, seleccion), null!, null!);

    private static IPublishedElement Elemento(string alias, object? vista = null, object? seleccion = null)
    {
        var elemento = Substitute.For<IPublishedElement>();
        elemento.ContentType.Alias.Returns(alias);
        // Cada propiedad se arma antes de su Returns: configurar un sustituto dentro de otro Returns lo rompe.
        var deLaVista = Propiedad(vista);
        var deLaSeleccion = Propiedad(seleccion);
        elemento.GetProperty(RutasDeLasVistas.AliasDeLaVista).Returns(deLaVista);
        elemento.GetProperty(RutasDeLasVistas.AliasDeLaSeleccion).Returns(deLaSeleccion);
        return elemento;
    }

    /// <summary>Una propiedad con ese valor, o ninguna: una interfaz sin configurar no es null en NSubstitute.</summary>
    private static IPublishedProperty? Propiedad(object? valor)
    {
        if (valor is null)
        {
            return null;
        }

        var propiedad = Substitute.For<IPublishedProperty>();
        propiedad.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(valor);
        return propiedad;
    }
}
