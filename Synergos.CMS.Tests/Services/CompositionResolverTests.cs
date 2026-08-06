using Microsoft.Extensions.DependencyInjection;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Tests.Services;

public class CompositionResolverTests
{
    private sealed record Element(string Raw);
    private sealed record SeoModel(string Title);
    private sealed record TrackingModel(bool Enabled);

    private sealed class SeoReader : ICompositionReader<Element, SeoModel>
    {
        public SeoModel Read(Element source) => new(source.Raw.ToUpperInvariant());
    }

    private sealed class TrackingReader : ICompositionReader<Element, TrackingModel>
    {
        public TrackingModel Read(Element source) => new(!string.IsNullOrEmpty(source.Raw));
    }

    [Fact]
    public void TryResolve_ReturnsRegisteredReader()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICompositionReader<Element, SeoModel>, SeoReader>();
        var resolver = new CompositionResolver(services.BuildServiceProvider());

        var reader = resolver.TryResolve<Element, SeoModel>();

        Assert.NotNull(reader);
        Assert.Equal("HELLO", reader!.Read(new Element("hello")).Title);
    }

    [Fact]
    public void TryResolve_ReturnsNull_WhenNoReaderRegistered()
    {
        var resolver = new CompositionResolver(new ServiceCollection().BuildServiceProvider());

        var reader = resolver.TryResolve<Element, SeoModel>();

        Assert.Null(reader);
    }

    [Fact]
    public void TryResolve_DispatchesByTargetModel_FromSameSource()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICompositionReader<Element, SeoModel>, SeoReader>();
        services.AddSingleton<ICompositionReader<Element, TrackingModel>, TrackingReader>();
        var resolver = new CompositionResolver(services.BuildServiceProvider());

        var seo = resolver.TryResolve<Element, SeoModel>();
        var tracking = resolver.TryResolve<Element, TrackingModel>();

        Assert.IsType<SeoReader>(seo);
        Assert.IsType<TrackingReader>(tracking);
        Assert.True(tracking!.Read(new Element("x")).Enabled);
    }

    [Fact]
    public void TryResolve_ReturnsNull_ForUnregisteredPair()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICompositionReader<Element, SeoModel>, SeoReader>();
        var resolver = new CompositionResolver(services.BuildServiceProvider());

        // Target not registered — even though SeoModel IS registered
        // for Element source, TrackingModel is not.
        var reader = resolver.TryResolve<Element, TrackingModel>();

        Assert.Null(reader);
    }
}
