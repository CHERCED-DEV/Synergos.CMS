using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Notifications;
using Synergos.CMS.Web.Services.Catalog;
using Synergos.CMS.Web.Services.Gobierno;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Persistence.Querying;
using Umbraco.Cms.Core.Services;

namespace Synergos.CMS.Tests.Services.Gobierno;

/// <summary>
/// Publicar un <c>tramitePage</c> publica su oferta en <c>Bff.Gobierno</c> con la clave del nodo y la tasa que dejan las
/// reglas del catálogo, y despublicarlo la retira; nada de eso tumba la acción del editor (009-gobierno-tramite T034).
/// </summary>
/// <remarks>
/// Las cuatro filas de la tasa van de punta a punta: el trámite lo arman las reglas de verdad
/// (<see cref="TramiteContentRules.BuildTramite"/>), el manejador lo pide al catálogo y el publicador de verdad lo manda a
/// un orquestador falso que graba la ruta, la llave y el cuerpo. Que la forma del cuerpo sea la del contrato lo cruza
/// <c>ConsumidorCmsDeBffGobiernoTests</c>.
/// </remarks>
public sealed class PublicadorDeOfertasDeGobiernoTests
{
    internal sealed class OrquestadorFalso : HttpMessageHandler
    {
        public List<(string Ruta, string? Llave, string Cuerpo)> Llamadas { get; } = [];

        public Func<HttpRequestMessage, HttpResponseMessage> Responde { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"tramite":"x","tasa":1,"moneda":"COP","retirada":false}""", Encoding.UTF8, "application/json"),
        };

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            Llamadas.Add((req.RequestUri!.AbsolutePath,
                req.Headers.TryGetValues("Idempotency-Key", out var v) ? v.Single() : null,
                req.Content is null ? "" : await req.Content.ReadAsStringAsync(ct)));
            return Responde(req);
        }
    }

    private sealed class Fabrica(HttpMessageHandler cable) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            Assert.Equal(PublicadorDeOfertasDeGobierno.ClientName, name);
            return new HttpClient(cable, disposeHandler: false) { BaseAddress = new Uri("http://bff-gobierno.local/") };
        }
    }

    internal static PublicadorDeOfertasDeGobierno Publicador(OrquestadorFalso orquestador, bool hayDestino = true)
        => new(new Fabrica(orquestador), NullLogger<PublicadorDeOfertasDeGobierno>.Instance, hayDestino);

    private static readonly TramiteStepLabels Pasos = new("Radicar", "", "Revisión", "", "Resolución", "");

    /// <summary>Un trámite como lo sirve el catálogo a partir de lo que escribió el editor; nulo si las reglas lo omiten.</summary>
    internal static TramiteDetail? Tramite(Guid clave, bool marcadoGratuito, decimal tasa)
        => TramiteContentRules.BuildTramite(
            new TramitePageContent(
                Slug: "certificado-residencia", Name: "Certificado de residencia", Agency: "Alcaldía", Channel: "en-linea",
                Required: ["Recibo de servicio público"], IsFree: marcadoGratuito, Fee: tasa, EstimatedDays: 2, Clave: clave),
            "COP", Pasos).Value;

    private readonly ITramiteCatalogProvider _catalogo = Substitute.For<ITramiteCatalogProvider>();
    private readonly IPortalDeTramitesEnElContenido _portal = Substitute.For<IPortalDeTramitesEnElContenido>();
    private readonly IContentService _contenidos = Substitute.For<IContentService>();

    public PublicadorDeOfertasDeGobiernoTests() => _portal.Ve(Arg.Any<Guid>()).Returns(true);

    private static IOptionsMonitor<CatalogSettings> Fuente(string fuente)
    {
        var fuentes = Substitute.For<IOptionsMonitor<CatalogSettings>>();
        fuentes.CurrentValue.Returns(new CatalogSettings
        {
            Sources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Gov"] = fuente },
        });
        return fuentes;
    }

    private OfertaDeTramiteAlPublicar AlPublicar(IPublicadorDeOfertasDeGobierno ofertas, string fuente = "cms")
        => new(_catalogo, ofertas, Fuente(fuente), _portal, NullLogger<OfertaDeTramiteAlPublicar>.Instance);

    private OfertaDeTramiteAlRetirar AlRetirar(IPublicadorDeOfertasDeGobierno ofertas, string fuente = "cms")
        => new(ofertas, _contenidos, Fuente(fuente), NullLogger<OfertaDeTramiteAlRetirar>.Instance);

    private static IContent Nodo(string alias, Guid clave, int id = 0)
    {
        var tipo = Substitute.For<ISimpleContentType>();
        tipo.Alias.Returns(alias);
        var nodo = Substitute.For<IContent>();
        nodo.Id.Returns(id);
        nodo.Key.Returns(clave);
        nodo.ContentType.Returns(tipo);
        return nodo;
    }

    private static ContentPublishedNotification Publicados(params IContent[] nodos) => new(nodos, new EventMessages());

    /// <summary>
    /// Las cuatro filas de la regla del catálogo (ADR 0123 §4): marcado gratuito y en cero → cero; sin marcar y con tasa →
    /// la tasa; marcado gratuito y con tasa → LA TASA (el catálogo lo registra como error: anunciar gratis lo que se paga
    /// deja al ciudadano en la caja); sin marcar y en cero → no se ofrece, y la que hubiera se retira.
    /// </summary>
    [Theory]
    [InlineData(true, 0, 0)]
    [InlineData(false, 65_500, 65_500)]
    [InlineData(true, 65_500, 65_500)]
    [InlineData(false, 0, null)]
    public async Task Publicar_ofrece_la_tasa_que_dejan_las_reglas_del_catalogo_con_la_clave_del_nodo(bool marcadoGratuito, int tasa, int? ofrecida)
    {
        var clave = Guid.NewGuid();
        _catalogo.GetAsync(clave.ToString(), Arg.Any<CancellationToken>()).Returns(Tramite(clave, marcadoGratuito, tasa));
        var orquestador = new OrquestadorFalso();

        await AlPublicar(Publicador(orquestador)).HandleAsync(Publicados(Nodo("tramitePage", clave), Nodo("textPage", Guid.NewGuid())), default);

        var (ruta, llave, cuerpo) = Assert.Single(orquestador.Llamadas);
        Assert.False(string.IsNullOrWhiteSpace(llave));
        if (ofrecida is null)
        {
            Assert.Equal($"/v1/ofertas/{clave}/retirar", ruta);
            Assert.Equal("", cuerpo);
            return;
        }
        Assert.Equal("/v1/ofertas", ruta);
        var o = JsonDocument.Parse(cuerpo).RootElement;
        Assert.Equal(clave.ToString(), o.GetProperty("tramite").GetString());
        Assert.Equal((decimal)ofrecida, o.GetProperty("tasa").GetDecimal());
        Assert.Equal("COP", o.GetProperty("moneda").GetString());
    }

    [Fact]
    public async Task Despublicar_un_tramite_o_el_nodo_que_lo_contiene_retira_su_oferta_por_la_clave()
    {
        var suelto = Guid.NewGuid();
        var hijo = Guid.NewGuid();
        _contenidos.GetPagedDescendants(Arg.Is(7), Arg.Is(0L), Arg.Any<int>(), out Arg.Any<long>(), Arg.Any<IQuery<IContent>?>(), Arg.Any<Ordering?>())
            .Returns(x =>
            {
                x[3] = 1L;
                return new[] { Nodo("tramitePage", hijo) };
            });
        var orquestador = new OrquestadorFalso
        {
            Responde = _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent($$"""{"code":"{{PublicadorDeOfertasDeGobierno.NoExiste}}","detail":"no"}""", Encoding.UTF8, "application/problem+json"),
            },
        };

        await AlRetirar(Publicador(orquestador)).HandleAsync(
            new ContentUnpublishedNotification([Nodo("tramitePage", suelto), Nodo("folder", Guid.NewGuid(), 7)], new EventMessages()), default);

        Assert.Equal(
            new[] { $"/v1/ofertas/{suelto}/retirar", $"/v1/ofertas/{hijo}/retirar" }.Order(StringComparer.Ordinal),
            orquestador.Llamadas.Select(l => l.Ruta).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Sin_destino_ni_publicar_ni_retirar_salen_a_la_red_y_el_editor_publica_igual()
    {
        var clave = Guid.NewGuid();
        _catalogo.GetAsync(clave.ToString(), Arg.Any<CancellationToken>()).Returns(Tramite(clave, false, 65_500m));
        var orquestador = new OrquestadorFalso();
        var publicador = Publicador(orquestador, hayDestino: false);

        await AlPublicar(publicador).HandleAsync(Publicados(Nodo("tramitePage", clave)), default);
        await AlRetirar(publicador).HandleAsync(new ContentUnpublishedNotification(Nodo("tramitePage", clave), new EventMessages()), default);

        Assert.Equal(EstadoDeLaOfertaDeTramite.SinDestino, await publicador.PublicarAsync(Tramite(clave, false, 65_500m)!));
        Assert.Empty(orquestador.Llamadas);
    }

    [Fact]
    public async Task Con_la_demo_un_tramitePage_no_se_ofrece_ni_se_retira()
    {
        var clave = Guid.NewGuid();
        _catalogo.GetAsync(clave.ToString(), Arg.Any<CancellationToken>()).Returns(Tramite(clave, false, 65_500m));
        var orquestador = new OrquestadorFalso();

        await AlPublicar(Publicador(orquestador), "demo").HandleAsync(Publicados(Nodo("tramitePage", clave)), default);
        await AlRetirar(Publicador(orquestador), "demo").HandleAsync(new ContentUnpublishedNotification(Nodo("tramitePage", clave), new EventMessages()), default);

        Assert.Empty(orquestador.Llamadas);
    }

    [Fact]
    public async Task Un_tramite_que_el_catalogo_todavia_no_ve_en_el_portal_no_se_toca()
    {
        var clave = Guid.NewGuid();
        _portal.Ve(clave).Returns(false);
        var orquestador = new OrquestadorFalso();

        await AlPublicar(Publicador(orquestador)).HandleAsync(Publicados(Nodo("tramitePage", clave)), default);

        Assert.Empty(orquestador.Llamadas);
        await _catalogo.DidNotReceiveWithAnyArgs().GetAsync(default!, default);
    }
}
