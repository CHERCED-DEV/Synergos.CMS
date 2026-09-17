using Microsoft.Extensions.Options;
using Synergos.Api.Fulfillment.Domain;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.Api.Fulfillment.Storage;

/// <summary>Dónde vive el almacén de esta capacidad.</summary>
public sealed class FulfillmentStorageOptions
{
    public string Root { get; set; } = Path.Combine(AppContext.BaseDirectory, "data", "fulfillment");
}

public interface IShipmentStore
{
    Shipment? Find(string id);
    Shipment? FindByTracking(string code);
    IReadOnlyList<Shipment> ForSubject(Ref subject);
    void Put(Shipment shipment);
}

public sealed class FileSystemShipmentStore : IShipmentStore
{
    private readonly JsonCollectionStore<Shipment> _store;

    public FileSystemShipmentStore(IOptions<FulfillmentStorageOptions> options)
        => _store = new JsonCollectionStore<Shipment>(options.Value.Root, "shipments", s => s.Id);

    public Shipment? Find(string id) => _store.Find(id);

    public Shipment? FindByTracking(string code)
    {
        var hit = _store.Where(s => string.Equals(s.TrackingCode, code, StringComparison.OrdinalIgnoreCase));
        return hit.Count > 0 ? hit[0] : null;
    }

    public IReadOnlyList<Shipment> ForSubject(Ref subject) => _store.Where(s => s.For == subject);

    public void Put(Shipment shipment) => _store.Put(shipment);
}
