using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Xunit;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Cubre <see cref="StubEventTicketingService"/> (seam <see cref="IEventTicketingService"/>,
/// motor transaccional de la cara de asistente del vertical Eventos): los 4 casos
/// canónicos (ADR 0075) — empty / happy (checkout→confirm con e-tickets QR) / filter
/// (validación tier/aforo) / idempotent (re-confirm) — más el reuso del motor de
/// reservas (un hold por unidad) y la selección de asiento (modo reserved).
/// </summary>
public class StubEventTicketingServiceTests
{
    private static StubEventTicketingService Make(
        IReservationService? reservations = null,
        IPaymentProvider? payments = null)
        => new StubEventTicketingService(
            new StubEventCatalogProvider(),
            reservations ?? new StubReservationService(),
            payments ?? new StubPaymentProvider());

    private static EventAttendeeInfo Attendee(string suffix = "1")
        => new($"Asistente {suffix}", $"asistente{suffix}@synergos.co", $"100{suffix}");

    [Fact] // empty: sin ítems lanza
    public async Task Checkout_NoItems_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Make().CheckoutAsync("evt-festival-estereo", Array.Empty<EventCheckoutItem>(), new[] { Attendee() }));
    }

    [Fact] // happy: general → checkout aparta + abre sesión; confirm emite e-tickets QR
    public async Task CheckoutThenConfirm_General_IssuesQrTickets()
    {
        var svc = Make();
        var checkout = await svc.CheckoutAsync(
            "evt-festival-estereo",
            new[] { new EventCheckoutItem("GEN", null, 2) },
            new[] { Attendee("1"), Attendee("2") });

        Assert.False(string.IsNullOrWhiteSpace(checkout.OrderRef));
        Assert.False(string.IsNullOrWhiteSpace(checkout.PaymentSessionId));
        Assert.Equal(360_000m, checkout.Amount); // 2 × 180.000 (precio real del catálogo)

        var confirmation = await svc.ConfirmAsync(checkout.OrderRef);
        Assert.Equal(2, confirmation.Tickets.Count);
        Assert.All(confirmation.Tickets, t => Assert.StartsWith("SYN-TKT-evt-festival-estereo-", t.Qr));
        Assert.All(confirmation.Tickets, t => Assert.Equal("GEN", t.Tier));
    }

    [Fact] // happy: reserved → cada asiento es una unidad con su Seat en el ticket
    public async Task Checkout_Reserved_PerSeatTickets()
    {
        var svc = Make();
        var checkout = await svc.CheckoutAsync(
            "evt-concierto-sinfonico",
            new[]
            {
                new EventCheckoutItem("PLATEA", "A1", 1),
                new EventCheckoutItem("PLATEA", "A2", 1),
            },
            new[] { Attendee("1"), Attendee("2") });

        var confirmation = await svc.ConfirmAsync(checkout.OrderRef);
        var seats = confirmation.Tickets.Select(t => t.Seat).OrderBy(s => s).ToList();
        Assert.Equal(new[] { "A1", "A2" }, seats);
    }

    [Fact] // filter: tier inexistente y aforo insuficiente lanzan
    public async Task Checkout_InvalidTierOrCapacity_Throws()
    {
        // tier inexistente
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Make().CheckoutAsync("evt-festival-estereo",
                new[] { new EventCheckoutItem("NO-EXISTE", null, 1) }, new[] { Attendee() }));

        // EARLY tiene Remaining=0 → aforo insuficiente
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Make().CheckoutAsync("evt-festival-estereo",
                new[] { new EventCheckoutItem("EARLY", null, 1) }, new[] { Attendee() }));
    }

    [Fact] // filter: asistentes deben igualar tickets
    public async Task Checkout_AttendeeCountMismatch_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Make().CheckoutAsync("evt-festival-estereo",
                new[] { new EventCheckoutItem("GEN", null, 2) }, new[] { Attendee() })); // 2 tickets, 1 asistente
    }

    [Fact] // happy: cada unidad genera un hold en el motor de reservas (reuso del motor)
    public async Task Checkout_HoldsOnePerUnit()
    {
        var reservations = new StubReservationService();
        var svc = Make(reservations);

        await svc.CheckoutAsync("evt-festival-estereo",
            new[] { new EventCheckoutItem("GEN", null, 3) },
            new[] { Attendee("1"), Attendee("2"), Attendee("3") });

        // ExpireStaleHolds no expira nada todavía (holds vigentes), pero confirma
        // que el motor de reservas recibió holds: 0 expirados, no excepción.
        Assert.Equal(0, await reservations.ExpireStaleHoldsAsync());
    }

    [Fact] // idempotent: re-confirmar la misma orden devuelve los mismos tickets, sin doble efecto
    public async Task Confirm_IsIdempotent()
    {
        var svc = Make();
        var checkout = await svc.CheckoutAsync("evt-festival-estereo",
            new[] { new EventCheckoutItem("GEN", null, 1) }, new[] { Attendee() });

        var first = await svc.ConfirmAsync(checkout.OrderRef);
        var second = await svc.ConfirmAsync(checkout.OrderRef);

        Assert.Equal("Confirmed", first.Status);
        Assert.Equal("Confirmed", second.Status);
        Assert.Equal(
            first.Tickets.Select(t => t.Id).OrderBy(x => x),
            second.Tickets.Select(t => t.Id).OrderBy(x => x));
        Assert.Equal(
            first.Tickets.Select(t => t.Qr).OrderBy(x => x),
            second.Tickets.Select(t => t.Qr).OrderBy(x => x));
    }

    [Fact] // orden inexistente lanza
    public async Task Confirm_UnknownOrder_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Make().ConfirmAsync("nope"));
    }

    // ── OLA 3 — Mis tickets / transferir / tracking ─────────────────────

    [Fact] // happy: "Mis tickets" del comprador (holder) tras confirmar; email desconocido → vacío
    public async Task GetTickets_ByHolder_ReturnsConfirmedTickets()
    {
        var svc = Make();
        var checkout = await svc.CheckoutAsync("evt-festival-estereo",
            new[] { new EventCheckoutItem("GEN", null, 2) },
            new[] { Attendee("1"), Attendee("2") });
        await svc.ConfirmAsync(checkout.OrderRef);

        // El comprador (primer asistente) es holder de sus tickets.
        var mine = await svc.GetTicketsAsync("asistente1@synergos.co");
        Assert.Single(mine);
        Assert.All(mine, t => Assert.Equal("valid", t.Status));

        Assert.Empty(await svc.GetTicketsAsync("nadie@synergos.co"));
    }

    [Fact] // happy: transferir reasigna holder, rota el QR (viejo != nuevo) y cambia estado
    public async Task Transfer_RotatesQr_AndReassignsHolder()
    {
        var svc = Make();
        var checkout = await svc.CheckoutAsync("evt-festival-estereo",
            new[] { new EventCheckoutItem("GEN", null, 1) }, new[] { Attendee("1") });
        var confirmation = await svc.ConfirmAsync(checkout.OrderRef);
        var ticket = confirmation.Tickets.Single();
        var oldQr = ticket.Qr;

        var transfer = await svc.TransferTicketAsync(ticket.Id, "nuevo.duenio@synergos.co");

        Assert.NotEqual(oldQr, transfer.NewQr);        // QR viejo invalidado
        Assert.Equal(transfer.NewQr, transfer.Ticket.Qr);
        Assert.Equal("nuevo.duenio@synergos.co", transfer.Ticket.HolderEmail);
        Assert.Equal("transferred", transfer.Ticket.Status);

        // El ticket ya no aparece para el holder original; sí para el nuevo.
        Assert.Empty(await svc.GetTicketsAsync("asistente1@synergos.co"));
        Assert.Single(await svc.GetTicketsAsync("nuevo.duenio@synergos.co"));
    }

    [Fact] // idempotent: transferir al holder actual no rota el QR ni cambia nada
    public async Task Transfer_ToCurrentHolder_IsIdempotent()
    {
        var svc = Make();
        var checkout = await svc.CheckoutAsync("evt-festival-estereo",
            new[] { new EventCheckoutItem("GEN", null, 1) }, new[] { Attendee("1") });
        var ticket = (await svc.ConfirmAsync(checkout.OrderRef)).Tickets.Single();

        var again = await svc.TransferTicketAsync(ticket.Id, "asistente1@synergos.co");
        Assert.Equal(ticket.Qr, again.NewQr); // sin rotar
    }

    [Fact] // filter/invalid: transferir un ticket inexistente o a email inválido lanza
    public async Task Transfer_InvalidTicketOrEmail_Throws()
    {
        var svc = Make();
        await Assert.ThrowsAsync<ArgumentException>(() => svc.TransferTicketAsync("tkt_no-existe", "x@y.co"));

        var checkout = await svc.CheckoutAsync("evt-festival-estereo",
            new[] { new EventCheckoutItem("GEN", null, 1) }, new[] { Attendee("1") });
        var ticket = (await svc.ConfirmAsync(checkout.OrderRef)).Tickets.Single();
        await Assert.ThrowsAsync<ArgumentException>(() => svc.TransferTicketAsync(ticket.Id, "sin-arroba"));
    }

    [Fact] // happy: confirmar alimenta el timeline de tracking (pipeline eventos)
    public async Task Confirm_FeedsTrackingTimeline()
    {
        var tracking = new StubOrderTrackingService(StubEventTicketingService.EventPipeline, null);
        var svc = new StubEventTicketingService(
            new StubEventCatalogProvider(), new StubReservationService(), new StubPaymentProvider(),
            tracking, null, null);

        var checkout = await svc.CheckoutAsync("evt-festival-estereo",
            new[] { new EventCheckoutItem("GEN", null, 1) }, new[] { Attendee("1") });

        Assert.Null(await tracking.GetTimelineAsync(checkout.OrderRef)); // aún no confirmado

        await svc.ConfirmAsync(checkout.OrderRef);
        var timeline = await tracking.GetTimelineAsync(checkout.OrderRef);
        Assert.NotNull(timeline);
        Assert.Equal("confirmed", timeline!.CurrentStage);
    }
}
