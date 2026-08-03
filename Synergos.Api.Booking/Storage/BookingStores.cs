using Microsoft.Extensions.Options;
using Synergos.Api.Booking.Domain;
using Synergos.Shared;

namespace Synergos.Api.Booking.Storage;

/// <summary>Dónde vive el almacén de esta capacidad.</summary>
public sealed class BookingStorageOptions
{
    public string Root { get; set; } = Path.Combine(AppContext.BaseDirectory, "data", "booking");
}

public interface IResourceStore
{
    Resource? Find(string id);
    IReadOnlyList<Resource> All();
    void Put(Resource resource);
}

public interface IHoldStore
{
    Hold? Find(string id);
    IReadOnlyList<Hold> ForResource(string resourceId);
    void Put(Hold hold);
}

public interface IReservationStore
{
    Reservation? Find(string id);
    IReadOnlyList<Reservation> ForResource(string resourceId);
    void Put(Reservation reservation);
}

public sealed class FileSystemResourceStore : IResourceStore
{
    private readonly JsonCollectionStore<Resource> _store;

    public FileSystemResourceStore(IOptions<BookingStorageOptions> options)
        => _store = new JsonCollectionStore<Resource>(options.Value.Root, "resources", r => r.Id);

    public Resource? Find(string id) => _store.Find(id);
    public IReadOnlyList<Resource> All() => _store.All();
    public void Put(Resource resource) => _store.Put(resource);
}

public sealed class FileSystemHoldStore : IHoldStore
{
    private readonly JsonCollectionStore<Hold> _store;

    public FileSystemHoldStore(IOptions<BookingStorageOptions> options)
        => _store = new JsonCollectionStore<Hold>(options.Value.Root, "holds", h => h.Id);

    public Hold? Find(string id) => _store.Find(id);
    public IReadOnlyList<Hold> ForResource(string resourceId)
        => _store.Where(h => string.Equals(h.ResourceId, resourceId, StringComparison.Ordinal));
    public void Put(Hold hold) => _store.Put(hold);
}

public sealed class FileSystemReservationStore : IReservationStore
{
    private readonly JsonCollectionStore<Reservation> _store;

    public FileSystemReservationStore(IOptions<BookingStorageOptions> options)
        => _store = new JsonCollectionStore<Reservation>(options.Value.Root, "reservations", r => r.Id);

    public Reservation? Find(string id) => _store.Find(id);
    public IReadOnlyList<Reservation> ForResource(string resourceId)
        => _store.Where(r => string.Equals(r.ResourceId, resourceId, StringComparison.Ordinal));
    public void Put(Reservation reservation) => _store.Put(reservation);
}

/// <summary>El ledger de idempotencia de esta capacidad, sobre su propio almacén.</summary>
public sealed class FileSystemIdempotencyStore : FileIdempotencyLedger
{
    public FileSystemIdempotencyStore(IOptions<BookingStorageOptions> options) : base(options.Value.Root) { }
}
