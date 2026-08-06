using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Dispatches to the <see cref="IElementViewModelMapper{TInput, TOutput}"/>
/// registered in the DI container for a given (source, target) type
/// pair. Returns <c>null</c> when none is registered.
/// </summary>
/// <remarks>
/// This resolver is deliberately a thin wrapper over
/// <see cref="IServiceProvider"/>. It exists per ADR 0009 to give the
/// core one typed dispatch point instead of the 74 element-specific
/// mappers that defined the Epic Fail 2 anti-pattern
/// (<c>_archive/fails/Synergos.CMS.epicfail2/Application/Mapping/Elements/</c>;
/// <c>01-epic-fail-2-forensic-analysis.md §4.2 [A2]</c>).
///
/// Concrete mappers are created by demand when a Document Type needs
/// one (Ola 7+); DI registration lives in <c>SeamComposer</c> in a
/// future wiring ola — this class is currently not registered in DI
/// and is consumed directly by tests.
///
/// The resolver performs no caching, reflection, or behavioural logic
/// beyond the lookup. If richer policy is required later (fallback,
/// logging, required-throw), it lives in a decorator, not here.
/// </remarks>
public sealed class ElementViewModelResolver
{
    private readonly IServiceProvider _services;

    public ElementViewModelResolver(IServiceProvider services) =>
        _services = services;

    /// <summary>
    /// Returns the registered
    /// <see cref="IElementViewModelMapper{TInput, TOutput}"/> for the
    /// given type pair, or <c>null</c> when none has been registered.
    /// Fail-open by design: callers decide whether to throw, log, or
    /// fall back.
    /// </summary>
    public IElementViewModelMapper<TInput, TOutput>? TryResolve<TInput, TOutput>() =>
        _services.GetService(typeof(IElementViewModelMapper<TInput, TOutput>))
            as IElementViewModelMapper<TInput, TOutput>;
}
