using NSubstitute;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Controllers;
using Synergos.CMS.Web.Services.Lecturas;
using static Synergos.CMS.Tests.Controllers.Lecturas.LecturasDePrueba;

namespace Synergos.CMS.Tests.Controllers.Lecturas;

/// <summary>Las reseñas genéricas de un producto, vistas desde la ficha del storefront (014-despiece-tienda, I4).</summary>
public sealed class LecturaDeResenasContractTests
{
    private const string Laptop = "tec-laptop-pro-14";

    private static (LecturaDeResenasController Controlador, IShopOrderService Pedidos) Armar(
        IMemberAccessGate sesion,
        IProductCatalogProvider? catalogo = null,
        params CustomerReview[] deCompradores)
    {
        var compradores = Substitute.For<ICatalogSocialProof>();
        compradores.GetReviewsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(deCompradores);
        var pedidos = Substitute.For<IShopOrderService>();
        pedidos.GetOrdersByMemberAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<ShopOrder>());
        var lectura = new LecturaDeResenasDeLaTienda(catalogo ?? new StubProductCatalogProvider(), compradores, pedidos);
        return (new LecturaDeResenasController(new[] { lectura }, sesion), pedidos);
    }

    /// <summary>
    /// Las reseñas son las que el catálogo le da al producto, y no se le suman las de los compradores: la fuente del
    /// contenido ya las pone en el producto, y sumarlas otra vez las duplicaba (se vio en vivo en la piloto). De
    /// <see cref="ICatalogSocialProof"/> sale sólo <c>verificada</c>.
    /// </summary>
    [Fact]
    public async Task Trae_las_del_catalogo_una_vez_y_marca_las_de_quien_lo_compro()
    {
        var deAna = new CustomerReview(Laptop, MiClave, "Ana Compradora", 5, "Llegó perfecta", "Rápida y liviana.", new DateOnly(2026, 7, 1));
        var catalogo = Substitute.For<IProductCatalogProvider>();
        catalogo.GetProductAsync(Laptop, Arg.Any<CancellationToken>()).Returns(new ProductDetail(
            new CatalogProductSummary(Laptop, "Laptop Pro 14", 5_200_000m, "COP", "Aurora", "Tecnología", null, 5, 2, 12),
            "Ultrabook de 14 pulgadas.",
            Array.Empty<string>(),
            Array.Empty<ProductVariant>(),
            new[]
            {
                new ProductReview(deAna.AuthorName, deAna.Rating, deAna.Title, deAna.Body, deAna.Date),
                new ProductReview("Camila R.", 4, "Excelente rendimiento", "La batería dura todo el día.", new DateOnly(2026, 5, 12)),
            },
            Array.Empty<ProductQuestion>()));
        var (controlador, _) = Armar(SinSesion(), catalogo, deAna);

        var resenas = Wire(await controlador.DelElemento("tienda", Laptop, default));

        TieneLasClaves(resenas, "id", "resumen", "resenas", "puedeResenar");
        Assert.Equal(2, resenas.GetProperty("resenas").GetArrayLength());
        var primera = resenas.GetProperty("resenas")[0];
        TieneLasClaves(primera, "autor", "calificacion", "titulo", "texto", "fecha", "verificada");
        Assert.Equal("Ana Compradora", primera.GetProperty("autor").GetString());
        Assert.True(primera.GetProperty("verificada").GetBoolean());
        Assert.False(resenas.GetProperty("resenas")[1].GetProperty("verificada").GetBoolean());
        Assert.Equal(2, resenas.GetProperty("resumen").GetProperty("cantidad").GetInt32());
        Assert.Equal(4.5, resenas.GetProperty("resumen").GetProperty("promedio").GetDouble());
        Assert.False(resenas.GetProperty("puedeResenar").GetBoolean());
    }

    [Fact]
    public async Task Quien_lo_compro_y_lo_pago_puede_resenarlo()
    {
        var (controlador, pedidos) = Armar(Sesion());
        pedidos.GetOrdersByMemberAsync(MiClave, Arg.Any<CancellationToken>())
            .Returns(new[] { Pedido("ord_1", OrderStatus.Paid, MiClave, Linea(Laptop)) });

        var resenas = Wire(await controlador.DelElemento("tienda", Laptop, default));

        Assert.True(resenas.GetProperty("puedeResenar").GetBoolean());
    }

    [Fact]
    public async Task Un_producto_que_no_existe_es_404()
    {
        var (controlador, _) = Armar(SinSesion());

        Assert.Equal(404, Estado(await controlador.DelElemento("tienda", "no-existe", default)));
    }
}
