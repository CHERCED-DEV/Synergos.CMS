using NSubstitute;
using Synergos.CMS.Web.Services;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models.Blocks;
using Umbraco.Cms.Core.Models.PublishedContent;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Cubre <see cref="PaginaQueColoca"/>: el botón del carrito enlaza la página que el editor
/// compuso con el resumen del carrito, no <c>/tienda/carrito</c> escrito a mano, que no existió
/// nunca (Synergos.CMS#187).
/// </summary>
public sealed class PaginaQueColocaTests
{
    private const string Carrito = "elementShopCartSummary";

    [Fact]
    public void Sin_ninguna_pagina_que_lo_coloque_no_hay_pagina()
    {
        var paginas = new[]
        {
            Pagina(1),
            Pagina(2, Rejilla(Bloque("elementCompHero"))),
        };

        Assert.Null(PaginaQueColoca.Primera(paginas, Carrito));
        Assert.Null(PaginaQueColoca.Primera(Array.Empty<IPublishedContent>(), Carrito));
    }

    [Fact]
    public void La_encuentra_dentro_del_area_de_una_seccion()
    {
        // Así coloca un editor: una sección del Layout Composer, y el bloque en su área.
        var seccion = Bloque("elementLayoutSection", Bloque("elementCompHero"), Bloque(Carrito));
        var paginas = new[] { Pagina(1, Rejilla(Bloque("elementCompHero"))), Pagina(2, Rejilla(seccion)) };

        Assert.Equal(2, PaginaQueColoca.Primera(paginas, Carrito)!.Id);
    }

    [Fact]
    public void Solo_mira_BlockGrid_y_BlockList_y_el_alias_exacto()
    {
        var conLista = Pagina(1, Propiedad(Constants.PropertyEditors.Aliases.BlockList,
            new BlockListModel(new List<BlockListItem> { new(Udi(), Elemento(Carrito), null!, null!) })));
        // Otro editor que casualmente devuelve una rejilla no se convierte ni se mira.
        var otroEditor = Substitute.For<IPublishedProperty>();
        otroEditor.PropertyType.EditorAlias.Returns("Umbraco.RichText");
        var rejillaConElCarrito = new BlockGridModel(new List<BlockGridItem> { Bloque(Carrito) }, 12);
        otroEditor.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(rejillaConElCarrito);
        var conTexto = Pagina(2, otroEditor);
        var parecido = Pagina(3, Rejilla(Bloque("elementShopCartSummaryV2"), Bloque("ElementShopCartSummary")));

        Assert.True(PaginaQueColoca.Coloca(conLista, Carrito));
        Assert.False(PaginaQueColoca.Coloca(conTexto, Carrito));
        otroEditor.DidNotReceiveWithAnyArgs().GetValue();
        Assert.False(PaginaQueColoca.Coloca(parecido, Carrito));
    }

    [Fact]
    public void Preguntar_dos_veces_da_la_misma_pagina_la_primera_en_orden()
    {
        var paginas = new[] { Pagina(7, Rejilla(Bloque(Carrito))), Pagina(9, Rejilla(Bloque(Carrito))) };

        Assert.Equal(7, PaginaQueColoca.Primera(paginas, Carrito)!.Id);
        Assert.Equal(7, PaginaQueColoca.Primera(paginas, Carrito)!.Id);
    }

    private static IPublishedContent Pagina(int id, params IPublishedProperty[] propiedades)
    {
        var pagina = Substitute.For<IPublishedContent>();
        pagina.Id.Returns(id);
        pagina.Properties.Returns(propiedades);
        return pagina;
    }

    private static IPublishedProperty Rejilla(params BlockGridItem[] bloques)
        => Propiedad(Constants.PropertyEditors.Aliases.BlockGrid, new BlockGridModel(bloques.ToList(), 12));

    private static IPublishedProperty Propiedad(string editor, object valor)
    {
        var propiedad = Substitute.For<IPublishedProperty>();
        propiedad.PropertyType.EditorAlias.Returns(editor);
        propiedad.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(valor);
        return propiedad;
    }

    private static BlockGridItem Bloque(string alias, params BlockGridItem[] enSuArea)
    {
        var bloque = new BlockGridItem(Udi(), Elemento(alias), null!, null!);
        if (enSuArea.Length > 0)
        {
            bloque.Areas = new[] { new BlockGridArea(enSuArea.ToList(), "content", 1, 12) };
        }

        return bloque;
    }

    private static IPublishedElement Elemento(string alias)
    {
        var elemento = Substitute.For<IPublishedElement>();
        elemento.ContentType.Alias.Returns(alias);
        return elemento;
    }

    private static GuidUdi Udi() => new(Constants.UdiEntityType.Element, Guid.NewGuid());
}
