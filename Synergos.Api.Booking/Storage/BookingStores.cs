using Microsoft.Extensions.Options;
using Synergos.Api.Booking.Domain;
using Synergos.Core;
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

    /// <summary>Las reservas tomadas para un <see cref="Ref"/>, sea cual sea el recurso.</summary>
    /// <remarks>
    /// <para>El simétrico de <see cref="ForResource"/>: uno contesta «la agenda de este
    /// consultorio», el otro «las citas de esta persona». Faltaba, y sin él la única forma de
    /// contestar la segunda era barrer los recursos de a uno preguntando por cada agenda — o sea
    /// que el llamador tenía que saber de antemano por dónde buscar lo que vino a buscar.</para>
    ///
    /// <para><b>No hay índice, y es a propósito.</b> Un mapa <c>Ref → reservas</c> sería una
    /// segunda verdad que se desincroniza en cada cancelación, y esta capacidad existe para no
    /// tener dos. El coste está medido y escrito en
    /// <see cref="Domain.BookingService.ListReservations"/>: es el MISMO recorrido que
    /// <see cref="ForResource"/> ya hacía.</para>
    /// </remarks>
    IReadOnlyList<Reservation> ForWhom(Ref forWhom);

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

    // El Ref se compara ENTERO y por igualdad de valor: no se mira su Kind ni se parte su Id.
    // Comparar sólo el Id haría que dos vocabularios distintos con el mismo identificador —un
    // "1" de salud y un "1" de viajes— se vieran las reservas (§0.B.13).
    public IReadOnlyList<Reservation> ForWhom(Ref forWhom) => _store.Where(r => r.For == forWhom);

    public void Put(Reservation reservation) => _store.Put(reservation);
}

/// <summary>El ledger de idempotencia de esta capacidad, sobre su propio almacén.</summary>
public sealed class FileSystemIdempotencyStore : FileIdempotencyLedger
{
    public FileSystemIdempotencyStore(IOptions<BookingStorageOptions> options) : base(options.Value.Root) { }
}
