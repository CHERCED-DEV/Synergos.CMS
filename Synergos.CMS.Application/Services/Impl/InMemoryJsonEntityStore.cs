using System.Collections.Concurrent;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// <see cref="IJsonEntityStore"/> en memoria del proceso — default cuando no hay
/// persistencia (tests, entornos efímeros). NO sobrevive un reinicio (esa es la razón de
/// existir del <c>FileSystemJsonEntityStore</c>). Thread-safe. Una sola instancia sirve a
/// TODAS las familias: la clave compuesta <c>{resourceType}/{key}</c> las mantiene
/// aisladas entre sí (así un test puede compartir un store para orden+pago+reserva).
/// </summary>
public sealed class InMemoryJsonEntityStore : IJsonEntityStore
{
    private readonly ConcurrentDictionary<string, string> _store = new(StringComparer.Ordinal);

    public Task WriteAsync(string resourceType, string key, string json, CancellationToken cancellationToken = default)
    {
        _store[Compose(resourceType, key)] = json;
        return Task.CompletedTask;
    }

    public Task<string?> ReadAsync(string resourceType, string key, CancellationToken cancellationToken = default)
        => Task.FromResult(_store.TryGetValue(Compose(resourceType, key), out var json) ? json : null);

    public Task<IReadOnlyList<string>> ListAsync(string resourceType, CancellationToken cancellationToken = default)
    {
        var prefix = resourceType + "/";
        return Task.FromResult<IReadOnlyList<string>>(_store
            .Where(kv => kv.Key.StartsWith(prefix, StringComparison.Ordinal))
            .Select(kv => kv.Value)
            .ToList());
    }

    public Task<bool> DeleteAsync(string resourceType, string key, CancellationToken cancellationToken = default)
        => Task.FromResult(_store.TryRemove(Compose(resourceType, key), out _));

    private static string Compose(string resourceType, string key) => resourceType + "/" + key;
}
