using System.Collections.Concurrent;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Default <see cref="ISocialGraphService"/> — grafo social dirigido asimétrico
/// (dominio Blogs) STUB con estado en memoria del proceso. follow/unfollow
/// idempotentes por <c>(follower, followee)</c>, listas y conteos. Siembra el
/// grafo de la demo desde <see cref="SocialDemoSeed"/> para que el feed
/// "Siguiendo" y los conteos del perfil arranquen poblados.
/// </summary>
/// <remarks>
/// Lógica pura en <c>Synergos.CMS.Application</c> — cero Umbraco/AspNetCore (ADR
/// 0002). El adapter real (store de grafo + caché de conteos) reemplaza la seam
/// sin tocar el módulo Angular. ADR 0075. Singleton — el estado vive en el proceso.
/// </remarks>
public sealed class StubSocialGraphService : ISocialGraphService
{
    // follower → set de followees. Set ordinal para idempotencia y conteos O(1).
    private readonly ConcurrentDictionary<string, HashSet<string>> _following =
        new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public StubSocialGraphService()
    {
        foreach (var (follower, followees) in SocialDemoSeed.Follows)
        {
            _following[follower] = new HashSet<string>(followees, StringComparer.Ordinal);
        }
    }

    public Task<FollowState> FollowAsync(string followerId, string followeeId, CancellationToken cancellationToken = default)
    {
        ValidatePair(followerId, followeeId);

        // No auto-seguirse: devolver el estado sin cambio.
        if (!string.Equals(followerId, followeeId, StringComparison.Ordinal))
        {
            lock (_gate)
            {
                var set = _following.GetOrAdd(followerId, _ => new HashSet<string>(StringComparer.Ordinal));
                set.Add(followeeId); // idempotente: re-seguir no duplica.
            }
        }

        return Task.FromResult(BuildState(followerId, followeeId));
    }

    public Task<FollowState> UnfollowAsync(string followerId, string followeeId, CancellationToken cancellationToken = default)
    {
        ValidatePair(followerId, followeeId);

        lock (_gate)
        {
            if (_following.TryGetValue(followerId, out var set))
            {
                set.Remove(followeeId); // idempotente: quitar lo ausente no lanza.
            }
        }

        return Task.FromResult(BuildState(followerId, followeeId));
    }

    public Task<bool> IsFollowingAsync(string followerId, string followeeId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(followerId) || string.IsNullOrWhiteSpace(followeeId))
        {
            return Task.FromResult(false);
        }

        lock (_gate)
        {
            return Task.FromResult(_following.TryGetValue(followerId, out var set) && set.Contains(followeeId));
        }
    }

    public Task<IReadOnlyList<string>> GetFollowersAsync(string actorId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(actorId))
        {
            return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        }

        lock (_gate)
        {
            var followers = _following
                .Where(kv => kv.Value.Contains(actorId))
                .Select(kv => kv.Key)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToList();
            return Task.FromResult<IReadOnlyList<string>>(followers);
        }
    }

    public Task<IReadOnlyList<string>> GetFollowingAsync(string actorId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(actorId))
        {
            return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        }

        lock (_gate)
        {
            var following = _following.TryGetValue(actorId, out var set)
                ? set.OrderBy(id => id, StringComparer.Ordinal).ToList()
                : new List<string>();
            return Task.FromResult<IReadOnlyList<string>>(following);
        }
    }

    public Task<SocialCounts> GetCountsAsync(string actorId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult(CountsLocked(actorId));
        }
    }

    private static void ValidatePair(string followerId, string followeeId)
    {
        if (string.IsNullOrWhiteSpace(followerId))
        {
            throw new ArgumentException("El followerId es requerido.", nameof(followerId));
        }
        if (string.IsNullOrWhiteSpace(followeeId))
        {
            throw new ArgumentException("El followeeId es requerido.", nameof(followeeId));
        }
    }

    // Asume lock tomado.
    private SocialCounts CountsLocked(string actorId)
    {
        if (string.IsNullOrWhiteSpace(actorId))
        {
            return new SocialCounts(0, 0);
        }

        var followers = _following.Count(kv => kv.Value.Contains(actorId));
        var following = _following.TryGetValue(actorId, out var set) ? set.Count : 0;
        return new SocialCounts(followers, following);
    }

    private FollowState BuildState(string followerId, string followeeId)
    {
        lock (_gate)
        {
            var following = _following.TryGetValue(followerId, out var set) && set.Contains(followeeId);
            var followeeCounts = CountsLocked(followeeId);
            return new FollowState(
                followerId,
                followeeId,
                following,
                followeeCounts.Followers,
                followeeCounts.Following);
        }
    }
}
