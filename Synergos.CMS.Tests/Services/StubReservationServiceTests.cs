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
}
