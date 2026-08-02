using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Controllers;

namespace Synergos.CMS.Tests.Controllers;

/// <summary>
/// Cierra el IDOR que la auditoría F2 encontró vivo en <see cref="ShopCatalogController"/>:
/// wishlist y mensajería tomaban la identidad (<c>owner</c>/<c>from</c>/<c>participant</c>) de
/// un parámetro del cliente, no de la sesión.
/// </summary>
/// <remarks>
/// <para><b>Qué se podía hacer antes.</b> El email es enumerable, así que con solo conocer un
/// correo cualquiera podía: leer y <b>mutar</b> la lista de deseos ajena; leer la bandeja de
/// otro; leer un hilo privado; y <b>enviar mensajes como otra persona</b>. El propio archivo
/// declaraba ese mismo IDOR cerrado en <c>Orders</c> (el <c>?customer=</c> se ignora) y lo
/// había dejado abierto en los bloques de al lado.</para>
///
/// <para><b>La propiedad que fijan estos tests</b> es la del <c>orders</c>: la identidad la
/// pone el servidor (<see cref="IMemberAccessGate.CurrentMemberEmail"/>), el parámetro del
/// cliente se ignora, y el anónimo recibe 401 <b>antes</b> de que el seam se toque. Cada test
/// verifica las dos mitades: el código HTTP correcto Y que el seam recibió la identidad de la
/// SESIÓN, no la del atacante.</para>
/// </remarks>
public sealed class ShopWishlistMessagingAuthTests
{
    private const string Yo = "victima@synergos.co";
    private const string Atacante = "atacante@synergos.co";

    private sealed record Harness(
        ShopCatalogController Controller,
        IUserCollection Collections,
        IMessagingService Messaging);

    private static Harness Make(bool authenticated, string? sessionEmail = Yo)
    {
        var gate = Substitute.For<IMemberAccessGate>();
        gate.IsAuthenticated.Returns(authenticated);
        gate.CurrentMemberEmail.Returns(authenticated ? sessionEmail : null);
        gate.CurrentMemberKey.Returns(authenticated ? Guid.NewGuid() : (Guid?)null);

        var collections = Substitute.For<IUserCollection>();
        collections.GetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<UserCollectionItem>());
        collections.GetCollectionsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<UserCollectionSummary>());

        var messaging = Substitute.For<IMessagingService>();

        var controller = new ShopCatalogController(
            Substitute.For<IProductCatalogProvider>(),
            Substitute.For<IShopOrderService>(),
            Substitute.For<IPriceFormatter>(),
            collections,
            Substitute.For<IOrderTrackingService>(),
            Substitute.For<IReturnService>(),
            messaging,
            gate,
            Substitute.For<IShopQuery>(),
            Substitute.For<ICatalogSocialProof>());

        return new Harness(controller, collections, messaging);
    }

    // ── Wishlist: dato personal, dueño = sesión ────────────────────────────────

    [Fact]
    public async Task Wishlist_anonima_da_401_y_NO_toca_el_seam()
    {
        var h = Make(authenticated: false);

        var result = await h.Controller.Wishlist(collection: null, CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result);
        await h.Collections.DidNotReceiveWithAnyArgs().GetAsync(default!, default!, default);
    }

    [Fact]
    public async Task Wishlist_lee_la_lista_de_la_SESION_no_la_de_un_parametro()
    {
        var h = Make(authenticated: true, sessionEmail: Yo);

        await h.Controller.Wishlist(collection: null, CancellationToken.None);

        // El seam se consulta con el email de la sesión — no hay forma de pedir el de otro
        // porque el endpoint ya no acepta `owner`.
        await h.Collections.Received(1).GetAsync(Yo, Arg.Any<string>(), Arg.Any<CancellationToken>());
        await h.Collections.DidNotReceive().GetAsync(Atacante, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WishlistAdd_ignora_el_Owner_del_body_y_usa_la_sesion()
    {
        var h = Make(authenticated: true, sessionEmail: Yo);
        h.Collections.AddAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new UserCollectionItem(Yo, "default", "prod-1", DateTime.UtcNow));

        // El atacante pone su email en Owner intentando escribir en la lista de la víctima…
        var body = new ShopCatalogController.WishlistItemRequest(Owner: Atacante, Collection: null, ItemRef: "prod-1");
        await h.Controller.WishlistAdd(body, CancellationToken.None);

        // …pero el servidor escribe en la lista de la SESIÓN, y el Owner del body es humo.
        await h.Collections.Received(1).AddAsync(Yo, Arg.Any<string>(), "prod-1", Arg.Any<CancellationToken>());
        await h.Collections.DidNotReceive().AddAsync(Atacante, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WishlistRemove_anonima_da_401_y_NO_borra_nada()
    {
        var h = Make(authenticated: false);

        var result = await h.Controller.WishlistRemove(collection: null, itemRef: "prod-1", CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result);
        await h.Collections.DidNotReceiveWithAnyArgs().RemoveAsync(default!, default!, default!, default);
    }

    // ── Mensajería: remitente = sesión, lectura solo para participantes ─────────

    [Fact]
    public async Task Inbox_devuelve_la_bandeja_de_la_SESION_no_la_de_un_parametro()
    {
        var h = Make(authenticated: true, sessionEmail: Yo);
        h.Messaging.GetInboxAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<MessageThreadSummary>());

        await h.Controller.Inbox(CancellationToken.None);

        await h.Messaging.Received(1).GetInboxAsync(Yo, Arg.Any<CancellationToken>());
        await h.Messaging.DidNotReceive().GetInboxAsync(Atacante, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartThread_envia_como_la_SESION_aunque_el_body_diga_otro()
    {
        var h = Make(authenticated: true, sessionEmail: Yo);
        h.Messaging.StartThreadAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new MessageThread("t1", "ctx", new[] { Yo, "vendedor@x.co" }, new List<ThreadMessage>(), DateTime.UtcNow, DateTime.UtcNow));

        var body = new ShopCatalogController.StartThreadRequest(
            ContextRef: "ctx", From: Atacante, To: "vendedor@x.co", Body: "hola");
        await h.Controller.StartThread(body, CancellationToken.None);

        // El From del body (suplantación) se descarta; el remitente es la sesión.
        await h.Messaging.Received(1).StartThreadAsync("ctx", Yo, "vendedor@x.co", "hola", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetThread_de_un_hilo_AJENO_da_404_y_no_lo_revela()
    {
        var h = Make(authenticated: true, sessionEmail: Atacante);
        // El hilo es entre la víctima y un vendedor; el atacante NO participa.
        h.Messaging.GetThreadAsync("t1", Arg.Any<CancellationToken>())
            .Returns(new MessageThread("t1", "ctx", new[] { Yo, "vendedor@x.co" }, new List<ThreadMessage>(), DateTime.UtcNow, DateTime.UtcNow));

        var result = await h.Controller.GetThread("t1", CancellationToken.None);

        // 404 y no 403: no se le confirma al tercero que el hilo existe.
        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task GetThread_de_un_hilo_PROPIO_si_se_lee()
    {
        var h = Make(authenticated: true, sessionEmail: Yo);
        h.Messaging.GetThreadAsync("t1", Arg.Any<CancellationToken>())
            .Returns(new MessageThread("t1", "ctx", new[] { Yo, "vendedor@x.co" }, new List<ThreadMessage>(), DateTime.UtcNow, DateTime.UtcNow));

        var result = await h.Controller.GetThread("t1", CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task Reply_a_un_hilo_AJENO_da_403_y_NO_escribe()
    {
        var h = Make(authenticated: true, sessionEmail: Atacante);
        h.Messaging.GetThreadAsync("t1", Arg.Any<CancellationToken>())
            .Returns(new MessageThread("t1", "ctx", new[] { Yo, "vendedor@x.co" }, new List<ThreadMessage>(), DateTime.UtcNow, DateTime.UtcNow));

        var body = new ShopCatalogController.ReplyRequest(From: Atacante, Body: "inyectado");
        var result = await h.Controller.ReplyThread("t1", body, CancellationToken.None);

        Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(403, ((StatusCodeResult)result).StatusCode);
        await h.Messaging.DidNotReceiveWithAnyArgs().ReplyAsync(default!, default!, default!, default);
    }
}
