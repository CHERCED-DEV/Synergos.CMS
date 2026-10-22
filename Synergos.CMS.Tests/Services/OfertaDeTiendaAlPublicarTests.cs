using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Notifications;
using Synergos.CMS.Web.Services.Catalog;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Notifications;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Publicar un <c>productPage</c> publica su oferta en el orquestador de la tienda, con el catálogo en el
/// contenido, y nada de eso tumba el publicar del editor (004-tienda-compra, R-9).
/// </summary>
public sealed class OfertaDeTiendaAlPublicarTests
{
    private readonly IProductCatalogProvider _catalogo = Substitute.For<IProductCatalogProvider>();
    private readonly ITiendaOfferPublisher _ofertas = Substitute.For<ITiendaOfferPublisher>();
    private readonly ITiendaEnElContenido _tienda = Substitute.For<ITiendaEnElContenido>();

    public OfertaDeTiendaAlPublicarTests() => _tienda.Ve(Arg.Any<Guid>()).Returns(true);

    private OfertaDeTiendaAlPublicar Montar(string fuente)
    {
        var fuentes = Substitute.For<IOptionsMonitor<CatalogSettings>>();
        fuentes.CurrentValue.Returns(new CatalogSettings
        {
            Sources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Shop"] = fuente },
        });
        return new OfertaDeTiendaAlPublicar(_catalogo, _ofertas, fuentes, _tienda, NullLogger<OfertaDeTiendaAlPublicar>.Instance);
    }

    internal static IContent Nodo(string alias, string? sku, int id = 0)
    {
        var tipo = Substitute.For<ISimpleContentType>();
        tipo.Alias.Returns(alias);
        var nodo = Substitute.For<IContent>();
        nodo.Id.Returns(id);
        nodo.Key.Returns(Guid.NewGuid());
        nodo.ContentType.Returns(tipo);
        nodo.GetValue<string>("productSku", Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<bool>()).Returns(sku);
        return nodo;
    }

    private static ContentPublishedNotification Publicados(params IContent[] nodos) => new(nodos, new EventMessages());

    [Fact]
    public async Task Con_el_catalogo_en_el_contenido_publica_la_oferta_del_producto_tal_como_lo_sirve_el_catalogo()
    {
        var taza = HttpTiendaOfferPublisherTests.Taza();
        _catalogo.GetProductAsync("TAZA-001", Arg.Any<CancellationToken>()).Returns(taza);

        await Montar("cms").HandleAsync(Publicados(Nodo("productPage", "TAZA-001"), Nodo("textPage", "OTRA")), default);

        await _ofertas.Received(1).PublishAsync(taza, Arg.Any<CancellationToken>());
        await _catalogo.DidNotReceive().GetProductAsync("OTRA", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Con_la_demo_un_productPage_no_se_vende_y_no_publica_nada()
    {
        _catalogo.GetProductAsync("TAZA-001", Arg.Any<CancellationToken>()).Returns(HttpTiendaOfferPublisherTests.Taza());

        await Montar("demo").HandleAsync(Publicados(Nodo("productPage", "TAZA-001")), default);

        await _ofertas.DidNotReceiveWithAnyArgs().PublishAsync(default!, default);
    }

    [Fact]
    public async Task Un_producto_que_el_catalogo_no_sirve_no_tiene_oferta_y_la_que_tuviera_se_retira()
    {
        _catalogo.GetProductAsync("SIN-PRECIO", Arg.Any<CancellationToken>()).Returns((ProductDetail?)null);

        await Montar("cms").HandleAsync(Publicados(Nodo("productPage", "SIN-PRECIO"), Nodo("productPage", null)), default);

        await _ofertas.DidNotReceiveWithAnyArgs().PublishAsync(default!, default);
        await _ofertas.Received(1).RetireAsync("SIN-PRECIO", Arg.Any<CancellationToken>());
        await _ofertas.Received(1).RetireAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// El rojo del CI (2026-10-22): al importar el árbol entero desde una base vacía, cada ficha se publicaba antes de
    /// que el catálogo alcanzara el <c>siteRoot</c> de la tienda; el catálogo dejaba un ERR por ficha y el manejador
    /// RETIRABA su oferta. Si el catálogo todavía no ve la ficha en la tienda, no se le pregunta y no se toca nada.
    /// </summary>
    [Fact]
    public async Task Si_el_catalogo_todavia_no_ve_la_tienda_no_retira_ni_le_pregunta_al_catalogo()
    {
        var ficha = Nodo("productPage", "TAZA-001");
        _tienda.Ve(ficha.Key).Returns(false);
        _catalogo.GetProductAsync("TAZA-001", Arg.Any<CancellationToken>()).Returns((ProductDetail?)null);

        await Montar("cms").HandleAsync(Publicados(ficha), default);

        await _catalogo.DidNotReceiveWithAnyArgs().GetProductAsync(default!, default);
        await _ofertas.DidNotReceiveWithAnyArgs().RetireAsync(default!, default);
        await _ofertas.DidNotReceiveWithAnyArgs().PublishAsync(default!, default);
    }

    [Fact]
    public async Task Un_fallo_al_leer_o_al_publicar_no_tumba_el_publicar_del_editor_ni_a_los_demas_productos()
    {
        var taza = HttpTiendaOfferPublisherTests.Taza();
        var camiseta = HttpTiendaOfferPublisherTests.Camiseta();
        _catalogo.GetProductAsync("ROTO", Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("caché"));
        _catalogo.GetProductAsync("TAZA-001", Arg.Any<CancellationToken>()).Returns(taza);
        _catalogo.GetProductAsync("CAMI-001", Arg.Any<CancellationToken>()).Returns(camiseta);
        _ofertas.PublishAsync(taza, Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("al armar el cuerpo"));

        await Montar("cms").HandleAsync(
            Publicados(Nodo("productPage", "ROTO"), Nodo("productPage", "TAZA-001"), Nodo("productPage", "CAMI-001")), default);

        await _ofertas.Received(1).PublishAsync(camiseta, Arg.Any<CancellationToken>());
    }
}
