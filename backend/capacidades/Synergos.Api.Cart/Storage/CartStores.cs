using Microsoft.Extensions.Options;
using Synergos.Core;
using Synergos.Shared;
using Modelo = Synergos.Api.Cart.Domain;

namespace Synergos.Api.Cart.Storage;

/// <summary>Dónde vive el almacén de esta capacidad.</summary>
public sealed class CartStorageOptions
{
    public string Root { get; set; } = Path.Combine(AppContext.BaseDirectory, "data", "cart");
}

public interface ICartStore
{
    Modelo.Cart? Find(string id);
    IReadOnlyList<Modelo.Cart> ForOwner(Ref owner);
    void Put(Modelo.Cart cart);
}

public sealed class FileSystemCartStore : ICartStore
{
    private readonly JsonCollectionStore<Modelo.Cart> _store;

    public FileSystemCartStore(IOptions<CartStorageOptions> options)
        => _store = new JsonCollectionStore<Modelo.Cart>(options.Value.Root, "carts", c => c.Id);

    public Modelo.Cart? Find(string id) => _store.Find(id);
    public IReadOnlyList<Modelo.Cart> ForOwner(Ref owner) => _store.Where(c => c.Owner == owner);
    public void Put(Modelo.Cart cart) => _store.Put(cart);
}
