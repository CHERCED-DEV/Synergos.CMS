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
    /// <summary>Firmante real (T9): sin él el servicio es fail-closed y no emite QR.</summary>
    private static readonly ITicketSigner Signer =
        new HmacTicketSigner(System.Text.Encoding.UTF8.GetBytes("llave-de-tests-t9"));

    private static (IEventManagementService Mgmt, StubEventTicketingService Ticketing) Make()
    {
        var catalog = new StubEventCatalogProvider();
        var ticketing = new StubEventTicketingService(
            catalog, new StubReservationService(), new StubPaymentProvider(),
            null, null, null, null, signer: Signer);
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
        var confirmation = await ticketing.ConfirmAsync(checkout.OrderRef);

        // T9: se entra con el TOKEN FIRMADO del QR, que es la credencial.
        var qr = confirmation.Tickets.Single().Qr;

        Assert.Equal("valid", (await mgmt.CheckInAsync(qr)).Status);
        Assert.Equal("already-used", (await mgmt.CheckInAsync(qr)).Status);

        // el dashboard refleja la marca de asistencia
        var attendee = (await mgmt.GetManageAsync("evt-festival-estereo")).Attendees.Single();
        Assert.True(attendee.CheckedIn);
    }

    [Fact] // T9 — EL AGUJERO QUE SE CIERRA: el ticketId suelto ya no abre la puerta.
           // La UI imprime ese id bajo el QR, así que antes bastaba una foto del ticket
           // ajeno para entrar en su lugar (el check-in ni miraba el QR).
    public async Task CheckIn_WithBareTicketId_IsRejected()
    {
        var (mgmt, ticketing) = Make();
        var checkout = await ticketing.CheckoutAsync("evt-festival-estereo",
            new[] { new EventCheckoutItem("GEN", null, 1) }, new[] { Attendee() });
        var confirmation = await ticketing.ConfirmAsync(checkout.OrderRef);
        var ticket = confirmation.Tickets.Single();

        // El id que la UI muestra en claro: rechazado.
        Assert.Equal("invalid", (await mgmt.CheckInAsync(ticket.Id)).Status);
        // Un token inventado con el formato correcto pero sin firma válida: rechazado.
        Assert.Equal("invalid", (await mgmt.CheckInAsync(
            $"SYN-TKT-evt-festival-estereo-{ticket.Id}-v0.deadbeef")).Status);

        // Control: la entrada sigue siendo válida con su token real (no la quemamos).
        Assert.Equal("valid", (await mgmt.CheckInAsync(ticket.Qr)).Status);
    }

    // ── OLA 3 — Crear evento (organizador) ──────────────────────────────

    [Fact] // happy: crear un evento lo publica y queda buscable + con ficha resoluble
    public async Task CreateEvent_PublishesToCatalog()
    {
        var (mgmt, _) = Make();
        var draft = new EventDraft(
            Name: "Concierto de Prueba",
            Venue: "Movistar Arena",
            Date: DateTimeOffset.UtcNow.AddDays(45),
            Tiers: new[]
            {
                new EventTierDraft("General", 120_000m, 500),
                new EventTierDraft("VIP", 300_000m, 100),
            });

        var result = await mgmt.CreateEventAsync(draft);
        Assert.False(string.IsNullOrWhiteSpace(result.EventId));

        // El dashboard del evento recién creado resuelve (aforo = suma de tiers, 0 vendidos).
        var view = await mgmt.GetManageAsync(result.EventId);
        Assert.Equal(600, view.Capacity);
        Assert.Equal(0, view.Sold);
    }

    [Fact] // filter/invalid: sin tiers o aforo <= 0 lanza
    public async Task CreateEvent_InvalidDraft_Throws()
    {
        var (mgmt, _) = Make();

        // sin tiers
        await Assert.ThrowsAsync<ArgumentException>(() => mgmt.CreateEventAsync(
            new EventDraft("Sin tiers", "Venue", DateTimeOffset.UtcNow, Array.Empty<EventTierDraft>())));

        // aforo <= 0
        await Assert.ThrowsAsync<ArgumentException>(() => mgmt.CreateEventAsync(
            new EventDraft("Aforo cero", "Venue", DateTimeOffset.UtcNow,
                new[] { new EventTierDraft("General", 10_000m, 0) })));
    }
}
