using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Controllers;
using Synergos.CMS.Web.Services;
using Synergos.CMS.Web.Services.Puerta;

namespace Synergos.CMS.Tests.Controllers;

/// <summary>
/// Las entradas de la tienda en la puerta: la de <c>tienda.compra</c> (004-tienda-compra) y las del despacho del personal,
/// <c>tienda.envio</c> y <c>tienda.entrega</c> (013-seller-operaciones, T017). Las del <c>appsettings.json</c> COMITEADO,
/// por el validador real de la puerta y contra la tabla que sale de los contratos incrustados.
/// </summary>
/// <remarks>
/// Hasta esta prueba, un campo de negocio mal escrito en <c>Synergos:Puerta:Flujos</c> sólo se veía al
/// arrancar el CMS: ninguna suite pasaba el fichero por el validador (medido con un mutante, «Carrer»).
/// </remarks>
public sealed class PuertaDeLaTiendaTests
{
    private const string Flujo = "tienda.compra";

    private static IConfiguration ElComiteado()
    {
        var raiz = new DirectoryInfo(AppContext.BaseDirectory);
        while (raiz is not null && !File.Exists(Path.Combine(raiz.FullName, "Synergos.CMS.sln"))) raiz = raiz.Parent;
        Assert.NotNull(raiz);

        return new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(raiz!.FullName, "Synergos.CMS.Web", "appsettings.json"), optional: false)
            .Build();
    }

    private static PuertaSettings LaComiteada()
        => ElComiteado().GetSection(PuertaSettings.Seccion).Get<PuertaSettings>() ?? new PuertaSettings();

    /// <summary>
    /// Las secciones que el CMS registra, leídas de sus composers: el <c>appsettings.json</c> comiteado abre los flujos de
    /// TODAS las líneas, y una lista escrita aquí se rompía cada vez que una abría uno con su propia sección.
    /// </summary>
    private static SeccionesDelSitio Secciones() => SeccionesDeNegocioDelArbol.Todas();

    [Fact]
    public void La_puerta_del_appsettings_comiteado_pasa_el_validador()
    {
        var ajustes = LaComiteada();
        Assert.True(ajustes.Flujos.ContainsKey(Flujo), $"appsettings.json no abre {Flujo} en la puerta.");

        var resultado = new ValidadorDeLaPuerta(TablaDeLaPuerta.Incrustada(), Secciones()).Validate(null, ajustes);

        Assert.True(resultado.Succeeded, string.Join(" | ", resultado.Failures ?? []));
    }

    [Fact]
    public void La_compra_de_la_tienda_es_de_miembros_con_la_transportadora_del_negocio_y_el_enlace_a_la_tienda()
    {
        var entrada = LaComiteada().Flujos[Flujo];

        Assert.Equal(AccesoDeLaPuerta.Miembro, entrada.Acceso);
        Assert.Equal("tienda.comprador", entrada.SujetoKind);
        Assert.Equal("Storefront", entrada.Negocio!.Seccion);
        Assert.Equal(["Carrier"], entrada.Negocio.Campos);
        Assert.Equal("/tienda/?compra={id}", entrada.Aviso!.Ruta);
    }

    /// <summary>Los dos actos del despacho que abre la puerta (013-seller-operaciones, T017).</summary>
    public static TheoryData<string> DelDespacho() => new() { "tienda.envio", "tienda.entrega" };

    [Theory]
    [MemberData(nameof(DelDespacho))]
    public void El_despacho_es_del_personal_de_la_tienda_con_sus_roles_y_sin_aviso(string flujo)
    {
        var ajustes = LaComiteada();
        Assert.True(ajustes.Flujos.TryGetValue(flujo, out var entrada), $"appsettings.json no abre {flujo} en la puerta.");

        var personal = (ElComiteado().GetSection("Synergos:Tienda").Get<TiendaSettings>() ?? new TiendaSettings()).Personal();
        Assert.Equal(AccesoDeLaPuerta.Personal, entrada!.Acceso);
        Assert.Equal(personal, entrada.Roles);
        Assert.Equal("tienda.personal", entrada.SujetoKind);
        Assert.Null(entrada.Negocio);
        // NG-9: el despacho no manda correo.
        Assert.Null(entrada.Aviso);
    }

    [Theory]
    [MemberData(nameof(DelDespacho))]
    public async Task Un_comprador_que_pide_el_despacho_recibe_403_y_al_orquestador_no_le_llega_nada(string flujo)
    {
        var monitor = Substitute.For<IOptionsMonitor<PuertaSettings>>();
        monitor.CurrentValue.Returns(LaComiteada());
        var comprador = Substitute.For<IMemberAccessGate>();
        comprador.IsAuthenticated.Returns(true);
        comprador.CurrentMemberKey.Returns(Guid.Parse("11111111-2222-3333-4444-555555555555"));
        comprador.HasAnyRole(Arg.Any<string?>()).Returns(false);
        var orquestador = new Grabadora();

        var http = new DefaultHttpContext();
        http.Request.Method = "POST";
        http.Request.Scheme = "https";
        http.Request.Host = new HostString("sitio.test");
        http.Request.Headers["Sec-Fetch-Site"] = "same-origin";
        http.Request.Headers["Idempotency-Key"] = "k-1";
        var cuerpo = Encoding.UTF8.GetBytes("""{"compra":"pta-1"}""");
        http.Request.Body = new MemoryStream(cuerpo);
        http.Request.ContentLength = cuerpo.Length;
        http.Request.ContentType = "application/json";
        var puerta = new FlujosController(
            TablaDeLaPuerta.Incrustada(), monitor, new DestinosDeLaPuerta(["Tienda"]), comprador,
            new ReenvioDeLaPuerta(orquestador, NullLogger<ReenvioDeLaPuerta>.Instance), Secciones())
        {
            ControllerContext = new ControllerContext { HttpContext = http },
        };

        var respuesta = Assert.IsType<ContentResult>(await puerta.Pasar(flujo, "abrir", default));

        Assert.Equal(403, respuesta.StatusCode);
        Assert.Equal("puerta.personal_requerido", JsonDocument.Parse(respuesta.Content!).RootElement.GetProperty("code").GetString());
        Assert.Equal(0, orquestador.Pedidos);
    }

    [Fact]
    public void Sin_el_orquestador_de_la_tienda_configurado_la_puerta_no_abre_el_despacho()
    {
        var tabla = TablaDeLaPuerta.Incrustada();
        var monitor = Substitute.For<IOptionsMonitor<PuertaSettings>>();
        monitor.CurrentValue.Returns(LaComiteada());

        foreach (var flujo in new[] { "tienda.envio", "tienda.entrega" })
        {
            Assert.False(new PuertaDeLosFlujos(tabla, monitor, new DestinosDeLaPuerta([])).Abre(flujo));
            Assert.True(new PuertaDeLosFlujos(tabla, monitor, new DestinosDeLaPuerta(["Tienda"])).Abre(flujo));
        }
    }

    /// <summary>El orquestador de la tienda: cuenta lo que le llega y contesta 201.</summary>
    private sealed class Grabadora : HttpMessageHandler, IHttpClientFactory
    {
        public int Pedidos { get; private set; }

        public HttpClient CreateClient(string name) => new(this, disposeHandler: false) { BaseAddress = new Uri("http://bff-tienda.local/") };

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Pedidos++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent("{}", Encoding.UTF8, "application/json") });
        }
    }
}
