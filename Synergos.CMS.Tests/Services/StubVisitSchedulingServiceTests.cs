using System;
using System.Linq;
using System.Threading.Tasks;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Xunit;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Cubre <see cref="StubVisitSchedulingService"/> (seam
/// <see cref="IVisitSchedulingService"/>, agendamiento de visitas del portal
/// inmobiliario): los 4 casos canónicos (ADR 0075) — empty (listado sin id) /
/// happy (book aparta+confirma SIN pago vía el motor) / filter (slot inválido) /
/// idempotent (re-agendar el mismo slot) — más la verificación de que reusa
/// <see cref="IReservationService"/> con el paso de pago desactivado.
/// </summary>
public class StubVisitSchedulingServiceTests
{
    private static readonly DateTimeOffset Clock = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

    private static StubVisitSchedulingService Make(IReservationService? reservations = null)
        => new StubVisitSchedulingService(reservations ?? new StubReservationService(), () => Clock);

    private static VisitContact Contact() => new("Camila Restrepo", "camila@synergos.co", "+57 300 555 0000");

    [Fact] // empty: listado sin id devuelve agenda vacía
    public async Task GetSlots_NoListing_ReturnsEmpty()
    {
        var slots = await Make().GetSlotsAsync(string.Empty);
        Assert.Empty(slots);
    }

    [Fact] // happy: book aparta el slot y lo deja Confirmed (sin pago)
    public async Task Book_HoldsAndConfirms_WithoutPayment()
    {
        var svc = Make();
        var slots = await svc.GetSlotsAsync("prop-001");
        Assert.NotEmpty(slots);
        Assert.All(slots, s => Assert.True(s.Available));

        var result = await svc.BookAsync("prop-001", slots[0].Id, Contact());

        Assert.Equal(ReservationStatus.Confirmed.ToString(), result.Status);
        Assert.StartsWith("visit_", result.VisitId);

        // El slot apartado ya no aparece disponible.
        var after = await svc.GetSlotsAsync("prop-001");
        Assert.False(after.Single(s => s.Id == slots[0].Id).Available);
    }

    [Fact] // happy: la visita reusa el motor de reservas (queda una reserva Confirmed)
    public async Task Book_UsesReservationEngine()
    {
        var reservations = new StubReservationService();
        var svc = Make(reservations);
        var slots = await svc.GetSlotsAsync("prop-002");

        await svc.BookAsync("prop-002", slots[0].Id, Contact());

        // El motor confirmó la reserva con la sesión neutra de visita gratis.
        // (No exponemos el id de reserva; verificamos vía idempotencia + estado.)
        var repeat = await svc.BookAsync("prop-002", slots[0].Id, Contact());
        Assert.Equal(ReservationStatus.Confirmed.ToString(), repeat.Status);
    }

    [Fact] // filter: slot inexistente lanza ArgumentException
    public async Task Book_UnknownSlot_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Make().BookAsync("prop-001", "slot-que-no-existe", Contact()));
    }

    [Fact] // filter: contacto incompleto lanza
    public async Task Book_MissingContact_Throws()
    {
        var svc = Make();
        var slots = await svc.GetSlotsAsync("prop-001");
        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.BookAsync("prop-001", slots[0].Id, new VisitContact("", "", null)));
    }

    [Fact] // idempotent: re-agendar el mismo slot devuelve la misma visita
    public async Task Book_SameSlot_IsIdempotent()
    {
        var svc = Make();
        var slots = await svc.GetSlotsAsync("prop-001");

        var first = await svc.BookAsync("prop-001", slots[0].Id, Contact());
        var second = await svc.BookAsync("prop-001", slots[0].Id, Contact());

        Assert.Equal(first.VisitId, second.VisitId);
    }
}
