using Microsoft.Extensions.Options;
using Synergos.Api.Inventory.Domain;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.Api.Inventory.Storage;

/// <summary>Dónde vive el almacén de esta capacidad.</summary>
public sealed class InventoryStorageOptions
{
    public string Root { get; set; } = Path.Combine(AppContext.BaseDirectory, "data", "inventory");
}

public interface IStockStore
{
    StockItem? Find(string id);
    StockItem? FindBySubject(Ref subject);
    StockItem? FindByHold(string holdId);
    void Put(StockItem item);
}

public sealed class FileSystemStockStore : IStockStore
{
    private readonly JsonCollectionStore<StockItem> _store;

    public FileSystemStockStore(IOptions<InventoryStorageOptions> options)
        => _store = new JsonCollectionStore<StockItem>(options.Value.Root, "stock", i => i.Id);

    public StockItem? Find(string id) => _store.Find(id);

    public StockItem? FindBySubject(Ref subject)
    {
        var hit = _store.Where(i => i.Subject == subject);
        return hit.Count > 0 ? hit[0] : null;
    }

    /// <summary>El ítem que contiene ese apartado. Los apartados viven dentro del ítem.</summary>
    /// <remarks>
    /// Viven dentro y no en su propia colección porque la comprobación de cupo los lee TODOS
    /// junto al total: separarlos obligaría a dos lecturas que hay que mantener coherentes entre
    /// sí, y esa coherencia es justo lo que se rompe bajo carga.
    /// </remarks>
    public StockItem? FindByHold(string holdId)
    {
        var hit = _store.Where(i => i.Holds.Any(h => h.Id == holdId));
        return hit.Count > 0 ? hit[0] : null;
    }

    public void Put(StockItem item) => _store.Put(item);
}
