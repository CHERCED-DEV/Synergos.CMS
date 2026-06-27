using System.Threading.Tasks;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Xunit;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Cubre <see cref="StubPaymentProvider"/> (seam <see cref="IPaymentProvider"/>,
/// pieza de pago del motor): los 4 casos canónicos (ADR 0075) —
/// inválido / happy (create→capture) / status / idempotente.
/// </summary>
public class StubPaymentProviderTests
{
    private static IPaymentProvider Make() => new StubPaymentProvider();

    private static PaymentSessionRequest Req(decimal amount = 250_000m)
        => new("ORD-1", amount, "COP",
            new[] { new PaymentLineItem("ROOM-DLX", "Habitación Deluxe · 2 noches", amount, 1) },
            "huesped@synergos.co");

    [Fact] // inválido / empty
    public async Task CreateSession_NonPositiveAmount_Throws()
    {
        await Assert.ThrowsAsync<System.ArgumentException>(
            () => Make().CreateSessionAsync(Req(amount: 0m)));
    }

    [Fact] // happy: create → capture
    public async Task CreateThenCapture_YieldsCaptured()
    {
        var psp = Make();
        var session = await psp.CreateSessionAsync(Req());
        Assert.Equal(PaymentStatus.Authorized, session.Status);
        Assert.Equal("stub", session.ProviderKey);

        var outcome = await psp.CaptureAsync(session.SessionId);
        Assert.Equal(PaymentStatus.Captured, outcome.Status);
        Assert.Equal(250_000m, outcome.AmountCaptured);
    }

    [Fact] // status
    public async Task GetStatus_ReflectsLifecycle()
    {
        var psp = Make();
        var session = await psp.CreateSessionAsync(Req());

        Assert.Equal(PaymentStatus.Authorized, (await psp.GetStatusAsync(session.SessionId)).Status);
        await psp.CaptureAsync(session.SessionId);
        Assert.Equal(PaymentStatus.Captured, (await psp.GetStatusAsync(session.SessionId)).Status);

        // sesión inexistente → Failed (no encontrada)
        Assert.Equal(PaymentStatus.Failed, (await psp.GetStatusAsync("nope")).Status);
    }

    [Fact] // idempotente: capturar dos veces no duplica el cobro
    public async Task Capture_IsIdempotent()
    {
        var psp = Make();
        var session = await psp.CreateSessionAsync(Req());

        var first = await psp.CaptureAsync(session.SessionId);
        var second = await psp.CaptureAsync(session.SessionId);

        Assert.Equal(PaymentStatus.Captured, first.Status);
        Assert.Equal(PaymentStatus.Captured, second.Status);
        Assert.Equal(250_000m, second.AmountCaptured); // mismo monto, no 500.000
    }
}
