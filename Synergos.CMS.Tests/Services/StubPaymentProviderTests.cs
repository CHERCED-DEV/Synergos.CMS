using System.Threading.Tasks;
using Synergos.CMS.Application.Configuration;
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

    // ── T3 (doc 25) — durabilidad del estado + knobs de simulación ──

    [Fact] // durabilidad: el estado sobrevive al REEMPLAZO del provider (proxy de reinicio)
    public async Task State_SurvivesProviderReplacement_ViaSharedStore()
    {
        var store = new InMemoryJsonEntityStore();
        var beforeRestart = new StubPaymentProvider(store);
        var session = await beforeRestart.CreateSessionAsync(Req());

        // "Reinicio": un provider NUEVO sobre el MISMO store captura la sesión creada
        // antes. Es la prueba que fallaría con el estado en un dict del proceso.
        var afterRestart = new StubPaymentProvider(store);
        var outcome = await afterRestart.CaptureAsync(session.SessionId);

        Assert.Equal(PaymentStatus.Captured, outcome.Status);
        Assert.Equal(250_000m, outcome.AmountCaptured);
    }

    [Fact] // knob: un SKU "veneno" declina la sesión (rechazo limpio, sin excepción)
    public async Task DeclineTriggerSku_YieldsFailedSession()
    {
        var psp = new StubPaymentProvider(
            new InMemoryJsonEntityStore(),
            new PaymentsSettings { DeclineTriggerSku = "ROOM-DLX" });   // el SKU de Req()

        var session = await psp.CreateSessionAsync(Req());

        Assert.Equal(PaymentStatus.Failed, session.Status);
        // una sesión fallida no se puede capturar
        Assert.Equal(PaymentStatus.Failed, (await psp.CaptureAsync(session.SessionId)).Status);
    }

    [Fact] // knob: simula 3DS/redirect (RequiresAction + RedirectUrl sintética)
    public async Task SimulateRequiresAction_YieldsRedirect()
    {
        var psp = new StubPaymentProvider(
            new InMemoryJsonEntityStore(),
            new PaymentsSettings { SimulateRequiresAction = true });

        var session = await psp.CreateSessionAsync(Req());

        Assert.Equal(PaymentStatus.RequiresAction, session.Status);
        // ADR 0116: la acción ya no es una URL suelta. Sin método preferido el
        // stub asume tarjeta, cuyo modo es el reto embebido de 3DS2. El caso
        // redirect (PSE/Bancolombia) lo cubre PaymentSeamShapeTests.
        Assert.NotNull(session.Action);
        Assert.Equal(PaymentActionKind.InlineChallenge, session.Action!.Kind);
    }
}
