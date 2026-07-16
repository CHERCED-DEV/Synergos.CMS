using System.Collections.Concurrent;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// <see cref="ITravelOrderStore"/> en memoria del proceso — el backing store que el
/// <c>TravelCartService</c> usaba directo antes de durabilizar Booking. Default cuando
/// no hay persistencia (tests, entornos efímeros). NO sobrevive un reinicio (esa es la
/// razón de existir del <c>FileSystemTravelOrderStore</c>). Calca <see cref="InMemoryShopOrderStore"/>.
/// </summary>
public sealed class InMemoryTravelOrderStore : ITravelOrderStore
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
