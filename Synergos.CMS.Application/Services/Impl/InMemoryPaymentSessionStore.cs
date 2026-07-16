using System.Collections.Concurrent;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// <see cref="IPaymentSessionStore"/> en memoria del proceso — el backing store que
/// el <c>StubPaymentProvider</c> usaba directo antes de T3. Default cuando no hay
/// persistencia (tests, entornos efímeros). NO sobrevive un reinicio (esa es la razón
/// de existir del <c>FileSystemPaymentSessionStore</c>). Thread-safe vía
/// <see cref="ConcurrentDictionary{TKey,TValue}"/>. Calca <see cref="InMemoryShopOrderStore"/>.
/// </summary>
public sealed class InMemoryPaymentSessionStore : IPaymentSessionStore
{
    private readonly ConcurrentDictionary<string, string> _store = new(StringComparer.Ordinal);

    public Task WriteAsync(string sessionId, string json, CancellationToken cancellationToken = default)
    {
        _store[sessionId] = json;
        return Task.CompletedTask;
    }

    public Task<string?> ReadAsync(string sessionId, CancellationToken cancellationToken = default)
        => Task.FromResult(_store.TryGetValue(sessionId, out var json) ? json : null);

    public Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<string>>(_store.Values.ToList());

    public Task<bool> DeleteAsync(string sessionId, CancellationToken cancellationToken = default)
        => Task.FromResult(_store.TryRemove(sessionId, out _));
}
