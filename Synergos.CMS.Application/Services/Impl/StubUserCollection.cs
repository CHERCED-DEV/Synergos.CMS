using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Default <see cref="IUserCollection"/> — colecciones por usuario (favoritos/
/// wishlist/listas/saved-searches) en memoria del proceso. Seam GENÉRICO del
/// plan doc 21 §1.4 (P11): un contrato, N dominios — el itemRef es opaco.
/// </summary>
/// <remarks>
/// Lógica pura en <c>Synergos.CMS.Application</c> — cero dependencia de
/// Umbraco/AspNetCore (ADR 0002). Determinista e idempotente: re-agregar el
/// mismo ítem devuelve el existente (misma fecha, sin duplicar); quitar dos
/// veces no lanza. Keys case-insensitive (owner/collection/itemRef) para que
/// "Wishlist" y "wishlist" sean la misma colección. Estado en memoria
/// (proceso), suficiente para demo; un adapter real delega a DB/perfil del
/// Member. Time source inyectable para determinismo en tests (ADR 0075).
/// </remarks>
public sealed class StubUserCollection : IUserCollection
{
    private readonly Func<DateTimeOffset> _now;
    private readonly object _gate = new();

    // owner → collection → itemRef → item. Diccionarios anidados protegidos
    // por _gate (stub de demo, baja contención; un adapter real delega a DB).
    private readonly Dictionary<string, Dictionary<string, Dictionary<string, UserCollectionItem>>> _owners =
        new(StringComparer.OrdinalIgnoreCase);

    // owner → collection → última actividad (agregar/quitar) para el resumen.
    private readonly Dictionary<string, Dictionary<string, DateTimeOffset>> _touched =
        new(StringComparer.OrdinalIgnoreCase);

    public StubUserCollection()
        : this(null)
    {
    }

    /// <summary>Ctor con time source inyectable para determinismo en tests. Null = reloj real.</summary>
    public StubUserCollection(Func<DateTimeOffset>? now)
    {
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    public Task<UserCollectionItem> AddAsync(
        string owner,
        string collection,
        string itemRef,
        CancellationToken cancellationToken = default)
    {
        var (ownerKey, collectionKey, itemKey) = Validate(owner, collection, itemRef);

        lock (_gate)
        {
            var collections = GetOrAdd(_owners, ownerKey,
                () => new Dictionary<string, Dictionary<string, UserCollectionItem>>(StringComparer.OrdinalIgnoreCase));
            var items = GetOrAdd(collections, collectionKey,
                () => new Dictionary<string, UserCollectionItem>(StringComparer.OrdinalIgnoreCase));

            // Idempotente: el ítem ya está → devolver el existente sin tocar fecha.
            if (items.TryGetValue(itemKey, out var existing))
            {
                return Task.FromResult(existing);
            }

            var item = new UserCollectionItem(ownerKey, collectionKey, itemKey, _now());
            items[itemKey] = item;
            Touch(ownerKey, collectionKey, item.AddedAt);
            return Task.FromResult(item);
        }
    }

    public Task<bool> RemoveAsync(
        string owner,
        string collection,
        string itemRef,
        CancellationToken cancellationToken = default)
    {
        var (ownerKey, collectionKey, itemKey) = Validate(owner, collection, itemRef);

        lock (_gate)
        {
            if (!_owners.TryGetValue(ownerKey, out var collections)
                || !collections.TryGetValue(collectionKey, out var items)
                || !items.Remove(itemKey))
            {
                return Task.FromResult(false);
            }

            Touch(ownerKey, collectionKey, _now());
            return Task.FromResult(true);
        }
    }

    public Task<IReadOnlyList<UserCollectionItem>> GetAsync(
        string owner,
        string collection,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(collection))
        {
            return Task.FromResult<IReadOnlyList<UserCollectionItem>>(Array.Empty<UserCollectionItem>());
        }

        lock (_gate)
        {
            if (!_owners.TryGetValue(owner.Trim(), out var collections)
                || !collections.TryGetValue(collection.Trim(), out var items))
            {
                return Task.FromResult<IReadOnlyList<UserCollectionItem>>(Array.Empty<UserCollectionItem>());
            }

            var list = items.Values
                .OrderByDescending(i => i.AddedAt)
                .ThenBy(i => i.ItemRef, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return Task.FromResult<IReadOnlyList<UserCollectionItem>>(list);
        }
    }

    public Task<IReadOnlyList<UserCollectionSummary>> GetCollectionsAsync(
        string owner,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(owner))
        {
            return Task.FromResult<IReadOnlyList<UserCollectionSummary>>(Array.Empty<UserCollectionSummary>());
        }

        lock (_gate)
        {
            if (!_owners.TryGetValue(owner.Trim(), out var collections))
            {
                return Task.FromResult<IReadOnlyList<UserCollectionSummary>>(Array.Empty<UserCollectionSummary>());
            }

            var ownerTouched = _touched.TryGetValue(owner.Trim(), out var t)
                ? t
                : new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);

            var list = collections
                .Select(kv => new UserCollectionSummary(
                    Collection: kv.Key,
                    Count: kv.Value.Count,
                    UpdatedAt: ownerTouched.TryGetValue(kv.Key, out var at) ? at : default))
                .OrderByDescending(s => s.UpdatedAt)
                .ThenBy(s => s.Collection, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return Task.FromResult<IReadOnlyList<UserCollectionSummary>>(list);
        }
    }

    private static (string Owner, string Collection, string ItemRef) Validate(
        string owner, string collection, string itemRef)
    {
        if (string.IsNullOrWhiteSpace(owner))
        {
            throw new ArgumentException("El dueño de la colección es obligatorio.", nameof(owner));
        }
        if (string.IsNullOrWhiteSpace(collection))
        {
            throw new ArgumentException("El nombre de la colección es obligatorio.", nameof(collection));
        }
        if (string.IsNullOrWhiteSpace(itemRef))
        {
            throw new ArgumentException("La referencia del ítem es obligatoria.", nameof(itemRef));
        }
        return (owner.Trim(), collection.Trim(), itemRef.Trim());
    }

    private void Touch(string ownerKey, string collectionKey, DateTimeOffset at)
    {
        var perOwner = GetOrAdd(_touched, ownerKey,
            () => new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase));
        perOwner[collectionKey] = at;
    }

    private static TValue GetOrAdd<TValue>(
        Dictionary<string, TValue> map, string key, Func<TValue> factory)
    {
        if (!map.TryGetValue(key, out var value))
        {
            value = factory();
            map[key] = value;
        }
        return value;
    }
}
