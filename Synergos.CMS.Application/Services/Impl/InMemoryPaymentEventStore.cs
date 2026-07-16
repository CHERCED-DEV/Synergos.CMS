using System.Collections.Concurrent;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// <see cref="IPaymentEventStore"/> en memoria del proceso — ledger de idempotencia
/// default para tests/efímeros. NO sobrevive un reinicio (el
/// <c>FileSystemPaymentEventStore</c> sí). La exclusividad atómica se logra con
/// <see cref="ConcurrentDictionary{TKey,TValue}.TryAdd"/> (equivalente en memoria del
/// <c>FileMode.CreateNew</c> del adapter FileSystem).
/// </summary>
public sealed class InMemoryPaymentEventStore : IPaymentEventStore
{
    private readonly ConcurrentDictionary<string, byte> _processed = new(StringComparer.Ordinal);

    public Task<bool> TryMarkProcessedAsync(string provider, string eventId, CancellationToken cancellationToken = default)
        => Task.FromResult(_processed.TryAdd($"{provider}:{eventId}", 0));
}
