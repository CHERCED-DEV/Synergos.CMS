using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Dispatches to the <see cref="ICompositionReader{TInput, TOutput}"/>
/// registered in the DI container for a given (source, target) type
/// pair. Returns <c>null</c> when none is registered.
/// </summary>
/// <remarks>
/// Thin wrapper over <see cref="IServiceProvider"/>, mirroring
/// <see cref="ElementViewModelResolver"/>. Per ADR 0009 this collapses
/// the 27 copy-pasted <c>*Reader</c> classes of Epic Fail 2
/// (<c>_archive/fails/Synergos.CMS.epicfail2/Application/Mapping/Compositions/</c>;
/// <c>01-epic-fail-2-forensic-analysis.md §4.4 [A4]</c>) into a single
/// typed dispatch point. Concrete readers are created only for
/// compositions whose shape is not purely declarative.
///
/// Not registered in DI yet; consumers will appear in a later ola and
/// wire registration in <c>SeamComposer</c>. No caching, no reflection,
/// no implicit behaviour — lookup only.
/// </remarks>
public sealed class CompositionResolver
{
    private readonly IServiceProvider _services;

    public CompositionResolver(IServiceProvider services) =>
        _services = services;

    /// <summary>
    /// Returns the registered
    /// <see cref="ICompositionReader{TInput, TOutput}"/> for the given
    /// type pair, or <c>null</c> when none has been registered.
    /// </summary>
    public ICompositionReader<TInput, TOutput>? TryResolve<TInput, TOutput>() =>
        _services.GetService(typeof(ICompositionReader<TInput, TOutput>))
            as ICompositionReader<TInput, TOutput>;
}
