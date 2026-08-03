using Microsoft.Extensions.Options;
using Synergos.Api.Booking.Domain;
using Synergos.Core;

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

/// <summary>
/// Recuerda qué produjo cada llave de idempotencia.
/// </summary>
/// <remarks>
/// <para>Es lo que hace que reintentar una mutación no la ejecute dos veces. Cuando esta API no
/// contesta, el llamador <b>no sabe</b> si se ejecutó; sin esto, reintentar duplica el hold y no
/// reintentar pierde la reserva.</para>
///
/// <para>La llave se guarda por <b>ámbito</b> (<c>hold</c>, <c>reservation</c>) y no global: dos
/// operaciones distintas pueden legítimamente traer la misma llave del mismo cliente, y
/// colisionarlas haría que la segunda devolviera el resultado de la primera.</para>
/// </remarks>
public interface IIdempotencyStore
{
    /// <summary>Qué produjo esta llave, o <c>null</c> si no se ha usado.</summary>
    /// <remarks>
    /// <b>Separado de <see cref="Remember"/> a propósito.</b> Cuando estaban fundidos, el
    /// servicio tenía que validar ANTES de consultar la llave — y entonces un reintento de un
    /// hold se topaba con su propio cupo tomado y salía rechazado por <c>at_capacity</c>: justo
    /// lo que la llave existía para evitar. Con la consulta aparte, el reintento devuelve lo ya
    /// hecho sin volver a pasar por reglas que el estado que él mismo creó ya no satisface.
    /// </remarks>
    string? Find(string scope, IdempotencyKey key);

    /// <summary>Asocia la llave a lo que produjo.</summary>
    void Remember(string scope, IdempotencyKey key, string resultId);
}

/// <summary>Lo que se guarda de una llave usada.</summary>
internal sealed record IdempotencyEntry(string Id, string ResultId);

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

public sealed class FileSystemIdempotencyStore : IIdempotencyStore
{
    private readonly JsonCollectionStore<IdempotencyEntry> _store;
    private readonly object _gate = new();

    public FileSystemIdempotencyStore(IOptions<BookingStorageOptions> options)
        => _store = new JsonCollectionStore<IdempotencyEntry>(options.Value.Root, "idempotency", e => e.Id);

    public string? Find(string scope, IdempotencyKey key)
    {
        lock (_gate) { return _store.Find(Clave(scope, key))?.ResultId; }
    }

    public void Remember(string scope, IdempotencyKey key, string resultId)
    {
        // La atomicidad de consultar-y-escribir la garantiza el lock del servicio, que abarca
        // toda la operación: acá alcanza con proteger el fichero.
        lock (_gate) { _store.Put(new IdempotencyEntry(Clave(scope, key), resultId)); }
    }

    private static string Clave(string scope, IdempotencyKey key) => $"{scope}|{key.Value}";
}
