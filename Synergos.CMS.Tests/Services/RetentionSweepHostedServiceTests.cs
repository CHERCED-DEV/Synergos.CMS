using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services.Retention;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Tests para <see cref="RetentionSweepHostedService.RunSinglePolicyAsync"/>
/// (Cap-290 Batch B, Ola 294). Verifica que cada sweep emite eventos
/// analytics estructurados con metadata accionable para ops dashboards.
/// El infinite loop del ExecuteAsync no es testable directamente — la
/// lógica per-policy se extrajo a internal method.
/// </summary>
public sealed class RetentionSweepHostedServiceTests
{
    private static RetentionSweepHostedService BuildSut(
        IAnalyticsTracker analytics,
        IEnumerable<IRetentionPolicy>? policies = null) =>
        new(
            policies ?? Array.Empty<IRetentionPolicy>(),
            analytics,
            NullLogger<RetentionSweepHostedService>.Instance);

    [Fact]
    public async Task RunSinglePolicyAsync_SuccessfulSweep_EmitsRetentionSweptEvent()
    {
        var policy = Substitute.For<IRetentionPolicy>();
        policy.Name.Returns("audit");
        policy.SweepAsync(Arg.Any<CancellationToken>()).Returns(7);
        var analytics = Substitute.For<IAnalyticsTracker>();
        var sut = BuildSut(analytics);

        await sut.RunSinglePolicyAsync(policy, CancellationToken.None);

        analytics.Received(1).Track(
            "retention.swept",
            Arg.Is<IReadOnlyDictionary<string, object?>>(d =>
                (string)d["policy"]! == "audit" &&
                (int)d["purged"]! == 7 &&
                (bool)d["success"]! == true));
    }

    [Fact]
    public async Task RunSinglePolicyAsync_ZeroPurged_StillEmitsEventForLiveness()
    {
        // Visibilidad de liveness: aun cuando no hay nada que purgar,
        // el evento confirma que el sweep está corriendo.
        var policy = Substitute.For<IRetentionPolicy>();
        policy.Name.Returns("comments");
        policy.SweepAsync(Arg.Any<CancellationToken>()).Returns(0);
        var analytics = Substitute.For<IAnalyticsTracker>();
        var sut = BuildSut(analytics);

        await sut.RunSinglePolicyAsync(policy, CancellationToken.None);

        analytics.Received(1).Track(
            "retention.swept",
            Arg.Is<IReadOnlyDictionary<string, object?>>(d =>
                (string)d["policy"]! == "comments" &&
                (int)d["purged"]! == 0 &&
                (bool)d["success"]! == true));
    }

    [Fact]
    public async Task RunSinglePolicyAsync_PolicyThrows_EmitsRetentionFailedEvent()
    {
        var policy = Substitute.For<IRetentionPolicy>();
        policy.Name.Returns("form-submissions");
        policy.SweepAsync(Arg.Any<CancellationToken>())
              .Returns<Task<int>>(_ => throw new InvalidOperationException("boom"));
        var analytics = Substitute.For<IAnalyticsTracker>();
        var sut = BuildSut(analytics);

        await sut.RunSinglePolicyAsync(policy, CancellationToken.None);

        analytics.DidNotReceive().Track("retention.swept", Arg.Any<IReadOnlyDictionary<string, object?>>());
        analytics.Received(1).Track(
            "retention.failed",
            Arg.Is<IReadOnlyDictionary<string, object?>>(d =>
                (string)d["policy"]! == "form-submissions" &&
                (string)d["exception"]! == "InvalidOperationException" &&
                (string)d["message"]! == "boom" &&
                (bool)d["success"]! == false));
    }

    [Fact]
    public async Task RunSinglePolicyAsync_OperationCanceled_PropagatesWithoutEmittingEvent()
    {
        // CancellationToken triggered → propagamos para que BackgroundService
        // pare; no emit metric porque no es failure del policy.
        var policy = Substitute.For<IRetentionPolicy>();
        policy.Name.Returns("search-analytics");
        policy.SweepAsync(Arg.Any<CancellationToken>())
              .Returns<Task<int>>(_ => throw new OperationCanceledException());
        var analytics = Substitute.For<IAnalyticsTracker>();
        var sut = BuildSut(analytics);

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            sut.RunSinglePolicyAsync(policy, CancellationToken.None));

        analytics.DidNotReceive().Track(
            Arg.Any<string>(),
            Arg.Any<IReadOnlyDictionary<string, object?>>());
    }
}
