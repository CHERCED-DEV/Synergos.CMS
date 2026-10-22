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
/// Despublicar, mandar a la papelera o borrar un <c>productPage</c> —o la categoría que lo contiene— retira su
/// oferta del orquestador de la tienda, y nada de eso tumba la acción del editor (004-tienda-compra, R-9).
/// </summary>
public sealed class OfertaDeTiendaAlRetirarTests
{
    private readonly ITiendaOfferPublisher _ofertas = Substitute.For<ITiendaOfferPublisher>();
    private readonly IContentService _contenidos = Substitute.For<IContentService>();

    private OfertaDeTiendaAlRetirar Montar(string fuente = "cms")
    {
        var fuentes = Substitute.For<IOptionsMonitor<CatalogSettings>>();
        fuentes.CurrentValue.Returns(new CatalogSettings
        {
            Sources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Shop"] = fuente },
        });
        return new OfertaDeTiendaAlRetirar(_ofertas, _contenidos, fuentes, NullLogger<OfertaDeTiendaAlRetirar>.Instance);
    }

    private static IContent Nodo(int id, string alias, string? sku) => OfertaDeTiendaAlPublicarTests.Nodo(alias, sku, id);

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
    public async Task Despublicar_un_producto_retira_su_oferta()
    {
        Hijos(1);

        await Montar().HandleAsync(new ContentUnpublishedNotification(Nodo(1, "productPage", "TAZA-001"), new EventMessages()), default);

        await _ofertas.Received(1).RetireAsync("TAZA-001", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Despublicar_una_categoria_retira_los_productos_que_contiene_y_nada_mas()
    {
        Hijos(10, Nodo(11, "productPage", "UNO"), Nodo(12, "textPage", "NO-ES-PRODUCTO"), Nodo(13, "productPage", "DOS"));

        await Montar().HandleAsync(new ContentUnpublishedNotification(Nodo(10, "productCategoryPage", null), new EventMessages()), default);

        await _ofertas.Received(1).RetireAsync("UNO", Arg.Any<CancellationToken>());
        await _ofertas.Received(1).RetireAsync("DOS", Arg.Any<CancellationToken>());
        await _ofertas.Received(2).RetireAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Mandar_a_la_papelera_con_sus_hijos_y_borrar_tambien_retiran()
    {
        Hijos(1, Nodo(3, "productPage", "HIJO"));
        var categoria = Nodo(1, "productCategoryPage", null);

        await Montar().HandleAsync(new ContentMovedToRecycleBinNotification(
            new MoveEventInfo<IContent>(categoria, "-1,1", -20), new EventMessages()), default);
        await Montar().HandleAsync(new ContentDeletedNotification(Nodo(2, "productPage", "BORRADO"), new EventMessages()), default);

        await _ofertas.Received(1).RetireAsync("HIJO", Arg.Any<CancellationToken>());
        await _ofertas.Received(1).RetireAsync("BORRADO", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Con_la_demo_no_se_retira_nada()
    {
        Hijos(1);

        await Montar("demo").HandleAsync(new ContentUnpublishedNotification(Nodo(1, "productPage", "TAZA-001"), new EventMessages()), default);

        await _ofertas.DidNotReceiveWithAnyArgs().RetireAsync(default!, default);
    }

    [Fact]
    public async Task Un_retiro_que_falla_no_tumba_al_editor_ni_a_los_demas_productos()
    {
        Hijos(10, Nodo(11, "productPage", "ROTO"), Nodo(12, "productPage", "BUENO"));
        _ofertas.RetireAsync("ROTO", Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("x"));

        await Montar().HandleAsync(new ContentUnpublishedNotification(Nodo(10, "productCategoryPage", null), new EventMessages()), default);

        await _ofertas.Received(1).RetireAsync("BUENO", Arg.Any<CancellationToken>());
    }
}
