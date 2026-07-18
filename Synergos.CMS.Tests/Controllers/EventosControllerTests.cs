using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Controllers;
using Xunit;

namespace Synergos.CMS.Tests.Controllers;

/// <summary>
/// Tests de AUTORIZACIÓN de <see cref="EventosController"/> (T2-Eventos + T9). El
/// controller tenía sus 9 rutas ANÓNIMAS; estos tests fijan quién entra a cada una:
/// <list type="bullet">
/// <item><b>Público</b> — catálogo y compra (invitado, como en Tienda): el
///   <c>orderRef</c> es <c>evord_{guid}</c>, inadivinable, así que funciona como
///   credencial del invitado.</item>
/// <item><b>Sesión</b> — "mis entradas" y transferir (ownership).</item>
/// <item><b>Rol de organizador</b> — consola, crear evento y check-in: exponen datos de
///   asistentes, publican al catálogo y queman entradas ajenas.</item>
/// </list>
/// ADR 0075, ADR 0110.
/// </summary>
public sealed class EventosControllerTests
{
    private readonly IEventCatalogProvider _catalog = Substitute.For<IEventCatalogProvider>();
    private readonly IEventTicketingService _ticketing = Substitute.For<IEventTicketingService>();
    private readonly IEventManagementService _management = Substitute.For<IEventManagementService>();
    private readonly IPriceFormatter _priceFormatter = Substitute.For<IPriceFormatter>();
    private readonly IMemberAccessGate _gate = Substitute.For<IMemberAccessGate>();

    private readonly IRealtimeNotifier _realtime = Substitute.For<IRealtimeNotifier>();

    private EventosController BuildSut() => new(
        _catalog, _ticketing, _management, _priceFormatter, _gate, _realtime,
        Microsoft.Extensions.Logging.Abstractions.NullLogger<EventosController>.Instance);

    private void Anonymous() => _gate.IsAuthenticated.Returns(false);

    /// <summary>Member normal: compró entradas, pero NO organiza el evento.</summary>
    private void Attendee()
    {
        _gate.IsAuthenticated.Returns(true);
        _gate.CurrentMemberEmail.Returns("asistente@correo.co");
        _gate.HasAnyRole(Arg.Any<string?>()).Returns(false);
    }

    private void Organizer()
    {
        _gate.IsAuthenticated.Returns(true);
        _gate.CurrentMemberEmail.Returns("organizador@entidad.co");
        _gate.HasAnyRole(Arg.Any<string?>()).Returns(true);
    }

    private static void AssertForbidden(IActionResult result)
    {
        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, status.StatusCode);
    }

    // ── La CONSOLA del organizador: datos de asistentes ────────────────────────────

    [Fact] // empty: anónimo no ve quién va al evento — ni se toca el seam
    public async Task Manage_Anonymous_Returns401_AndSkipsSeam()
    {
        Anonymous();

        var result = await BuildSut().Manage("evt-1", default);

        Assert.IsType<UnauthorizedObjectResult>(result);
        await _management.DidNotReceive().GetManageAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact] // filter: un ASISTENTE logueado tampoco — comprar no te hace organizador
    public async Task Manage_AttendeeWithoutRole_Returns403_AndSkipsSeam()
    {
        Attendee();

        AssertForbidden(await BuildSut().Manage("evt-1", default));
        await _management.DidNotReceive().GetManageAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact] // happy: el organizador sí pasa el guard y llega al seam
    public async Task Manage_Organizer_PassesGuard()
    {
        Organizer();
        _management.GetManageAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new EventManageView("evt-1", Array.Empty<EventAttendee>(), 100, 0));

        var result = await BuildSut().Manage("evt-1", default);

        Assert.IsType<OkObjectResult>(result);
        await _management.Received(1).GetManageAsync("evt-1", Arg.Any<CancellationToken>());
    }

    // ── PUBLICAR un evento al catálogo ─────────────────────────────────────────────

    [Fact] // empty: publicar era anónimo — cualquiera colgaba un evento en el sitio
    public async Task CreateEvent_Anonymous_Returns401()
    {
        Anonymous();

        var result = await BuildSut().CreateEvent(
            new EventosController.EventDraftRequest("Fiesta pirata", null, default, null, null, null, null, null, null, null), default);

        Assert.IsType<UnauthorizedObjectResult>(result);
    }

    [Fact] // filter: un asistente logueado tampoco publica
    public async Task CreateEvent_AttendeeWithoutRole_Returns403()
    {
        Attendee();

        AssertForbidden(await BuildSut().CreateEvent(
            new EventosController.EventDraftRequest("Fiesta pirata", null, default, null, null, null, null, null, null, null), default));
    }

    // ── CHECK-IN: quemar una entrada es irreversible para su dueño ─────────────────

    [Fact] // empty: anónimo no quema entradas
    public async Task CheckIn_Anonymous_Returns401_AndSkipsSeam()
    {
        Anonymous();

        var result = await BuildSut().CheckIn(new EventosController.CheckInRequest("SYN-TKT-x.abc"), default);

        Assert.IsType<UnauthorizedObjectResult>(result);
        await _management.DidNotReceive().CheckInAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact] // filter: tener una entrada NO te deja quemar las de los demás
    public async Task CheckIn_AttendeeWithoutRole_Returns403_AndSkipsSeam()
    {
        Attendee();

        AssertForbidden(await BuildSut().CheckIn(new EventosController.CheckInRequest("SYN-TKT-x.abc"), default));
        await _management.DidNotReceive().CheckInAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // ── "MIS ENTRADAS": basta con la sesión, y es la del member ────────────────────

    [Fact] // empty: sin sesión no hay bandeja (antes: ?holder=<email> de cualquiera)
    public async Task Tickets_Anonymous_Returns401_AndSkipsSeam()
    {
        Anonymous();

        var result = await BuildSut().Tickets(default);

        Assert.IsType<UnauthorizedObjectResult>(result);
        await _ticketing.DidNotReceive().GetTicketsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact] // EL CASO CENTRAL: se consulta por el correo del GATE, no por uno del cliente
    public async Task Tickets_Member_UsesTheGateEmail()
    {
        Attendee();
        _ticketing.GetTicketsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<EventTicket>());

        Assert.IsType<OkObjectResult>(await BuildSut().Tickets(default));

        await _ticketing.Received(1).GetTicketsAsync("asistente@correo.co", Arg.Any<CancellationToken>());
    }

    // ── TRANSFERIR: regalar la entrada exige que sea TUYA ──────────────────────────

    [Fact] // empty: anónimo no transfiere
    public async Task Transfer_Anonymous_Returns401()
    {
        Anonymous();

        var result = await BuildSut().TransferTicket(
            "tkt_1", new EventosController.TransferRequest("otro@correo.co"), default);

        Assert.IsType<UnauthorizedObjectResult>(result);
    }

    [Fact] // EL ROBO QUE SE CIERRA: conocer el id no basta para quedarse la entrada ajena
    public async Task Transfer_ForeignTicket_Returns403_AndNeverTransfers()
    {
        Attendee();
        // Su bandeja NO contiene el ticket que intenta transferir.
        _ticketing.GetTicketsAsync("asistente@correo.co", Arg.Any<CancellationToken>())
            .Returns(Array.Empty<EventTicket>());

        AssertForbidden(await BuildSut().TransferTicket(
            "tkt_ajeno", new EventosController.TransferRequest("ladron@correo.co"), default));

        await _ticketing.DidNotReceive().TransferTicketAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // ── CONTROL: el catálogo y la compra siguen siendo PÚBLICOS ────────────────────

    [Fact] // El catálogo es la vitrina: gatearlo mataría el negocio, no lo protegería
    public async Task Events_Anonymous_IsPublic()
    {
        Anonymous();
        _catalog.SearchAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<EventSummary>());

        Assert.IsType<OkObjectResult>(await BuildSut().Events(null, default));
    }
}
