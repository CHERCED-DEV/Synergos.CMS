using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Tests para <see cref="WebhookTelemetryAlertScanner"/> (Cap-240
/// Batch C, Ola 237). Cubre alert firing, cooldown, y recovery
/// emails cuando un canal previamente alerting vuelve a healthy
/// (cierra deferred §11.20 #10).
/// </summary>
public sealed class WebhookTelemetryAlertScannerTests
{
    private readonly IWebhookTelemetryStore _store = Substitute.For<IWebhookTelemetryStore>();
    private readonly IEmailService _email = Substitute.For<IEmailService>();
    private readonly FakeTimeProvider _time = new();

    private WebhookTelemetryAlertScanner BuildSut(
        bool enabled = true,
        string alertEmail = "ops@example.com",
        double threshold = 0.20,
        int minSample = 100,
        int cooldownMinutes = 60,
        bool recoveryEnabled = true)
    {
        var settings = Options.Create(new WebhookTelemetryAlertSettings
        {
            Enabled = enabled,
            AlertEmail = alertEmail,
            FailRateThreshold = threshold,
            MinimumSampleSize = minSample,
            CooldownMinutes = cooldownMinutes,
            CheckIntervalMinutes = 5,
            RecoveryEmailEnabled = recoveryEnabled,
        });
        var monitor = Substitute.For<IOptionsMonitor<WebhookTelemetryAlertSettings>>();
        monitor.CurrentValue.Returns(settings.Value);
        return new WebhookTelemetryAlertScanner(
            _store, _email, monitor,
            NullLogger<WebhookTelemetryAlertScanner>.Instance,
            _time);
    }

    private static ChannelTelemetrySnapshot Snap(
        string name, long total, long failures, DateTime? lastObserved = null)
        => new(
            ChannelName: name,
            TotalCalls: total,
            SuccessCount: total - failures,
            FailureCount: failures,
            P50LatencyMs: 100, P95LatencyMs: 300, P99LatencyMs: 800,
            LastObservedUtc: lastObserved ?? DateTime.UtcNow);

    [Fact]
    public async Task ScanAndDispatchAsync_NotEnabled_NoOp()
    {
        var sut = BuildSut(enabled: false);
        _store.GetChannelStats().Returns(new[] { Snap("ch", 1000, 500) });

        await sut.ScanAndDispatchAsync(CancellationToken.None);

        await _email.DidNotReceive().SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ScanAndDispatchAsync_BelowMinimumSample_NoAlert()
    {
        var sut = BuildSut(minSample: 100);
        _store.GetChannelStats().Returns(new[] { Snap("ch", 50, 30) }); // 60% but only 50 samples

        await sut.ScanAndDispatchAsync(CancellationToken.None);

        await _email.DidNotReceive().SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ScanAndDispatchAsync_BelowThreshold_NoAlert()
    {
        var sut = BuildSut(threshold: 0.20);
        _store.GetChannelStats().Returns(new[] { Snap("ch", 1000, 100) }); // 10%

        await sut.ScanAndDispatchAsync(CancellationToken.None);

        await _email.DidNotReceive().SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ScanAndDispatchAsync_AboveThreshold_FiresAlert()
    {
        var sut = BuildSut();
        _store.GetChannelStats().Returns(new[] { Snap("ch", 1000, 500) }); // 50%

        await sut.ScanAndDispatchAsync(CancellationToken.None);

        await _email.Received(1).SendAsync(
            Arg.Is<EmailMessage>(m => m.Subject.Contains("fail rate breach") && m.To == "ops@example.com"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ScanAndDispatchAsync_WithinCooldown_DoesNotReFire()
    {
        var sut = BuildSut(cooldownMinutes: 60);
        _store.GetChannelStats().Returns(new[] { Snap("ch", 1000, 500) });

        await sut.ScanAndDispatchAsync(CancellationToken.None);
        _time.Advance(TimeSpan.FromMinutes(10));
        await sut.ScanAndDispatchAsync(CancellationToken.None);

        await _email.Received(1).SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ScanAndDispatchAsync_PostCooldown_ReFires()
    {
        var sut = BuildSut(cooldownMinutes: 60);
        _store.GetChannelStats().Returns(new[] { Snap("ch", 1000, 500) });

        await sut.ScanAndDispatchAsync(CancellationToken.None);
        _time.Advance(TimeSpan.FromMinutes(61));
        await sut.ScanAndDispatchAsync(CancellationToken.None);

        await _email.Received(2).SendAsync(
            Arg.Is<EmailMessage>(m => m.Subject.Contains("fail rate breach")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ScanAndDispatchAsync_RecoveryFires_WhenChannelReturnsHealthy()
    {
        var sut = BuildSut();

        // Tick 1: alerting (50%)
        _store.GetChannelStats().Returns(new[] { Snap("ch", 1000, 500) });
        await sut.ScanAndDispatchAsync(CancellationToken.None);

        _time.Advance(TimeSpan.FromMinutes(70));

        // Tick 2: healthy (5%)
        _store.GetChannelStats().Returns(new[] { Snap("ch", 1000, 50) });
        await sut.ScanAndDispatchAsync(CancellationToken.None);

        await _email.Received(1).SendAsync(
            Arg.Is<EmailMessage>(m => m.Subject.Contains("fail rate breach")),
            Arg.Any<CancellationToken>());
        await _email.Received(1).SendAsync(
            Arg.Is<EmailMessage>(m => m.Subject.Contains("recovered")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ScanAndDispatchAsync_NoRecoveryEmail_IfChannelNeverAlerted()
    {
        var sut = BuildSut();
        _store.GetChannelStats().Returns(new[] { Snap("ch", 1000, 50) }); // healthy from the start

        await sut.ScanAndDispatchAsync(CancellationToken.None);

        await _email.DidNotReceive().SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ScanAndDispatchAsync_RecoveryDisabled_NoEmailButStateClears()
    {
        var sut = BuildSut(recoveryEnabled: false);

        _store.GetChannelStats().Returns(new[] { Snap("ch", 1000, 500) });
        await sut.ScanAndDispatchAsync(CancellationToken.None);
        _time.Advance(TimeSpan.FromMinutes(70));

        // Healthy now — recovery should NOT email but state should clear.
        _store.GetChannelStats().Returns(new[] { Snap("ch", 1000, 50) });
        await sut.ScanAndDispatchAsync(CancellationToken.None);

        await _email.Received(1).SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
        await _email.DidNotReceive().SendAsync(
            Arg.Is<EmailMessage>(m => m.Subject.Contains("recovered")),
            Arg.Any<CancellationToken>());

        // Verifica que el state se limpió: si vuelve a alerting, dispara
        // como first-time (no como re-fire dentro de cooldown).
        _time.Advance(TimeSpan.FromMinutes(5)); // dentro de un nuevo "cooldown"
        _store.GetChannelStats().Returns(new[] { Snap("ch", 1000, 500) });
        await sut.ScanAndDispatchAsync(CancellationToken.None);
        await _email.Received(2).SendAsync(
            Arg.Is<EmailMessage>(m => m.Subject.Contains("fail rate breach")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ScanAndDispatchAsync_RecoveryRequiresMinimumSample()
    {
        var sut = BuildSut(minSample: 100);

        _store.GetChannelStats().Returns(new[] { Snap("ch", 1000, 500) });
        await sut.ScanAndDispatchAsync(CancellationToken.None);
        _time.Advance(TimeSpan.FromMinutes(70));

        // Sample insuficiente — NO dispara recovery aunque failRate sea bajo.
        _store.GetChannelStats().Returns(new[] { Snap("ch", 50, 0) });
        await sut.ScanAndDispatchAsync(CancellationToken.None);

        await _email.Received(1).SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
        await _email.DidNotReceive().SendAsync(
            Arg.Is<EmailMessage>(m => m.Subject.Contains("recovered")),
            Arg.Any<CancellationToken>());
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 4, 28, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan delta) => _now = _now.Add(delta);
    }
}
