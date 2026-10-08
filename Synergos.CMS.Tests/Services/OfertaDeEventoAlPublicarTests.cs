using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Notifications;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Notifications;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Publicar un <c>eventPage</c> publica su oferta en el orquestador, con el catálogo en el contenido, y
/// nada de eso tumba el publicar del editor (ADR 0140 F3).
/// </summary>
public sealed class OfertaDeEventoAlPublicarTests
{
    private readonly IEventCatalogProvider _catalogo = Substitute.For<IEventCatalogProvider>();
    private readonly IEventOfferPublisher _ofertas = Substitute.For<IEventOfferPublisher>();

    private OfertaDeEventoAlPublicar Montar(string fuente)
    {
        var fuentes = Substitute.For<IOptionsMonitor<CatalogSettings>>();
        fuentes.CurrentValue.Returns(new CatalogSettings
        {
            Sources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Events"] = fuente },
        });
        return new OfertaDeEventoAlPublicar(_catalogo, _ofertas, fuentes, NullLogger<OfertaDeEventoAlPublicar>.Instance);
    }

    private static IContent Nodo(string alias, string? slug)
    {
        var tipo = Substitute.For<ISimpleContentType>();
        tipo.Alias.Returns(alias);
        var nodo = Substitute.For<IContent>();
        nodo.ContentType.Returns(tipo);
        nodo.GetValue<string>("eventSlug", Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<bool>()).Returns(slug);
        return nodo;
    }

    private static ContentPublishedNotification Publicados(params IContent[] nodos) => new(nodos, new EventMessages());

    [Fact]
    public async Task Con_el_catalogo_en_el_contenido_publica_la_oferta_del_evento_tal_como_lo_sirve_el_catalogo()
    {
        var evento = HttpEventOfferPublisherTests.Evento();
        _catalogo.GetEventAsync("concierto", Arg.Any<CancellationToken>()).Returns(evento);

        await Montar("cms").HandleAsync(Publicados(Nodo("eventPage", "concierto"), Nodo("textPage", "otra")), default);

        await _ofertas.Received(1).PublishAsync(evento, Arg.Any<CancellationToken>());
        await _catalogo.DidNotReceive().GetEventAsync("otra", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Con_la_demo_un_eventPage_no_se_vende_y_no_publica_nada()
    {
        // El catálogo SÍ lo serviría: lo único que frena es la fuente.
        _catalogo.GetEventAsync("concierto", Arg.Any<CancellationToken>()).Returns(HttpEventOfferPublisherTests.Evento());

        await Montar("demo").HandleAsync(Publicados(Nodo("eventPage", "concierto")), default);

        await _ofertas.DidNotReceiveWithAnyArgs().PublishAsync(default!, default);
    }

    [Fact]
    public async Task Un_evento_que_el_catalogo_no_sirve_no_tiene_oferta()
    {
        _catalogo.GetEventAsync("sin-ficha", Arg.Any<CancellationToken>()).Returns((EventDetail?)null);

        await Montar("cms").HandleAsync(Publicados(Nodo("eventPage", "sin-ficha"), Nodo("eventPage", null)), default);

        await _ofertas.DidNotReceiveWithAnyArgs().PublishAsync(default!, default);
    }

    [Fact]
    public async Task Un_fallo_al_leer_o_publicar_no_tumba_el_publicar_del_editor_ni_a_los_demas_eventos()
    {
        var evento = HttpEventOfferPublisherTests.Evento();
        _catalogo.GetEventAsync("roto", Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("caché"));
        _catalogo.GetEventAsync("bueno", Arg.Any<CancellationToken>()).Returns(evento);

        await Montar("cms").HandleAsync(Publicados(Nodo("eventPage", "roto"), Nodo("eventPage", "bueno")), default);

        await _ofertas.Received(1).PublishAsync(evento, Arg.Any<CancellationToken>());
    }
}
