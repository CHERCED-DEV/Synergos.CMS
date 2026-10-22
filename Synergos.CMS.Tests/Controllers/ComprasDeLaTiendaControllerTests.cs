using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Controllers;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Controllers;

/// <summary>
/// Las rutas del registro de las compras de la puerta (004-tienda-compra, US 6): sólo con sesión, sólo del mismo
/// origen para anotar, nunca en caché, y una compra ajena es un 404.
/// </summary>
public sealed class ComprasDeLaTiendaControllerTests
{
    private static readonly Guid Ana = Guid.Parse("11111111-2222-3333-4444-555555555555");

    [Fact]
    public async Task Sin_sesion_contesta_401_sin_tocar_la_red_y_sin_cache()
    {
        var (sut, red) = Controller(miembro: null);

        var r = Assert.IsType<ContentResult>(await sut.Compra("pta-1", CancellationToken.None));

        Assert.Equal(401, r.StatusCode);
        Assert.Contains("puerta.sesion_requerida", r.Content, StringComparison.Ordinal);
        Assert.Equal("no-store", sut.HttpContext.Response.Headers.CacheControl.ToString());
        Assert.Equal(0, red.Pedidos);
    }

    [Fact]
    public async Task Anotar_desde_otro_origen_se_rechaza_antes_de_leer_la_compra()
    {
        var (sut, red) = Controller(miembro: Ana);
        sut.HttpContext.Request.Method = "POST";
        sut.HttpContext.Request.Headers["Sec-Fetch-Site"] = "cross-site";

        var r = Assert.IsType<ContentResult>(await sut.Anotar("pta-1", CancellationToken.None));

        Assert.Equal(403, r.StatusCode);
        Assert.Equal(0, red.Pedidos);
    }

    [Fact]
    public async Task La_compra_de_otro_es_un_404()
    {
        var (sut, red) = Controller(miembro: Ana);

        var r = Assert.IsType<ContentResult>(await sut.Compra("de-otro", CancellationToken.None));

        Assert.Equal(404, r.StatusCode);
        Assert.Contains("tienda.purchase_not_found", r.Content, StringComparison.Ordinal);
        Assert.Equal(1, red.Pedidos);
    }

    private static (ComprasDeLaTiendaController Sut, RedQueCuenta Red) Controller(Guid? miembro)
    {
        var puerta = new PuertaSettings();
        puerta.Flujos["tienda.compra"] = new FlujoDeLaPuertaSettings { Acceso = AccesoDeLaPuerta.Miembro, SujetoKind = "tienda.comprador" };
        var monitor = Substitute.For<IOptionsMonitor<PuertaSettings>>();
        monitor.CurrentValue.Returns(puerta);
        var red = new RedQueCuenta();
        var registro = new RegistroDeComprasDeLaTienda(
            red, new LibroDeComprasDeLaPuerta(new InMemoryJsonEntityStore()), monitor, Substitute.For<IProductCatalogProvider>(),
            NullLogger<RegistroDeComprasDeLaTienda>.Instance, hayDestino: true);

        var gate = Substitute.For<IMemberAccessGate>();
        gate.IsAuthenticated.Returns(miembro is not null);
        gate.CurrentMemberKey.Returns(miembro);

        var sut = new ComprasDeLaTiendaController(registro, gate)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
        return (sut, red);
    }

    /// <summary>Un orquestador que no tiene ninguna compra para nadie, y cuenta cuántas veces se le preguntó.</summary>
    private sealed class RedQueCuenta : IHttpClientFactory
    {
        public int Pedidos { get; private set; }

        public HttpClient CreateClient(string name) => new(new Manejador(this)) { BaseAddress = new Uri("http://tienda.local/") };

        private sealed class Manejador(RedQueCuenta red) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                red.Pedidos++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("", Encoding.UTF8) });
            }
        }
    }
}
