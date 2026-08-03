using Microsoft.Extensions.Options;
using Synergos.Api.Catalog.Domain;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.Api.Catalog.Storage;

/// <summary>Dónde vive el almacén de esta capacidad.</summary>
public sealed class CatalogStorageOptions
{
    public string Root { get; set; } = Path.Combine(AppContext.BaseDirectory, "data", "catalog");
}

public interface ICatalogStore
{
    CatalogItem? Find(string id);
    CatalogItem? FindBySubject(Ref subject);
    IReadOnlyList<CatalogItem> Indexed();
    void Put(CatalogItem item);
}

public sealed class FileSystemCatalogStore : ICatalogStore
{
    private readonly JsonCollectionStore<CatalogItem> _store;

    public FileSystemCatalogStore(IOptions<CatalogStorageOptions> options)
        => _store = new JsonCollectionStore<CatalogItem>(options.Value.Root, "items", i => i.Id);

    public CatalogItem? Find(string id) => _store.Find(id);

    public CatalogItem? FindBySubject(Ref subject)
    {
        var hit = _store.Where(i => i.Subject == subject);
        return hit.Count > 0 ? hit[0] : null;
    }

    public IReadOnlyList<CatalogItem> Indexed() => _store.Where(i => !i.Unindexed);

    public void Put(CatalogItem item) => _store.Put(item);
}
