namespace Synergos.CMS.Domain.Services;

/// <summary>
/// Tracks which CDN bundles have already emitted their <c>&lt;script&gt;</c>
/// tag in the current request. Lets multiple instances of the same CDN
/// element coexist on a page with a single script load — the second and
/// subsequent instances reuse the module registered by the first.
///
/// Scoped lifetime: one tracker per request, so the set resets naturally
/// between pages. Implementations must be thread-safe within the scope
/// (Razor can render blocks in parallel via async partials).
///
/// Caller pattern in Razor:
/// <code>
/// @inject IEmittedBundleTracker Bundles
/// @if (Bundles.TryClaim("card"))
/// {
///     &lt;script src="@ElementUrl.ResolveBundle("card")" type="module" defer&gt;&lt;/script&gt;
/// }
/// &lt;synergos-card config='@cfg'&gt;&lt;/synergos-card&gt;
/// </code>
/// </summary>
public interface IEmittedBundleTracker
{
    /// <summary>
    /// Claims the bundle for emission. Returns <c>true</c> exactly once
    /// per request per <paramref name="bundleName"/> — subsequent calls
    /// return <c>false</c>, signalling the caller to skip the
    /// <c>&lt;script&gt;</c> tag.
    /// </summary>
    bool TryClaim(string bundleName);

    /// <summary>
    /// Snapshot of the bundles that have been claimed in the current
    /// request. Intended for diagnostics (e.g. a trailing debug panel,
    /// analytics beacon) — not for emitting preload hints, since the
    /// head is flushed before body bundles are discovered.
    /// For preloads, the caller walks the Block Grid upfront via a
    /// prescanner and emits <c>&lt;link rel="modulepreload"&gt;</c> in head.
    /// </summary>
    IReadOnlyCollection<string> EmittedBundles { get; }
}
