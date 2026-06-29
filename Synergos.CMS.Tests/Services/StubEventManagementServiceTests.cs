using System;
using System.Linq;
using System.Threading.Tasks;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Xunit;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Cubre <see cref="StubEventManagementService"/> (seam <see cref="IEventManagementService"/>,
/// cara de organizador del vertical Eventos): los 4 casos canónicos (ADR 0075) —
/// empty (sin ventas) / happy (dashboard tras confirmar) / filter (check-in inválido) /
/// idempotent (check-in repetido → already-used). Compone el StubEventTicketingService
/// concreto (DIP) como fuente de verdad de los tickets.
/// </summary>
public class StubEventManagementServiceTests
{
    private static (IEventManagementService Mgmt, StubEventTicketingService Ticketing) Make()
    {
        var catalog = new StubEventCatalogProvider();
        var ticketing = new StubEventTicketingService(
            catalog, new StubReservationService(), new StubPaymentProvider());
        var mgmt = new StubEventManagementService(ticketing, catalog);
        return (mgmt, ticketing);
    }

    private static EventAttendeeInfo Attendee(string s = "1")
        => new($"Asistente {s}", $"asistente{s}@synergos.co", null);

    [Fact] // empty: evento sin ventas → 0 vendidos, asistentes vacío, aforo > 0
    public async Task GetManage_NoSales_EmptyButHasCapacity()
    {
        var (mgmt, _) = Make();
        var view = await mgmt.GetManageAsync("evt-festival-estereo");

        Assert.Empty(view.Attendees);
        Assert.Equal(0, view.Sold);
        Assert.True(view.Capacity > 0);
    }

    [Fact] // happy: tras confirmar, el dashboard lista los asistentes y cuenta vendidos
    public async Task GetManage_AfterConfirm_ListsAttendees()
    {
        var (mgmt, ticketing) = Make();
        var checkout = await ticketing.CheckoutAsync("evt-festival-estereo",
            new[] { new EventCheckoutItem("GEN", null, 2) },
            new[] { Attendee("1"), Attendee("2") });
        await ticketing.ConfirmAsync(checkout.OrderRef);

        var view = await mgmt.GetManageAsync("evt-festival-estereo");

        Assert.Equal(2, view.Sold);
        Assert.Equal(2, view.Attendees.Count);
        Assert.All(view.Attendees, a => Assert.False(a.CheckedIn)); // aún sin check-in
    }

    [Fact] // filter/invalid: check-in de un ticket inexistente → invalid; evento inexistente lanza
    public async Task CheckIn_Unknown_Invalid_AndUnknownEventThrows()
    {
        var (mgmt, _) = Make();

        var result = await mgmt.CheckInAsync("tkt_no-existe");
        Assert.Equal("invalid", result.Status);

        await Assert.ThrowsAsync<ArgumentException>(() => mgmt.GetManageAsync("nope"));
    }

    [Fact] // idempotent: primer check-in valid, el segundo already-used; marca CheckedIn
    public async Task CheckIn_IsIdempotent()
    {
        var (mgmt, ticketing) = Make();
        var checkout = await ticketing.CheckoutAsync("evt-festival-estereo",
            new[] { new EventCheckoutItem("GEN", null, 1) }, new[] { Attendee() });
        await ticketing.ConfirmAsync(checkout.OrderRef);

        var ticketId = (await mgmt.GetManageAsync("evt-festival-estereo")).Attendees.Single().TicketId;

        Assert.Equal("valid", (await mgmt.CheckInAsync(ticketId)).Status);
        Assert.Equal("already-used", (await mgmt.CheckInAsync(ticketId)).Status);

        // el dashboard refleja la marca de asistencia
        var attendee = (await mgmt.GetManageAsync("evt-festival-estereo")).Attendees.Single();
        Assert.True(attendee.CheckedIn);
    }
}
