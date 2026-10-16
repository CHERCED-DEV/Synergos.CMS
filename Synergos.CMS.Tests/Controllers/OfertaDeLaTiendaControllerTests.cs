using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Tests.Services;
using Synergos.CMS.Web.Controllers;

namespace Synergos.CMS.Tests.Controllers;

/// <summary>
/// Republicar la oferta del catálogo entero de la tienda (004-tienda-compra, R-9): sólo el personal, y recorre
/// TODO el catálogo aunque venga en páginas.
/// </summary>
public sealed class OfertaDeLaTiendaControllerTests
{
    private readonly IProductCatalogProvider _catalogo = Substitute.For<IProductCatalogProvider>();
    private readonly ITiendaOfferPublisher _ofertas = Substitute.For<ITiendaOfferPublisher>();

    private OfertaDeLaTiendaController Montar(bool sesion = true, string? rol = "admin")
    {
        var gate = Substitute.For<IMemberAccessGate>();
        gate.IsAuthenticated.Returns(sesion);
        gate.HasAnyRole(Arg.Any<string>()).Returns(c => rol is not null && c.Arg<string>().Split(',').Contains(rol));
        return new OfertaDeLaTiendaController(_catalogo, _ofertas, gate, NullLogger<OfertaDeLaTiendaController>.Instance);
    }

    private static CatalogProductSummary Resumen(string id) => new(id, id, 1m, "COP", "", "", null, 0d, 0, 1);

    /// <summary>Un catálogo de <paramref name="total"/> productos que contesta de a <paramref name="tope"/>, pida lo que pida.</summary>
    private void Catalogo(int total, int tope)
    {
        var todos = Enumerable.Range(1, total).Select(i => Resumen($"P-{i:000}")).ToList();
        _catalogo.SearchAsync(Arg.Any<ProductQuery>(), Arg.Any<CancellationToken>()).Returns(c =>
        {
            var q = c.Arg<ProductQuery>();
            return new ProductSearchResult(todos.Skip(q.Skip).Take(Math.Min(q.Take, tope)).ToList(), [], total);
        });
        _catalogo.GetProductAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(c =>
        {
            var taza = HttpTiendaOfferPublisherTests.Taza();
            return (ProductDetail?)(taza with { Product = taza.Product with { Id = c.Arg<string>() } });
        });
    }

    [Fact]
    public async Task Sin_sesion_es_401_y_sin_ser_personal_es_403_y_no_publica_nada()
    {
        Catalogo(3, 48);

        Assert.IsType<UnauthorizedObjectResult>(await Montar(sesion: false).Republicar(default));
        var prohibido = Assert.IsType<ObjectResult>(await Montar(rol: "comprador").Republicar(default));
        Assert.Equal(StatusCodes.Status403Forbidden, prohibido.StatusCode);
        await _ofertas.DidNotReceiveWithAnyArgs().PublishAsync(default!, default);
    }

    [Fact]
    public async Task Recorre_todo_el_catalogo_aunque_conteste_paginas_mas_cortas_de_lo_pedido()
    {
        // 30 productos de a 7: saltar de a lo pedido dejaría la mayoría sin publicar, en silencio.
        Catalogo(30, 7);
        _ofertas.PublishAsync(Arg.Any<ProductDetail>(), Arg.Any<CancellationToken>()).Returns(TiendaOfferOutcome.Published);

        var r = Assert.IsType<OkObjectResult>(await Montar().Republicar(default));

        var cuenta = Assert.IsType<OfertaDeLaTiendaController.RepublicarOfertaResponse>(r.Value);
        Assert.Equal(new OfertaDeLaTiendaController.RepublicarOfertaResponse(30, 30, 0, 0), cuenta);
        await _ofertas.Received(30).PublishAsync(Arg.Any<ProductDetail>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cuenta_lo_que_no_tiene_ficha_lo_que_falla_y_lo_que_no_tiene_destino()
    {
        Catalogo(3, 48);
        _catalogo.GetProductAsync("P-001", Arg.Any<CancellationToken>()).Returns((ProductDetail?)null);
        _ofertas.PublishAsync(Arg.Is<ProductDetail>(p => p.Product.Id == "P-002"), Arg.Any<CancellationToken>()).Returns(TiendaOfferOutcome.Failed);
        _ofertas.PublishAsync(Arg.Is<ProductDetail>(p => p.Product.Id == "P-003"), Arg.Any<CancellationToken>()).Returns(TiendaOfferOutcome.NoDestination);

        var r = Assert.IsType<OkObjectResult>(await Montar().Republicar(default));

        Assert.Equal(new OfertaDeLaTiendaController.RepublicarOfertaResponse(3, 0, 2, 1), r.Value);
    }
}
