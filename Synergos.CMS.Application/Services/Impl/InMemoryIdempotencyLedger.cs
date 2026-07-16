using System.Collections.Concurrent;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// <see cref="IIdempotencyLedger"/> en memoria del proceso — default de tests/efímeros.
/// NO sobrevive un reinicio (el <c>FileSystemIdempotencyLedger</c> sí). La exclusividad
/// atómica se logra con <see cref="ConcurrentDictionary{TKey,TValue}.TryAdd"/> —
/// equivalente en memoria del <c>FileMode.CreateNew</c> del adapter FileSystem: bajo
/// concurrencia exactamente un llamado obtiene <c>true</c>.
/// </summary>
public sealed class InMemoryIdempotencyLedger : IIdempotencyLedger
{
    private readonly ConcurrentDictionary<string, byte> _claimed = new(StringComparer.Ordinal);

    public Task<bool> TryClaimAsync(string scope, string key, CancellationToken cancellationToken = default)
        => Task.FromResult(_claimed.TryAdd($"{scope}:{key}", 0));
}
