using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Notifications;
using Synergos.CMS.Web.Services;
using Synergos.CMS.Web.Services.Catalog;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Persistence.Querying;
using Umbraco.Cms.Core.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Publicar un <c>equipmentPage</c> publica su oferta en el orquestador del alquiler y despublicarlo la retira, con el
/// catálogo en el contenido, sin que nada de eso tumbe la acción del editor (línea 005, T029).
/// </summary>
public sealed class OfertaDeEquipoTests
{
    private readonly IEquipmentCatalogProvider _catalogo = Substitute.For<IEquipmentCatalogProvider>();
    private readonly IEquipmentOfferPublisher _ofertas = Substitute.For<IEquipmentOfferPublisher>();
    private readonly IContentService _contenidos = Substitute.For<IContentService>();
    private readonly IRaizDelAlquiler _raiz = Substitute.For<IRaizDelAlquiler>();

    public OfertaDeEquipoTests() => _raiz.SeVe().Returns(true);

    private static IOptionsMonitor<CatalogSettings> Fuentes(string fuente)
    {
        var fuentes = Substitute.For<IOptionsMonitor<CatalogSettings>>();
        fuentes.CurrentValue.Returns(new CatalogSettings
        {
            Sources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Alquiler"] = fuente },
        });
        return fuentes;
    }

    private OfertaDeEquipoAlPublicar AlPublicar(string fuente = "cms", ILogger<OfertaDeEquipoAlPublicar>? log = null)
        => new(_catalogo, _ofertas, Fuentes(fuente), _raiz, log ?? NullLogger<OfertaDeEquipoAlPublicar>.Instance);

    private sealed class LogQueGuarda : ILogger<OfertaDeEquipoAlPublicar>
    {
        public List<(LogLevel Nivel, string Texto)> Lineas { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Lineas.Add((logLevel, formatter(state, exception)));
    }

    private OfertaDeEquipoAlRetirar AlRetirar(string fuente = "cms")
        => new(_ofertas, _contenidos, Fuentes(fuente), NullLogger<OfertaDeEquipoAlRetirar>.Instance);

    private static IContent Nodo(int id, string alias, string? slug)
    {
        var tipo = Substitute.For<ISimpleContentType>();
        tipo.Alias.Returns(alias);
        var nodo = Substitute.For<IContent>();
        nodo.Id.Returns(id);
        nodo.ContentType.Returns(tipo);
        nodo.GetValue<string>("equipmentSlug", Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<bool>()).Returns(slug);
        return nodo;
    }

    private void Hijos(int padre, params IContent[] hijos)
        => _contenidos.GetPagedDescendants(Arg.Is(padre), Arg.Is(0L), Arg.Any<int>(), out Arg.Any<long>(), Arg.Any<IQuery<IContent>?>(), Arg.Any<Ordering?>())
            .Returns(x =>
            {
                x[3] = (long)hijos.Length;
                return hijos;
            });

    [Fact]
    public async Task Publicar_un_equipo_publica_su_oferta_tal_como_la_sirve_el_catalogo_e_ignora_otros_tipos()
    {
        var equipo = HttpEquipmentOfferPublisherTests.Equipo();
        _catalogo.GetAsync("andamio-6m", Arg.Any<CancellationToken>()).Returns(equipo);

        await AlPublicar().HandleAsync(new ContentPublishedNotification(
            [Nodo(1, "equipmentPage", "andamio-6m"), Nodo(2, "eventPage", "otra")], new EventMessages()), default);

        await _ofertas.Received(1).PublishAsync(equipo, Arg.Any<CancellationToken>());
        await _catalogo.DidNotReceive().GetAsync("otra", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Con_la_demo_no_se_publica_ni_se_retira_nada()
    {
        _catalogo.GetAsync("andamio-6m", Arg.Any<CancellationToken>()).Returns(HttpEquipmentOfferPublisherTests.Equipo());
        Hijos(1);

        await AlPublicar("demo").HandleAsync(new ContentPublishedNotification(Nodo(1, "equipmentPage", "andamio-6m"), new EventMessages()), default);
        await AlRetirar("demo").HandleAsync(new ContentUnpublishedNotification(Nodo(1, "equipmentPage", "andamio-6m"), new EventMessages()), default);

        await _ofertas.DidNotReceiveWithAnyArgs().PublishAsync(default!, default);
        await _ofertas.DidNotReceiveWithAnyArgs().RetireAsync(default!, default);
    }

    [Fact]
    public async Task Un_equipo_que_el_catalogo_no_sirve_no_tiene_oferta_y_la_que_tuviera_se_retira()
    {
        _catalogo.GetAsync("sin-ficha", Arg.Any<CancellationToken>()).Returns((RentalEquipment?)null);

        await AlPublicar().HandleAsync(new ContentPublishedNotification(
            [Nodo(1, "equipmentPage", "sin-ficha"), Nodo(2, "equipmentPage", null)], new EventMessages()), default);

        await _ofertas.DidNotReceiveWithAnyArgs().PublishAsync(default!, default);
        await _ofertas.Received(1).RetireAsync("sin-ficha", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Sin_la_raiz_del_sitio_a_la_vista_no_se_retira_ni_se_publica_y_queda_un_aviso_sin_error()
    {
        // El import que trae el siteRoot y sus equipos en el mismo lote: el árbol publicado todavía no muestra la raíz, y
        // el catálogo diría que no sirve ningún equipo. Ni se le pregunta (su ERR tumbaba el import completo).
        _raiz.SeVe().Returns(false);
        _catalogo.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((RentalEquipment?)null);
        var log = new LogQueGuarda();

        await AlPublicar(log: log).HandleAsync(new ContentPublishedNotification(
            [Nodo(1, "equipmentPage", "andamio-6m"), Nodo(2, "equipmentPage", "taladro")], new EventMessages()), default);

        await _ofertas.DidNotReceiveWithAnyArgs().RetireAsync(default!, default);
        await _ofertas.DidNotReceiveWithAnyArgs().PublishAsync(default!, default);
        await _catalogo.DidNotReceiveWithAnyArgs().GetAsync(default!, default);
        var (nivel, texto) = Assert.Single(log.Lineas);
        Assert.Equal(LogLevel.Warning, nivel);
        Assert.Contains("andamio-6m, taladro", texto, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Despublicar_mandar_a_la_papelera_o_borrar_retira_y_con_los_que_contiene()
    {
        Hijos(10, Nodo(11, "equipmentPage", "uno"), Nodo(12, "textPage", "no-es-equipo"), Nodo(13, "equipmentPage", "dos"));
        Hijos(20);

        await AlRetirar().HandleAsync(new ContentUnpublishedNotification(Nodo(10, "catalogo", null), new EventMessages()), default);
        await AlRetirar().HandleAsync(new ContentMovedToRecycleBinNotification(
            new MoveEventInfo<IContent>(Nodo(20, "equipmentPage", "tres"), "-1,20", -20), new EventMessages()), default);
        await AlRetirar().HandleAsync(new ContentDeletedNotification(Nodo(30, "equipmentPage", "cuatro"), new EventMessages()), default);

        foreach (var slug in new[] { "uno", "dos", "tres", "cuatro" })
        {
            await _ofertas.Received(1).RetireAsync(slug, Arg.Any<CancellationToken>());
        }
        await _ofertas.Received(4).RetireAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Con_la_puerta_cerrada_publicar_no_lanza_ni_sale_a_la_red()
    {
        // El publicador de verdad sin destino (sin Synergos:Alquiler:BaseUrl): el publicar del editor sigue.
        var orquestador = new HttpEquipmentOfferPublisherTests.OrquestadorFalso();
        var sinDestino = new HttpEquipmentOfferPublisher(
            new HttpEquipmentOfferPublisherTests.Fabrica(orquestador), NullLogger<HttpEquipmentOfferPublisher>.Instance, hayDestino: false);
        _catalogo.GetAsync("andamio-6m", Arg.Any<CancellationToken>()).Returns(HttpEquipmentOfferPublisherTests.Equipo());
        Hijos(1);

        await new OfertaDeEquipoAlPublicar(_catalogo, sinDestino, Fuentes("cms"), _raiz, NullLogger<OfertaDeEquipoAlPublicar>.Instance)
            .HandleAsync(new ContentPublishedNotification(Nodo(1, "equipmentPage", "andamio-6m"), new EventMessages()), default);
        await new OfertaDeEquipoAlRetirar(sinDestino, _contenidos, Fuentes("cms"), NullLogger<OfertaDeEquipoAlRetirar>.Instance)
            .HandleAsync(new ContentUnpublishedNotification(Nodo(1, "equipmentPage", "andamio-6m"), new EventMessages()), default);

        Assert.Empty(orquestador.Llamadas);
    }

    [Fact]
    public async Task Un_publicador_que_lanza_no_tumba_al_editor_ni_a_los_demas_equipos()
    {
        var roto = HttpEquipmentOfferPublisherTests.Equipo() with { Id = "roto" };
        var bueno = HttpEquipmentOfferPublisherTests.Equipo() with { Id = "bueno" };
        _catalogo.GetAsync("roto", Arg.Any<CancellationToken>()).Returns(roto);
        _catalogo.GetAsync("bueno", Arg.Any<CancellationToken>()).Returns(bueno);
        _ofertas.PublishAsync(roto, Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("al armar el cuerpo"));
        Hijos(10, Nodo(11, "equipmentPage", "roto"), Nodo(12, "equipmentPage", "bueno"));
        _ofertas.RetireAsync("roto", Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("x"));

        await AlPublicar().HandleAsync(new ContentPublishedNotification(
            [Nodo(11, "equipmentPage", "roto"), Nodo(12, "equipmentPage", "bueno")], new EventMessages()), default);
        await AlRetirar().HandleAsync(new ContentUnpublishedNotification(Nodo(10, "catalogo", null), new EventMessages()), default);

        await _ofertas.Received(1).PublishAsync(bueno, Arg.Any<CancellationToken>());
        await _ofertas.Received(1).RetireAsync("bueno", Arg.Any<CancellationToken>());
    }
}
