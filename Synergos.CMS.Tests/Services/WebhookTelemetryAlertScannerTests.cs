using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Tests para <see cref="WebhookTelemetryAlertScanner"/> (Cap-240
/// Batch C Ola 237 + refactor Cap-260 Batch B Ola 256). Cubre alert
/// firing, cooldown, y recovery cuando un canal previamente alerting
/// vuelve a healthy. Verifica las llamadas al <see cref="IAlertNotifier"/>
/// composite (no más IEmailService directo).
/// </summary>
public sealed class WebhookTelemetryAlertScannerTests
{
    private readonly IWebhookTelemetryStore _store = Substitute.For<IWebhookTelemetryStore>();
    private readonly IAlertNotifier _notifier = Substitute.For<IAlertNotifier>();
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
            _store, _notifier, monitor,
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

        await _notifier.DidNotReceive().NotifyAlertAsync(
            Arg.Any<WebhookAlertEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ScanAndDispatchAsync_BelowMinimumSample_NoAlert()
    {
        var sut = BuildSut(minSample: 100);
        _store.GetChannelStats().Returns(new[] { Snap("ch", 50, 30) });

        await sut.ScanAndDispatchAsync(CancellationToken.None);

        await _notifier.DidNotReceive().NotifyAlertAsync(
            Arg.Any<WebhookAlertEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ScanAndDispatchAsync_BelowThreshold_NoAlert()
    {
        var sut = BuildSut(threshold: 0.20);
        _store.GetChannelStats().Returns(new[] { Snap("ch", 1000, 100) });

        await sut.ScanAndDispatchAsync(CancellationToken.None);

        await _notifier.DidNotReceive().NotifyAlertAsync(
            Arg.Any<WebhookAlertEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ScanAndDispatchAsync_AboveThreshold_FiresAlertWithCanonicalShape()
    {
        var sut = BuildSut();
        _store.GetChannelStats().Returns(new[] { Snap("ch", 1000, 500) });

        await sut.ScanAndDispatchAsync(CancellationToken.None);

        await _notifier.Received(1).NotifyAlertAsync(
            Arg.Is<WebhookAlertEvent>(a =>
                a.ChannelName == "ch" &&
                a.FailRate == 0.5 &&
                a.Threshold == 0.20 &&
                a.TotalCalls == 1000 &&
                a.FailureCount == 500 &&
                a.SuccessCount == 500 &&
                a.CooldownMinutes == 60),
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

        await _notifier.Received(1).NotifyAlertAsync(
            Arg.Any<WebhookAlertEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ScanAndDispatchAsync_PostCooldown_ReFires()
    {
        var sut = BuildSut(cooldownMinutes: 60);
        _store.GetChannelStats().Returns(new[] { Snap("ch", 1000, 500) });

        await sut.ScanAndDispatchAsync(CancellationToken.None);
        _time.Advance(TimeSpan.FromMinutes(61));
        await sut.ScanAndDispatchAsync(CancellationToken.None);

        await _notifier.Received(2).NotifyAlertAsync(
            Arg.Any<WebhookAlertEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ScanAndDispatchAsync_RecoveryFires_WithCanonicalShape()
    {
        var sut = BuildSut();

        // Tick 1: alerting (50%)
        _store.GetChannelStats().Returns(new[] { Snap("ch", 1000, 500) });
        await sut.ScanAndDispatchAsync(CancellationToken.None);

        _time.Advance(TimeSpan.FromMinutes(70));

        // Tick 2: healthy (5%)
        _store.GetChannelStats().Returns(new[] { Snap("ch", 1000, 50) });
        await sut.ScanAndDispatchAsync(CancellationToken.None);

        await _notifier.Received(1).NotifyAlertAsync(
            Arg.Any<WebhookAlertEvent>(), Arg.Any<CancellationToken>());
        await _notifier.Received(1).NotifyRecoveryAsync(
            Arg.Is<WebhookRecoveryEvent>(r =>
                r.ChannelName == "ch" &&
                r.CurrentFailRate == 0.05 &&
                r.PriorFailRate == 0.5 &&
                r.AlertingDuration.TotalMinutes >= 70),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ScanAndDispatchAsync_NoRecovery_IfChannelNeverAlerted()
    {
        var sut = BuildSut();
        _store.GetChannelStats().Returns(new[] { Snap("ch", 1000, 50) });

        await sut.ScanAndDispatchAsync(CancellationToken.None);

        await _notifier.DidNotReceive().NotifyAlertAsync(
            Arg.Any<WebhookAlertEvent>(), Arg.Any<CancellationToken>());
        await _notifier.DidNotReceive().NotifyRecoveryAsync(
            Arg.Any<WebhookRecoveryEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ScanAndDispatchAsync_RecoveryDisabled_NoNotifyButStateClears()
    {
        var sut = BuildSut(recoveryEnabled: false);

        _store.GetChannelStats().Returns(new[] { Snap("ch", 1000, 500) });
        await sut.ScanAndDispatchAsync(CancellationToken.None);
        _time.Advance(TimeSpan.FromMinutes(70));

        _store.GetChannelStats().Returns(new[] { Snap("ch", 1000, 50) });
        await sut.ScanAndDispatchAsync(CancellationToken.None);

        await _notifier.Received(1).NotifyAlertAsync(
            Arg.Any<WebhookAlertEvent>(), Arg.Any<CancellationToken>());
        await _notifier.DidNotReceive().NotifyRecoveryAsync(
            Arg.Any<WebhookRecoveryEvent>(), Arg.Any<CancellationToken>());

        // Verifica que el state se limpió: si vuelve a alerting, dispara
        // como first-time (no como re-fire dentro de cooldown).
        _time.Advance(TimeSpan.FromMinutes(5));
        _store.GetChannelStats().Returns(new[] { Snap("ch", 1000, 500) });
        await sut.ScanAndDispatchAsync(CancellationToken.None);
        await _notifier.Received(2).NotifyAlertAsync(
            Arg.Any<WebhookAlertEvent>(), Arg.Any<CancellationToken>());
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

        await _notifier.Received(1).NotifyAlertAsync(
            Arg.Any<WebhookAlertEvent>(), Arg.Any<CancellationToken>());
        await _notifier.DidNotReceive().NotifyRecoveryAsync(
            Arg.Any<WebhookRecoveryEvent>(), Arg.Any<CancellationToken>());
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 4, 28, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan delta) => _now = _now.Add(delta);
    }
}
