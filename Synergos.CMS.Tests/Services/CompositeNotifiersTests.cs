using Microsoft.Extensions.Logging.Abstractions;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Tests para los 3 composite notifiers (Olas 89-90 + 91 + 102).
/// Verifica dispatch fan-out + isolation entre canales (un canal
/// roto no afecta a los demás).
/// </summary>
public sealed class CompositeNotifiersTests
{
    [Fact]
    public async Task CompositeCommentModeration_DispatchesToAllChannels()
    {
        var ch1 = new CountingCommentChannel();
        var ch2 = new CountingCommentChannel();
        var ch3 = new CountingCommentChannel();
        var sut = new CompositeCommentModerationNotifier(
            new[] { (ICommentModerationNotifierChannel)ch1, ch2, ch3 },
            NullLogger<CompositeCommentModerationNotifier>.Instance);

        await sut.NotifyPendingAsync(
            new Comment("c1", 42, null, "Author", "Body", DateTime.UtcNow, false),
            CancellationToken.None);

        Assert.Equal(1, ch1.Calls);
        Assert.Equal(1, ch2.Calls);
        Assert.Equal(1, ch3.Calls);
    }

    [Fact]
    public async Task CompositeCommentModeration_BrokenChannelDoesNotAbortOthers()
    {
        var ch1 = new CountingCommentChannel();
        var broken = new ThrowingCommentChannel();
        var ch3 = new CountingCommentChannel();
        var sut = new CompositeCommentModerationNotifier(
            new[] { (ICommentModerationNotifierChannel)ch1, broken, ch3 },
            NullLogger<CompositeCommentModerationNotifier>.Instance);

        // Should not throw despite broken channel.
        await sut.NotifyPendingAsync(
            new Comment("c1", 42, null, "Author", "Body", DateTime.UtcNow, false),
            CancellationToken.None);

        Assert.Equal(1, ch1.Calls);
        Assert.Equal(1, ch3.Calls);
    }

    [Fact]
    public async Task CompositeCommentModeration_EmptyChannels_NoOp()
    {
        var sut = new CompositeCommentModerationNotifier(
            Array.Empty<ICommentModerationNotifierChannel>(),
            NullLogger<CompositeCommentModerationNotifier>.Instance);

        // No exception when zero channels registered.
        await sut.NotifyPendingAsync(
            new Comment("c1", 42, null, "Author", "Body", DateTime.UtcNow, false),
            CancellationToken.None);
    }

    [Fact]
    public async Task CompositeFormSubmission_DispatchesToAllChannels()
    {
        var ch1 = new CountingFormChannel();
        var ch2 = new CountingFormChannel();
        var sut = new CompositeFormSubmissionNotifier(
            new[] { (IFormSubmissionNotifierChannel)ch1, ch2 },
            NullLogger<CompositeFormSubmissionNotifier>.Instance);

        await sut.NotifySubmittedAsync(
            new FormSubmissionRequest(
                FormKey: "contact",
                Fields: new Dictionary<string, string> { ["name"] = "x" },
                ClientIp: null,
                UserAgent: null,
                Referrer: null,
                ReceivedAtUtc: DateTime.UtcNow),
            FormSubmissionResult.Ok("storage-ref"),
            CancellationToken.None);

        Assert.Equal(1, ch1.Calls);
        Assert.Equal(1, ch2.Calls);
    }

    [Fact]
    public async Task CompositeCartAbandonment_DispatchesToAllChannels()
    {
        var ch1 = new CountingCartChannel();
        var ch2 = new CountingCartChannel();
        var ch3 = new CountingCartChannel();
        var sut = new CompositeCartAbandonmentNotifier(
            new[] { (ICartAbandonmentNotifierChannel)ch1, ch2, ch3 },
            NullLogger<CompositeCartAbandonmentNotifier>.Instance);

        await sut.NotifyAbandonedAsync(
            new AbandonedCart(
                CartId: "cart-1",
                ItemCount: 3,
                Subtotal: 187500m,
                Currency: "COP",
                LastActivityUtc: DateTime.UtcNow.AddMinutes(-30)),
            CancellationToken.None);

        Assert.Equal(1, ch1.Calls);
        Assert.Equal(1, ch2.Calls);
        Assert.Equal(1, ch3.Calls);
    }

    [Fact]
    public async Task CompositeCartAbandonment_BrokenChannelDoesNotAbortOthers()
    {
        var ch1 = new CountingCartChannel();
        var broken = new ThrowingCartChannel();
        var ch3 = new CountingCartChannel();
        var sut = new CompositeCartAbandonmentNotifier(
            new[] { (ICartAbandonmentNotifierChannel)ch1, broken, ch3 },
            NullLogger<CompositeCartAbandonmentNotifier>.Instance);

        await sut.NotifyAbandonedAsync(
            new AbandonedCart(
                CartId: "cart-1",
                ItemCount: 3,
                Subtotal: 187500m,
                Currency: "COP",
                LastActivityUtc: DateTime.UtcNow.AddMinutes(-30)),
            CancellationToken.None);

        Assert.Equal(1, ch1.Calls);
        Assert.Equal(1, ch3.Calls);
    }

    [Fact]
    public async Task CompositeAlert_DispatchesAlertToAllChannels()
    {
        var ch1 = new CountingAlertChannel();
        var ch2 = new CountingAlertChannel();
        var ch3 = new CountingAlertChannel();
        var sut = new CompositeAlertNotifier(
            new[] { (IAlertNotifierChannel)ch1, ch2, ch3 },
            NullLogger<CompositeAlertNotifier>.Instance);

        await sut.NotifyAlertAsync(
            new WebhookAlertEvent("ch", 0.5, 0.2, 1000, 500, 500,
                100, 300, 800, DateTime.UtcNow, 60),
            CancellationToken.None);

        Assert.Equal(1, ch1.AlertCalls);
        Assert.Equal(1, ch2.AlertCalls);
        Assert.Equal(1, ch3.AlertCalls);
    }

    [Fact]
    public async Task CompositeAlert_DispatchesRecoveryToAllChannels()
    {
        var ch1 = new CountingAlertChannel();
        var ch2 = new CountingAlertChannel();
        var sut = new CompositeAlertNotifier(
            new[] { (IAlertNotifierChannel)ch1, ch2 },
            NullLogger<CompositeAlertNotifier>.Instance);

        await sut.NotifyRecoveryAsync(
            new WebhookRecoveryEvent("ch", 0.05, 0.5, 0.2,
                TimeSpan.FromMinutes(70),
                DateTime.UtcNow.AddMinutes(-70),
                DateTime.UtcNow.AddMinutes(-10),
                1000, 950, 50),
            CancellationToken.None);

        Assert.Equal(1, ch1.RecoveryCalls);
        Assert.Equal(1, ch2.RecoveryCalls);
    }

    [Fact]
    public async Task CompositeAlert_BrokenChannelDoesNotAbortOthers()
    {
        var ch1 = new CountingAlertChannel();
        var broken = new ThrowingAlertChannel();
        var ch3 = new CountingAlertChannel();
        var sut = new CompositeAlertNotifier(
            new[] { (IAlertNotifierChannel)ch1, broken, ch3 },
            NullLogger<CompositeAlertNotifier>.Instance);

        await sut.NotifyAlertAsync(
            new WebhookAlertEvent("ch", 0.5, 0.2, 1000, 500, 500,
                100, 300, 800, DateTime.UtcNow, 60),
            CancellationToken.None);

        Assert.Equal(1, ch1.AlertCalls);
        Assert.Equal(1, ch3.AlertCalls);
    }

    [Fact]
    public async Task CompositeAlert_EmptyChannels_NoOp()
    {
        var sut = new CompositeAlertNotifier(
            Array.Empty<IAlertNotifierChannel>(),
            NullLogger<CompositeAlertNotifier>.Instance);

        await sut.NotifyAlertAsync(
            new WebhookAlertEvent("ch", 0.5, 0.2, 1000, 500, 500,
                100, 300, 800, DateTime.UtcNow, 60),
            CancellationToken.None);
    }

    private sealed class CountingAlertChannel : IAlertNotifierChannel
    {
        public int AlertCalls { get; private set; }
        public int RecoveryCalls { get; private set; }
        public Task NotifyAlertAsync(WebhookAlertEvent alert, CancellationToken cancellationToken)
        {
            AlertCalls++;
            return Task.CompletedTask;
        }
        public Task NotifyRecoveryAsync(WebhookRecoveryEvent recovery, CancellationToken cancellationToken)
        {
            RecoveryCalls++;
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingAlertChannel : IAlertNotifierChannel
    {
        public Task NotifyAlertAsync(WebhookAlertEvent alert, CancellationToken cancellationToken)
            => throw new InvalidOperationException("simulated alert channel failure");
        public Task NotifyRecoveryAsync(WebhookRecoveryEvent recovery, CancellationToken cancellationToken)
            => throw new InvalidOperationException("simulated alert channel failure");
    }

    private sealed class CountingCommentChannel : ICommentModerationNotifierChannel
    {
        public int Calls { get; private set; }
        public Task NotifyPendingAsync(Comment comment, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingCommentChannel : ICommentModerationNotifierChannel
    {
        public Task NotifyPendingAsync(Comment comment, CancellationToken cancellationToken)
            => throw new InvalidOperationException("simulated channel failure");
    }

    private sealed class CountingFormChannel : IFormSubmissionNotifierChannel
    {
        public int Calls { get; private set; }
        public Task NotifySubmittedAsync(FormSubmissionRequest request, FormSubmissionResult result, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.CompletedTask;
        }
    }

    private sealed class CountingCartChannel : ICartAbandonmentNotifierChannel
    {
        public int Calls { get; private set; }
        public Task NotifyAbandonedAsync(AbandonedCart cart, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingCartChannel : ICartAbandonmentNotifierChannel
    {
        public Task NotifyAbandonedAsync(AbandonedCart cart, CancellationToken cancellationToken)
            => throw new InvalidOperationException("simulated channel failure");
    }
}
