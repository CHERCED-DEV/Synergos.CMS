using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Default <see cref="ISavedSearchService"/> — búsquedas guardadas + alertas del
/// portal inmobiliario (doc propiedades-app-spec §4). Da SEMÁNTICA a las
/// saved-searches sobre el seam GENÉRICO <see cref="IUserCollection"/> (colección
/// <c>saved-searches</c>): serializa la <see cref="PropertyQuery"/> en el itemRef
/// opaco, le asigna un id estable, y re-ejecuta los criterios contra
/// <see cref="IPropertyCatalogProvider"/> para contar nuevos matches (la alerta).
/// </summary>
/// <remarks>
/// Lógica pura en <c>Synergos.CMS.Application</c> — cero dependencia de
/// Umbraco/AspNetCore (ADR 0002). NO duplica el almacenamiento: COMPONE (DIP)
/// <see cref="IUserCollection"/> (persistencia por usuario) +
/// <see cref="IPropertyCatalogProvider"/> (universo a matchear). El id de la búsqueda
/// es DETERMINISTA por (owner, criterios) → <see cref="SaveAsync"/> es idempotente:
/// guardar la misma búsqueda dos veces devuelve la misma (el itemRef ya está, el
/// stub de UserCollection no duplica). El itemRef persiste <c>{id}|{label}|{queryJson}</c>
/// para reconstruir el registro completo desde la colección genérica sin un store
/// paralelo. En la demo, "nuevos matches" se SIMULA marcando los destacados como
/// novedad. ADR 0075.
/// </remarks>
public sealed class StubSavedSearchService : ISavedSearchService
{
    /// <summary>Nombre de la colección genérica donde viven las búsquedas guardadas.</summary>
    public const string CollectionName = "saved-searches";

    private const char Sep = '|';

    private readonly IUserCollection _collection;
    private readonly IPropertyCatalogProvider _catalog;
    private readonly Func<DateTimeOffset> _now;

    public StubSavedSearchService(IUserCollection collection, IPropertyCatalogProvider catalog)
        : this(collection, catalog, null)
    {
    }

    /// <summary>Ctor con time source inyectable para determinismo en tests. Null = reloj real.</summary>
    public StubSavedSearchService(IUserCollection collection, IPropertyCatalogProvider catalog, Func<DateTimeOffset>? now)
    {
        _collection = collection ?? throw new ArgumentNullException(nameof(collection));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task<SavedSearch> SaveAsync(string owner, PropertyQuery criteria, string? label = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(owner))
        {
            throw new ArgumentException("El dueño de la búsqueda es obligatorio.", nameof(owner));
        }

        criteria ??= new PropertyQuery();
        var ownerKey = owner.Trim();

        // Id determinista por (owner, criterios) → idempotente: la misma búsqueda del
        // mismo usuario reusa el mismo id, y el UserCollection stub no duplica el itemRef.
        var queryJson = SerializeQuery(criteria);
        var id = "search_" + StableHash(ownerKey + Sep + queryJson);
        var friendly = string.IsNullOrWhiteSpace(label) ? Describe(criteria) : label!.Trim();
        var itemRef = string.Join(Sep, id, Encode(friendly), Encode(queryJson));

        await _collection.AddAsync(ownerKey, CollectionName, itemRef, cancellationToken);
        _index[id] = criteria; // que GetMatchesAsync resuelva sin re-leer la colección primero

        return new SavedSearch(id, ownerKey, friendly, criteria, _now());
    }

    public async Task<IReadOnlyList<SavedSearch>> GetForOwnerAsync(string owner, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(owner))
        {
            return Array.Empty<SavedSearch>();
        }

        var items = await _collection.GetAsync(owner.Trim(), CollectionName, cancellationToken);
        var list = new List<SavedSearch>(items.Count);
        foreach (var item in items)
        {
            if (TryParse(item.Owner, item.ItemRef, item.AddedAt, out var saved))
            {
                list.Add(saved);
            }
        }
        return list;
    }

    public async Task<SavedSearchMatches> GetMatchesAsync(string searchId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(searchId))
        {
            throw new ArgumentException("El id de la búsqueda es obligatorio.", nameof(searchId));
        }

        var id = searchId.Trim();
        var found = _index.TryGetValue(id, out var criteria)
            ? criteria
            : throw new ArgumentException($"Búsqueda '{id}' no encontrada.", nameof(searchId));

        var result = await _catalog.SearchAsync(found, cancellationToken);
        // Alerta simulada: los destacados cuentan como "novedad" que cumple la búsqueda;
        // si no hay destacados, el conteo es el total de matches (el adapter real diffea
        // contra un watermark persistido por búsqueda).
        var fresh = result.Listings.Where(l => l.Featured).ToList();
        var listings = fresh.Count > 0 ? fresh : result.Listings.ToList();
        return new SavedSearchMatches(id, listings.Count, listings);
    }

    // Índice id → criterios para resolver GetMatchesAsync sin re-leer la colección de
    // TODOS los usuarios (el UserCollection no indexa por itemRef). Se puebla en SaveAsync;
    // en un adapter real vive junto al store de la búsqueda.
    private readonly ConcurrentDictionary<string, PropertyQuery> _index = new(StringComparer.Ordinal);

    private static string SerializeQuery(PropertyQuery q)
        => JsonSerializer.Serialize(q, QueryJsonOptions);

    private bool TryParse(string owner, string itemRef, DateTimeOffset savedAt, out SavedSearch saved)
    {
        saved = default!;
        if (string.IsNullOrWhiteSpace(itemRef))
        {
            return false;
        }

        var parts = itemRef.Split(Sep);
        if (parts.Length != 3)
        {
            return false;
        }

        var id = parts[0];
        var label = Decode(parts[1]);
        var queryJson = Decode(parts[2]);

        PropertyQuery? criteria;
        try
        {
            criteria = JsonSerializer.Deserialize<PropertyQuery>(queryJson, QueryJsonOptions);
        }
        catch (JsonException)
        {
            return false;
        }
        if (criteria is null)
        {
            return false;
        }

        _index[id] = criteria;
        saved = new SavedSearch(id, owner, label, criteria, savedAt);
        return true;
    }

    private static readonly JsonSerializerOptions QueryJsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    // Descripción amable por defecto cuando el usuario no nombra la búsqueda.
    private static string Describe(PropertyQuery q)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(q.Type)) parts.Add(q.Type!);
        if (!string.IsNullOrWhiteSpace(q.Location)) parts.Add("en " + q.Location);
        if (q.Beds is > 0) parts.Add($"{q.Beds}+ hab");
        if (q.MaxPrice is > 0) parts.Add("hasta " + q.MaxPrice!.Value.ToString("N0", CultureInfo.InvariantCulture));
        if (!string.IsNullOrWhiteSpace(q.Text)) parts.Add($"\"{q.Text}\"");
        return parts.Count == 0 ? "Todos los inmuebles" : string.Join(", ", parts);
    }

    // Hash estable (FNV-1a) para el id determinista — NO usa GetHashCode (aleatorio
    // por proceso desde .NET Core). Hex corto, suficiente para colisiones de demo.
    private static string StableHash(string value)
    {
        const uint offset = 2166136261;
        const uint prime = 16777619;
        var hash = offset;
        foreach (var b in Encoding.UTF8.GetBytes(value))
        {
            hash ^= b;
            hash *= prime;
        }
        return hash.ToString("x8", CultureInfo.InvariantCulture);
    }

    // El itemRef usa '|' como separador; escapamos ese char en label/json para no romper
    // el parseo (percent-encoding mínimo de '|' y '%').
    private static string Encode(string s) => s.Replace("%", "%25", StringComparison.Ordinal).Replace("|", "%7C", StringComparison.Ordinal);

    private static string Decode(string s) => s.Replace("%7C", "|", StringComparison.Ordinal).Replace("%25", "%", StringComparison.Ordinal);
}
