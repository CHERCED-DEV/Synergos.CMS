using System.Collections.Concurrent;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// <see cref="IShopOrderStore"/> en memoria del proceso — el backing store que el
/// motor usaba directo antes de T1. Default cuando no hay persistencia (tests,
/// entornos efímeros). NO sobrevive un reinicio (esa es la razón de existir del
/// <c>FileSystemShopOrderStore</c>). Thread-safe vía <see cref="ConcurrentDictionary{TKey,TValue}"/>.
/// </summary>
public sealed class InMemoryShopOrderStore : IShopOrderStore
{
    private readonly ConcurrentDictionary<string, string> _store = new(StringComparer.Ordinal);

    public Task WriteAsync(string orderRef, string json, CancellationToken cancellationToken = default)
    {
        _store[orderRef] = json;
        return Task.CompletedTask;
    }

    public Task<string?> ReadAsync(string orderRef, CancellationToken cancellationToken = default)
        => Task.FromResult(_store.TryGetValue(orderRef, out var json) ? json : null);

    public Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<string>>(_store.Values.ToList());

    public Task<bool> DeleteAsync(string orderRef, CancellationToken cancellationToken = default)
        => Task.FromResult(_store.TryRemove(orderRef, out _));
}
