using System.Text.Encodings.Web;
using System.Text.Json;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Default <see cref="IUserCollection"/> — colecciones por usuario (favoritos/
/// wishlist/listas/saved-searches). Seam GENÉRICO del plan doc 21 §1.4 (P11):
/// un contrato, N dominios — el itemRef es opaco.
/// </summary>
/// <remarks>
/// <para>Lógica pura en <c>Synergos.CMS.Application</c> — cero dependencia de
/// Umbraco/AspNetCore (ADR 0002). Determinista e idempotente: re-agregar el
/// mismo ítem devuelve el existente (misma fecha, sin duplicar); quitar dos
/// veces no lanza. Keys case-insensitive (owner/collection/itemRef) para que
/// "Wishlist" y "wishlist" sean la misma colección. Time source inyectable
/// para determinismo en tests (ADR 0075).</para>
///
/// <para><b>Durabilidad.</b> El estado vive detrás de
/// <see cref="IJsonEntityStore"/> (ADR 0105) y ya no en diccionarios del
/// proceso. Es el seam de MAYOR impacto de la familia: lo comparten tres
/// verticales a la vez —la wishlist de Tienda, los favoritos/shortlist de
/// Propiedades y los guardados de Blogs— así que un reinicio borraba las tres
/// listas de una sola vez.</para>
///
/// <para><b>Un documento por dueño</b> (<c>{owner}</c> en minúsculas es la
/// clave). Todas las colecciones de un usuario viven en el mismo documento, de
/// modo que ninguna operación necesita recorrer el espacio completo: leer una
/// colección, resumirlas todas, agregar o quitar cuesta UNA lectura (más una
/// escritura si muta). El aislamiento entre dueños es estructural — un dueño
/// nunca puede leer el documento de otro.</para>
///
/// <para>El <c>storeNamespace</c> es propio de esta familia
/// (<c>user-collections</c>): compartirlo con otro motor mezclaría documentos
/// de forma distinta bajo la misma clave.</para>
/// </remarks>
public sealed class StubUserCollection : IUserCollection
{
    /// <summary>Familia de entidades en el store genérico (→ App_Data/syn-user-collections/).</summary>
    public const string DefaultResourceType = "user-collections";

    private static readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly Func<DateTimeOffset> _now;
    private readonly IJsonEntityStore _store;
    private readonly string _resourceType;

    // Serializa el read-modify-write de agregar/quitar. No se puede usar lock{}
    // porque el cuerpo hace await; SemaphoreSlim es el equivalente async —
    // mismo criterio que StubOrderTrackingService.
    private readonly SemaphoreSlim _mutate = new(1, 1);

    public StubUserCollection()
        : this(null)
    {
    }

    /// <summary>Ctor con time source inyectable para determinismo en tests. Null = reloj real.</summary>
    public StubUserCollection(Func<DateTimeOffset>? now)
        : this(now, null, null)
    {
    }

    /// <summary>
    /// Ctor completo: <paramref name="store"/> es el backing store durable
    /// (FileSystem en Web, InMemory en tests) y <paramref name="storeNamespace"/>
    /// separa esta familia de las demás.
    /// </summary>
    /// <param name="now">Reloj inyectable para los tests. Null usa el del sistema.</param>
    /// <param name="store">Backing store durable. Null deja las colecciones en memoria.</param>
    /// <param name="storeNamespace">Familia de entidades. Null usa
    ///   <see cref="DefaultResourceType"/>.</param>
    public StubUserCollection(Func<DateTimeOffset>? now, IJsonEntityStore? store, string? storeNamespace)
    {
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _store = store ?? new InMemoryJsonEntityStore();
        _resourceType = string.IsNullOrWhiteSpace(storeNamespace) ? DefaultResourceType : storeNamespace;
    }

    public async Task<UserCollectionItem> AddAsync(
        string owner,
        string collection,
        string itemRef,
        CancellationToken cancellationToken = default)
    {
        var (ownerKey, collectionKey, itemKey) = Validate(owner, collection, itemRef);

        await _mutate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await LoadAsync(ownerKey, cancellationToken).ConfigureAwait(false);
            var bucket = Find(state, collectionKey);
            if (bucket is null)
            {
                bucket = new CollectionState { Name = collectionKey };
                state.Collections.Add(bucket);
            }

            // Idempotente: el ítem ya está → devolver el existente sin tocar fecha.
            var existing = bucket.Items.FirstOrDefault(i =>
                string.Equals(i.ItemRef, itemKey, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                return existing;
            }

            var item = new UserCollectionItem(ownerKey, collectionKey, itemKey, _now());
            bucket.Items.Add(item);
            bucket.UpdatedAt = item.AddedAt;
            await SaveAsync(ownerKey, state, cancellationToken).ConfigureAwait(false);
            return item;
        }
        finally
        {
            _mutate.Release();
        }
    }

    public async Task<bool> RemoveAsync(
        string owner,
        string collection,
        string itemRef,
        CancellationToken cancellationToken = default)
    {
        var (ownerKey, collectionKey, itemKey) = Validate(owner, collection, itemRef);

        await _mutate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await LoadAsync(ownerKey, cancellationToken).ConfigureAwait(false);
            var bucket = Find(state, collectionKey);
            if (bucket is null)
            {
                return false;
            }

            var index = bucket.Items.FindIndex(i =>
                string.Equals(i.ItemRef, itemKey, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                return false;
            }

            bucket.Items.RemoveAt(index);
            bucket.UpdatedAt = _now();
            await SaveAsync(ownerKey, state, cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally
        {
            _mutate.Release();
        }
    }

    public async Task<IReadOnlyList<UserCollectionItem>> GetAsync(
        string owner,
        string collection,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(collection))
        {
            return Array.Empty<UserCollectionItem>();
        }

        var state = await LoadAsync(owner.Trim(), cancellationToken).ConfigureAwait(false);
        var bucket = Find(state, collection.Trim());
        if (bucket is null)
        {
            return Array.Empty<UserCollectionItem>();
        }

        return bucket.Items
            .OrderByDescending(i => i.AddedAt)
            .ThenBy(i => i.ItemRef, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<IReadOnlyList<UserCollectionSummary>> GetCollectionsAsync(
        string owner,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(owner))
        {
            return Array.Empty<UserCollectionSummary>();
        }

        var state = await LoadAsync(owner.Trim(), cancellationToken).ConfigureAwait(false);
        return state.Collections
            .Select(c => new UserCollectionSummary(
                Collection: c.Name,
                Count: c.Items.Count,
                UpdatedAt: c.UpdatedAt))
            .OrderByDescending(s => s.UpdatedAt)
            .ThenBy(s => s.Collection, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Lee el documento del dueño. Un documento corrupto se trata como "sin
    /// colecciones" en vez de reventar: una wishlist ilegible no puede tumbar la
    /// página de nadie — y la próxima escritura lo reemplaza.
    /// </summary>
    private async Task<OwnerState> LoadAsync(string ownerKey, CancellationToken cancellationToken)
    {
        var json = await _store.ReadAsync(_resourceType, StoreKey(ownerKey), cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json))
        {
            return new OwnerState();
        }

        OwnerState? state;
        try
        {
            state = JsonSerializer.Deserialize<OwnerState>(json, _json);
        }
        catch (JsonException)
        {
            return new OwnerState();
        }

        if (state is null)
        {
            return new OwnerState();
        }
        state.Collections ??= new List<CollectionState>();
        foreach (var bucket in state.Collections)
        {
            bucket.Items ??= new List<UserCollectionItem>();
        }
        return state;
    }

    private Task SaveAsync(string ownerKey, OwnerState state, CancellationToken cancellationToken)
        => _store.WriteAsync(
            _resourceType, StoreKey(ownerKey), JsonSerializer.Serialize(state, _json), cancellationToken);

    // La clave del documento va en minúsculas porque el owner es case-insensitive:
    // "Camila@x.co" y "camila@x.co" son el MISMO usuario y deben caer en el mismo
    // documento (el diccionario que esto reemplaza usaba OrdinalIgnoreCase).
    private static string StoreKey(string ownerKey) => ownerKey.ToLowerInvariant();

    private static CollectionState? Find(OwnerState state, string collectionKey)
        => state.Collections.FirstOrDefault(c =>
            string.Equals(c.Name, collectionKey, StringComparison.OrdinalIgnoreCase));

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

    /// <summary>Documento persistido: TODAS las colecciones de un dueño.</summary>
    private sealed class OwnerState
    {
        public List<CollectionState> Collections { get; set; } = new();
    }

    /// <summary>
    /// Una colección dentro del documento del dueño. <c>Name</c> conserva el
    /// casing del primer add (igual que la clave del diccionario anterior) y
    /// <c>UpdatedAt</c> es la última actividad — una colección que se vacía
    /// SIGUE existiendo con conteo 0, tal como antes.
    /// </summary>
    private sealed class CollectionState
    {
        public string Name { get; set; } = string.Empty;
        public DateTimeOffset UpdatedAt { get; set; }
        public List<UserCollectionItem> Items { get; set; } = new();
    }
}
