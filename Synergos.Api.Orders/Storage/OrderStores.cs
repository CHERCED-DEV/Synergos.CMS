using Microsoft.Extensions.Options;
using Synergos.Api.Orders.Domain;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.Api.Orders.Storage;

/// <summary>Dónde vive el almacén de esta capacidad.</summary>
public sealed class OrderStorageOptions
{
    public string Root { get; set; } = Path.Combine(AppContext.BaseDirectory, "data", "orders");
}

public interface IOrderStore
{
    Order? Find(string id);
    IReadOnlyList<Order> ForBuyer(Ref buyer);
    void Put(Order order);
}

public sealed class FileSystemOrderStore : IOrderStore
{
    private readonly JsonCollectionStore<Order> _store;

    public FileSystemOrderStore(IOptions<OrderStorageOptions> options)
        => _store = new JsonCollectionStore<Order>(options.Value.Root, "orders", o => o.Id);

    public Order? Find(string id) => _store.Find(id);
    public IReadOnlyList<Order> ForBuyer(Ref buyer) => _store.Where(o => o.Buyer == buyer);
    public void Put(Order order) => _store.Put(order);
}
