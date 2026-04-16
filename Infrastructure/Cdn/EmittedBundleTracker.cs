using Synergos.CMS.Domain.Services;

namespace Synergos.CMS.Infrastructure.Cdn;

/// <summary>
/// In-memory, request-scoped <see cref="IEmittedBundleTracker"/>. Backed
/// by a <see cref="HashSet{T}"/> guarded by a lock so parallel async
/// partials inside the same request can race safely.
///
/// Lifetime: Scoped. ASP.NET Core creates one per request; the tracker
/// is garbage-collected when the request ends — no reset logic needed.
/// </summary>
public sealed class EmittedBundleTracker : IEmittedBundleTracker
{
    private readonly HashSet<string> _emitted = new(StringComparer.Ordinal);
    private readonly object          _gate    = new();

    public bool TryClaim(string bundleName)
    {
        if (string.IsNullOrWhiteSpace(bundleName)) return false;

        lock (_gate)
        {
            return _emitted.Add(bundleName);
        }
    }

    public IReadOnlyCollection<string> EmittedBundles
    {
        get
        {
            lock (_gate)
            {
                // Return a snapshot so callers can enumerate outside the lock.
                return _emitted.ToArray();
            }
        }
    }
}
