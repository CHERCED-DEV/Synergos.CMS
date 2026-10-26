using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Interfaces.SynHost;
using Synergos.CMS.Web.Services.SynHost;

namespace Synergos.CMS.Tests.Services.SynHost;

/// <summary>
/// La consola del vendedor recibe lo del editor, dónde vive su API en el sitio y la zona de sus fechas
/// (013-seller-operaciones, T004): la bandeja dice el día de cada pedido en la hora del SITIO, como «Mis compras».
/// </summary>
public sealed class SellerResolutorTests
{
    private static SellerProps Resuelto(ListadosSettings listados)
    {
        var negocio = Substitute.For<INegocioDelSitio<NegocioDeSeller>>();
        negocio.Actual().Returns(new NegocioDeSeller("/api/shop"));

        return new SellerResolutor(ElementoFalso.Fallback, negocio, Options.Create(listados), NullLogger<SellerResolutor>.Instance)
            .Resolver(ElementoFalso.Con(("heading", "Consola de la tienda"), ("subheading", "Pedidos y devoluciones")))
            .Props;
    }

    [Fact]
    public void Copia_lo_del_editor_la_API_del_sitio_y_la_zona_de_los_listados()
    {
        var props = Resuelto(new ListadosSettings());

        Assert.Equal(new SellerProps("Consola de la tienda", "Pedidos y devoluciones", "/api/shop", "America/Bogota"), props);
    }

    [Fact]
    public void La_zona_es_la_que_declara_el_despliegue_y_no_una_que_suponga_el_elemento()
    {
        Assert.Equal("Europe/Madrid", Resuelto(new ListadosSettings { ZonaHoraria = "Europe/Madrid" }).ZonaHoraria);
    }
}
