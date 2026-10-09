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
using Umbraco.Cms.Core.Persistence.Querying;
using Umbraco.Cms.Core.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Despublicar, mandar a la papelera o borrar un <c>eventPage</c> —o el nodo que lo contiene— retira su
/// oferta del orquestador, y nada de eso tumba la acción del editor (ADR 0140 F3).
/// </summary>
public sealed class OfertaDeEventoAlRetirarTests
{
    private readonly IEventOfferPublisher _ofertas = Substitute.For<IEventOfferPublisher>();
    private readonly IContentService _contenidos = Substitute.For<IContentService>();

    private OfertaDeEventoAlRetirar Montar(string fuente = "cms")
    {
        var fuentes = Substitute.For<IOptionsMonitor<CatalogSettings>>();
        fuentes.CurrentValue.Returns(new CatalogSettings
        {
            Sources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Events"] = fuente },
        });
        return new OfertaDeEventoAlRetirar(_ofertas, _contenidos, fuentes, NullLogger<OfertaDeEventoAlRetirar>.Instance);
    }

    private static IContent Nodo(int id, string alias, string? slug)
    {
        var tipo = Substitute.For<ISimpleContentType>();
        tipo.Alias.Returns(alias);
        var nodo = Substitute.For<IContent>();
        nodo.Id.Returns(id);
        nodo.ContentType.Returns(tipo);
        nodo.GetValue<string>("eventSlug", Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<bool>()).Returns(slug);
        return nodo;
    }

    private void Hijos(int padre, params IContent[] hijos)
    {
        _contenidos.GetPagedDescendants(Arg.Is(padre), Arg.Is(0L), Arg.Any<int>(), out Arg.Any<long>(), Arg.Any<IQuery<IContent>?>(), Arg.Any<Ordering?>())
            .Returns(x =>
            {
                x[3] = (long)hijos.Length;
                return hijos;
            });
    }

    [Fact]
    public async Task Despublicar_un_evento_retira_su_oferta()
    {
        Hijos(1);

        await Montar().HandleAsync(new ContentUnpublishedNotification(Nodo(1, "eventPage", "concierto"), new EventMessages()), default);

        await _ofertas.Received(1).RetireAsync("concierto", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Despublicar_la_agenda_retira_los_eventos_que_contiene_y_nada_mas()
    {
        Hijos(10, Nodo(11, "eventPage", "uno"), Nodo(12, "textPage", "no-es-evento"), Nodo(13, "eventPage", "dos"));

        await Montar().HandleAsync(new ContentUnpublishedNotification(Nodo(10, "agenda", null), new EventMessages()), default);

        await _ofertas.Received(1).RetireAsync("uno", Arg.Any<CancellationToken>());
        await _ofertas.Received(1).RetireAsync("dos", Arg.Any<CancellationToken>());
        await _ofertas.Received(2).RetireAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Mandar_a_la_papelera_y_borrar_tambien_retiran()
    {
        Hijos(1);
        var evento = Nodo(1, "eventPage", "concierto");

        await Montar().HandleAsync(new ContentMovedToRecycleBinNotification(
            new MoveEventInfo<IContent>(evento, "-1,1", -20), new EventMessages()), default);
        await Montar().HandleAsync(new ContentDeletedNotification(Nodo(2, "eventPage", "borrado"), new EventMessages()), default);

        await _ofertas.Received(1).RetireAsync("concierto", Arg.Any<CancellationToken>());
        await _ofertas.Received(1).RetireAsync("borrado", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Con_la_demo_no_se_retira_nada()
    {
        Hijos(1);

        await Montar("demo").HandleAsync(new ContentUnpublishedNotification(Nodo(1, "eventPage", "concierto"), new EventMessages()), default);

        await _ofertas.DidNotReceiveWithAnyArgs().RetireAsync(default!, default);
    }

    [Fact]
    public async Task Un_retiro_que_falla_no_tumba_al_editor_ni_a_los_demas_eventos()
    {
        Hijos(10, Nodo(11, "eventPage", "roto"), Nodo(12, "eventPage", "bueno"));
        _ofertas.RetireAsync("roto", Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("x"));

        await Montar().HandleAsync(new ContentUnpublishedNotification(Nodo(10, "agenda", null), new EventMessages()), default);

        await _ofertas.Received(1).RetireAsync("bueno", Arg.Any<CancellationToken>());
    }
}
