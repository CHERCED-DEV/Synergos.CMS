using System.Collections.Concurrent;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// <see cref="IReservationStore"/> en memoria del proceso — el backing store que el
/// <c>StubReservationService</c> usaba directo antes de T3. Default cuando no hay
/// persistencia (tests, entornos efímeros). NO sobrevive un reinicio (esa es la razón
/// de existir del <c>FileSystemReservationStore</c>). Thread-safe vía
/// <see cref="ConcurrentDictionary{TKey,TValue}"/>. Calca <see cref="InMemoryShopOrderStore"/>.
/// </summary>
public sealed class InMemoryReservationStore : IReservationStore
{
    private readonly ConcurrentDictionary<string, string> _store = new(StringComparer.Ordinal);

    public Task WriteAsync(string reservationId, string json, CancellationToken cancellationToken = default)
    {
        _store[reservationId] = json;
        return Task.CompletedTask;
    }

    public Task<string?> ReadAsync(string reservationId, CancellationToken cancellationToken = default)
        => Task.FromResult(_store.TryGetValue(reservationId, out var json) ? json : null);

    public Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<string>>(_store.Values.ToList());

    public Task<bool> DeleteAsync(string reservationId, CancellationToken cancellationToken = default)
        => Task.FromResult(_store.TryRemove(reservationId, out _));
}
