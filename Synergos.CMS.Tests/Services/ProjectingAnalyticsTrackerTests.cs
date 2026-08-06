using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Tests para <see cref="ProjectingAnalyticsTracker"/> (ADR 0097). Verifica
/// que delega siempre en el inner, que proyecta al store, y los guardrails
/// no-negociables: nunca lanza si el store falla; no proyecta nombre vacío.
/// </summary>
public sealed class ProjectingAnalyticsTrackerTests
{
    private static (ProjectingAnalyticsTracker sut, IAnalyticsTracker inner, IMetricsProjectionStore store) BuildSut()
    {
        var inner = Substitute.For<IAnalyticsTracker>();
        var store = Substitute.For<IMetricsProjectionStore>();
        var sut = new ProjectingAnalyticsTracker(inner, store, NullLogger<ProjectingAnalyticsTracker>.Instance);
        return (sut, inner, store);
    }

    [Fact]
    public void Track_DelegatesToInner()
    {
        var (sut, inner, _) = BuildSut();
        var props = new Dictionary<string, object?> { ["k"] = "v" };

        sut.Track("search.executed", props);

        inner.Received(1).Track("search.executed", props);
    }

    [Fact]
    public void Track_ProjectsCountToStore()
    {
        var (sut, _, store) = BuildSut();

        sut.Track("cart.item-added");

        store.Received(1).RecordEvent(Arg.Is<MetricEvent>(m =>
            m.Slug == "cart.item-added" && m.Value == 1m));
    }

    [Fact]
    public void Track_StoreThrows_DoesNotThrow_AndStillDelegates()
    {
        var (sut, inner, store) = BuildSut();
        store.When(s => s.RecordEvent(Arg.Any<MetricEvent>()))
            .Do(_ => throw new InvalidOperationException("boom"));

        var ex = Record.Exception(() => sut.Track("form.submitted"));

        Assert.Null(ex);                              // swallow — jamás rompe al caller
        inner.Received(1).Track("form.submitted");    // el comportamiento original se preservó
    }

    [Fact]
    public void Track_EmptyEventName_DelegatesButDoesNotProject()
    {
        var (sut, inner, store) = BuildSut();

        sut.Track("");

        inner.Received(1).Track("", Arg.Any<IReadOnlyDictionary<string, object?>?>());
        store.DidNotReceive().RecordEvent(Arg.Any<MetricEvent>());
    }

    [Fact]
    public void Track_DelegatesBeforeProjecting()
    {
        var (sut, inner, store) = BuildSut();

        sut.Track("account.login");

        // El inner se invoca primero (logging nunca se pierde aunque la
        // proyección falle); ambos reciben el evento.
        Received.InOrder(() =>
        {
            inner.Track("account.login", Arg.Any<IReadOnlyDictionary<string, object?>?>());
            store.RecordEvent(Arg.Any<MetricEvent>());
        });
    }
}
