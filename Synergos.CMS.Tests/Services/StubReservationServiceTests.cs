using System;
using System.Threading.Tasks;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Xunit;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Cubre <see cref="StubReservationService"/> (seam
/// <see cref="IReservationService"/>, ciclo de vida de la reserva del vertical
/// Hoteles): los 4 casos canónicos (ADR 0075) —
/// inválido / happy (hold→confirm) / status (lifecycle) / idempotente (confirm).
/// </summary>
public class StubReservationServiceTests
{
    private static IReservationService Make() => new StubReservationService();

    private static ReservationRequest Req(decimal total = 440_000m)
        => new(
            "STD",
            "STD-FLEX",
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 3),
            new[] { new RoomOccupancy(2, Array.Empty<int>()) },
            "Camila Restrepo",
            "camila@synergos.co",
            total,
            "COP");

    [Fact] // inválido: total no positivo
    public async Task Hold_NonPositiveTotal_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Make().HoldAsync(Req(total: 0m)));
    }

    [Fact] // happy: hold → confirm liga la sesión de pago y deja Confirmed
    public async Task HoldThenConfirm_YieldsConfirmedWithPaymentSession()
    {
        var svc = Make();
        var held = await svc.HoldAsync(Req());
        Assert.Equal(ReservationStatus.Held, held.Status);
        Assert.Null(held.PaymentSessionId);

        var confirmed = await svc.ConfirmAsync(held.Id, "stub_session_1");
        Assert.Equal(ReservationStatus.Confirmed, confirmed.Status);
        Assert.Equal("stub_session_1", confirmed.PaymentSessionId);
    }

    [Fact] // status: Get refleja el lifecycle; cancel deja Cancelled; inexistente → null
    public async Task Get_ReflectsLifecycle()
    {
        var svc = Make();
        var held = await svc.HoldAsync(Req());

        Assert.Equal(ReservationStatus.Held, (await svc.GetAsync(held.Id))!.Status);
        await svc.CancelAsync(held.Id, "huésped canceló");
        Assert.Equal(ReservationStatus.Cancelled, (await svc.GetAsync(held.Id))!.Status);

        // reserva inexistente → null
        Assert.Null(await svc.GetAsync("nope"));
    }

    [Fact] // idempotente: confirmar dos veces deja Confirmed sin doble efecto
    public async Task Confirm_IsIdempotent()
    {
        var svc = Make();
        var held = await svc.HoldAsync(Req());

        var first = await svc.ConfirmAsync(held.Id, "stub_session_1");
        var second = await svc.ConfirmAsync(held.Id, "stub_session_2");

        Assert.Equal(ReservationStatus.Confirmed, first.Status);
        Assert.Equal(ReservationStatus.Confirmed, second.Status);
        // Conserva la sesión original — no la sobreescribe en la 2ª confirmación.
        Assert.Equal("stub_session_1", second.PaymentSessionId);
    }

    // ── Hold-timeout + auto-cancel (aprendizaje NS.Booking, doc 17) ──

    [Fact] // hold setea ExpiresAt = now + holdWindow
    public async Task Hold_SetsExpiresAt()
    {
        var now = new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);
        var svc = new StubReservationService(TimeSpan.FromMinutes(15), () => now);
        var held = await svc.HoldAsync(Req());
        Assert.Equal(now + TimeSpan.FromMinutes(15), held.ExpiresAt);
    }

    [Fact] // confirmar tras vencer falla y deja la reserva Expired
    public async Task Confirm_AfterExpiry_FailsAndExpires()
    {
        var clock = new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);
        var svc = new StubReservationService(TimeSpan.FromMinutes(15), () => clock);
        var held = await svc.HoldAsync(Req());
        clock = clock.AddMinutes(20); // pasa la ventana del hold
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.ConfirmAsync(held.Id, "s"));
        Assert.Equal(ReservationStatus.Expired, (await svc.GetAsync(held.Id))!.Status);
    }

    [Fact] // el scanner expira solo los Held vencidos (no toca vigentes ni confirmados)
    public async Task ExpireStaleHolds_ReleasesExpiredOnly()
    {
        var clock = new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);
        var svc = new StubReservationService(TimeSpan.FromMinutes(15), () => clock);
        var stale = await svc.HoldAsync(Req());
        var confirmed = await svc.HoldAsync(Req());
        await svc.ConfirmAsync(confirmed.Id, "s");
        clock = clock.AddMinutes(20);
        var fresh = await svc.HoldAsync(Req()); // creado a las 12:20 → vence 12:35

        var expired = await svc.ExpireStaleHoldsAsync();

        Assert.Equal(1, expired); // solo el stale
        Assert.Equal(ReservationStatus.Expired, (await svc.GetAsync(stale.Id))!.Status);
        Assert.Equal(ReservationStatus.Confirmed, (await svc.GetAsync(confirmed.Id))!.Status);
        Assert.Equal(ReservationStatus.Held, (await svc.GetAsync(fresh.Id))!.Status);
    }

    // ── T3 (doc 25) — durabilidad del hold ─────────────────────────

    [Fact] // durabilidad: el hold sobrevive al REEMPLAZO del servicio (proxy de reinicio)
    public async Task Hold_SurvivesServiceReplacement_ViaSharedStore()
    {
        var store = new InMemoryJsonEntityStore();
        var beforeRestart = new StubReservationService(StubReservationService.DefaultHoldWindow, null, store);
        var held = await beforeRestart.HoldAsync(Req());

        // "Reinicio": un servicio NUEVO sobre el MISMO store confirma el hold creado
        // antes. Es la prueba que fallaría ("Reserva no encontrada") con el hold en un
        // dict del proceso — el defecto que cerraba el restart-gap e2e.
        var afterRestart = new StubReservationService(StubReservationService.DefaultHoldWindow, null, store);
        var confirmed = await afterRestart.ConfirmAsync(held.Id, "stub_session_1");

        Assert.Equal(ReservationStatus.Confirmed, confirmed.Status);
        Assert.Equal("stub_session_1", confirmed.PaymentSessionId);
    }
}
