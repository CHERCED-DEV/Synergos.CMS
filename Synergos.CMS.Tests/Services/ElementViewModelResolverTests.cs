using Microsoft.Extensions.DependencyInjection;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Tests.Services;

public class ElementViewModelResolverTests
{
    private sealed record SourceA(string Value);
    private sealed record TargetA(string Echoed);

    private sealed record SourceB(int N);
    private sealed record TargetB(int DoubledN);

    private sealed class MapperA : IElementViewModelMapper<SourceA, TargetA>
    {
        public TargetA Map(SourceA source) => new(source.Value);
    }

    private sealed class MapperB : IElementViewModelMapper<SourceB, TargetB>
    {
        public TargetB Map(SourceB source) => new(source.N * 2);
    }

    [Fact]
    public void TryResolve_ReturnsRegisteredMapper()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IElementViewModelMapper<SourceA, TargetA>, MapperA>();
        var resolver = new ElementViewModelResolver(services.BuildServiceProvider());

        var mapper = resolver.TryResolve<SourceA, TargetA>();

        Assert.NotNull(mapper);
        Assert.Equal("hello", mapper!.Map(new SourceA("hello")).Echoed);
    }

    [Fact]
    public void TryResolve_ReturnsNull_WhenNoMapperRegistered()
    {
        var resolver = new ElementViewModelResolver(new ServiceCollection().BuildServiceProvider());

        var mapper = resolver.TryResolve<SourceA, TargetA>();

        Assert.Null(mapper);
    }

    [Fact]
    public void TryResolve_DispatchesIndependentlyPerGenericCombo()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IElementViewModelMapper<SourceA, TargetA>, MapperA>();
        services.AddSingleton<IElementViewModelMapper<SourceB, TargetB>, MapperB>();
        var resolver = new ElementViewModelResolver(services.BuildServiceProvider());

        var mapperA = resolver.TryResolve<SourceA, TargetA>();
        var mapperB = resolver.TryResolve<SourceB, TargetB>();

        Assert.IsType<MapperA>(mapperA);
        Assert.IsType<MapperB>(mapperB);
        Assert.Equal(6, mapperB!.Map(new SourceB(3)).DoubledN);
    }

    [Fact]
    public void TryResolve_ReturnsNull_ForUnregisteredComboEvenWhenSwappedComboIsRegistered()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IElementViewModelMapper<SourceA, TargetA>, MapperA>();
        var resolver = new ElementViewModelResolver(services.BuildServiceProvider());

        // Swapped generic parameters — no registration for this pair
        var mapper = resolver.TryResolve<TargetA, SourceA>();

        Assert.Null(mapper);
    }
}
