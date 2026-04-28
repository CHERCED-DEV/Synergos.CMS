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
