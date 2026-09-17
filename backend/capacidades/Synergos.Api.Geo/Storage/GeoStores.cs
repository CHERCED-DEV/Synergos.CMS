using Microsoft.Extensions.Options;
using Synergos.Api.Geo.Domain;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.Api.Geo.Storage;

/// <summary>Dónde vive el almacén de esta capacidad.</summary>
public sealed class GeoStorageOptions
{
    public string Root { get; set; } = Path.Combine(AppContext.BaseDirectory, "data", "geo");
}

public interface IPlaceStore
{
    Place? Find(string id);
    Place? FindBySubject(Ref subject);
    IReadOnlyList<Place> All();
    void Put(Place place);
}

public sealed class FileSystemPlaceStore : IPlaceStore
{
    private readonly JsonCollectionStore<Place> _store;

    public FileSystemPlaceStore(IOptions<GeoStorageOptions> options)
        => _store = new JsonCollectionStore<Place>(options.Value.Root, "places", p => p.Id);

    public Place? Find(string id) => _store.Find(id);

    public Place? FindBySubject(Ref subject)
    {
        var hit = _store.Where(p => p.Subject == subject);
        return hit.Count > 0 ? hit[0] : null;
    }

    /// <summary>Todo el índice. Lo recorre la búsqueda por radio.</summary>
    /// <remarks>
    /// Barrido lineal: con el volumen de v1 es correcto y es honesto. El día que no lo sea hace
    /// falta un índice espacial (geohash o R-tree), y se cambia esta clase sin que nadie afuera
    /// se entere — el contrato es HTTP.
    /// </remarks>
    public IReadOnlyList<Place> All() => _store.All();

    public void Put(Place place) => _store.Put(place);
}
