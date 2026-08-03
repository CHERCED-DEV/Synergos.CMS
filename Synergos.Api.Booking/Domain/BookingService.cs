using Synergos.Api.Booking.Storage;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.Api.Booking.Domain;

/// <summary>
/// Compone las reglas puras de <see cref="BookingRules"/> con el almacén.
/// </summary>
/// <remarks>
/// <para><b>Por qué existe esta capa y los endpoints no llaman al almacén.</b> Las reglas son
/// puras y no saben leer; el almacén sabe leer y no tiene reglas. Alguien tiene que decidir el
/// ORDEN —qué se comprueba antes de escribir, y qué se escribe junto— y ese orden es la parte
/// que se rompe en silencio si vive repartida entre cinco endpoints.</para>
///
/// <para><b>El <c>lock</c> es la parte que importa.</b> Comprobar cupo y escribir el hold tiene
/// que ser <b>una</b> operación: sin él, dos peticiones simultáneas para el último cupo lo ven
/// libre las dos y se lo llevan las dos. Y es de proceso — dos instancias de esta API se
/// pisarían. Es aceptable con un despliegue único, que es el caso hoy, y es la primera razón por
/// la que habría que cambiar de almacén.</para>
///
/// <para>El reloj entra por <see cref="TimeProvider"/> y no se lee de <c>UtcNow</c>: los bordes
/// temporales son la mitad de los errores de una agenda, y sin reloj inyectable no se
/// reproducen.</para>
/// </remarks>
public sealed class BookingService
{
    /// <summary>Cuánto vale un hold si el llamador no pide otra cosa.</summary>
    public static readonly TimeSpan DefaultHoldTtl = TimeSpan.FromMinutes(10);

    /// <summary>Techo del TTL. Un hold eterno agota el recurso sin que nadie reserve.</summary>
    public static readonly TimeSpan MaxHoldTtl = TimeSpan.FromHours(2);

    private readonly IResourceStore _resources;
    private readonly IHoldStore _holds;
    private readonly IReservationStore _reservations;
    private readonly IIdempotencyLedger _idempotency;
    private readonly TimeProvider _clock;
    private readonly object _gate = new();

    public BookingService(
        IResourceStore resources,
        IHoldStore holds,
        IReservationStore reservations,
        IIdempotencyLedger idempotency,
        TimeProvider clock)
    {
        _resources = resources;
        _holds = holds;
        _reservations = reservations;
        _idempotency = idempotency;
        _clock = clock;
    }

    private DateTimeOffset Now => _clock.GetUtcNow();

    // ── Recursos ────────────────────────────────────────────────────────────

    public Result<Resource> RegisterResource(
        Ref subject, int capacity, string timeZoneId,
        IReadOnlyList<OpeningRule> opening, CancellationPolicy policy, IdempotencyKey key)
    {
        if (capacity <= 0)
        {
            return Rejection.Invalid($"{BookingRules.CodePrefix}.bad_capacity",
                "Un recurso con capacidad cero o negativa no puede reservarse nunca.");
        }

        lock (_gate)
        {
            if (_idempotency.Find("resource", key) is { } yaEra)
            {
                return _resources.Find(yaEra) is { } previo
                    ? Result.Ok(previo)
                    : Rejection.Conflict($"{BookingRules.CodePrefix}.idempotency_orphan",
                        "La llave ya se usó pero el recurso no está. El almacén quedó inconsistente.");
            }

            var id = Guid.NewGuid().ToString("n");
            var resource = new Resource(id, subject, capacity, timeZoneId, opening, policy);
            _resources.Put(resource);
            _idempotency.Remember("resource", key, id);
            return Result.Ok(resource);
        }
    }

    public Result<Resource> GetResource(string id)
        => _resources.Find(id) is { } r
            ? Result.Ok(r)
            : Rejection.NotFound($"{BookingRules.CodePrefix}.resource_not_found", $"No existe el recurso {id}.");

    public Page<Resource> ListResources(int offset, int limit)
    {
        var todos = _resources.All().OrderBy(r => r.Id, StringComparer.Ordinal).ToList();
        return new Page<Resource>(todos.Skip(offset).Take(limit).ToList(), todos.Count, offset);
    }

    // ── Disponibilidad ──────────────────────────────────────────────────────

    /// <summary>
    /// Si una ventana <b>concreta</b> se puede tomar, y cuánto cupo hay.
    /// </summary>
    /// <remarks>
    /// <b>Booking contesta sobre la ventana que le proponen; no publica una grilla.</b> Es
    /// deliberado: el tamaño de la franja —30 minutos de consulta, una noche de hotel, dos horas
    /// de aula— es una decisión del negocio, no de esta capacidad. Publicar una grilla propia
    /// habría obligado a elegir una granularidad, y esa elección la habría atado al primer
    /// dominio que la usara.
    /// </remarks>
    public Result<(bool Available, int Taken, int Capacity, Rejection? Reason)> CheckAvailability(
        string resourceId, TimeWindow window)
    {
        var resource = _resources.Find(resourceId);
        if (resource is null)
        {
            return Rejection.NotFound($"{BookingRules.CodePrefix}.resource_not_found", $"No existe el recurso {resourceId}.");
        }

        var ocupadas = Occupied(resourceId).ToList();
        var solapadas = ocupadas.Count(w => w.Overlaps(window));

        var motivo = BookingRules.CheckNotInThePast(window, Now)
                     ?? BookingRules.CheckOpeningHours(resource, window)
                     ?? BookingRules.CheckCapacity(resource, ocupadas, window);

        return Result.Ok((motivo is null, solapadas, resource.Capacity, motivo));
    }

    // ── Holds ───────────────────────────────────────────────────────────────

    public Result<Hold> CreateHold(string resourceId, TimeWindow window, Ref heldFor, TimeSpan? ttl, IdempotencyKey key)
    {
        lock (_gate)
        {
            // La idempotencia se resuelve ANTES que cualquier regla que dependa del estado.
            // Al revés —como estuvo— un reintento tras un timeout se topaba con el cupo que él
            // mismo había tomado y salía rechazado por at_capacity: exactamente lo que la llave
            // existía para evitar. Lo destapó un run con dos procesos, no un test.
            if (_idempotency.Find("hold", key) is { } yaEra)
            {
                return _holds.Find(yaEra) is { } previo
                    ? Result.Ok(previo)
                    : Rejection.Conflict($"{BookingRules.CodePrefix}.idempotency_orphan",
                        "La llave ya se usó pero el hold no está.");
            }

            var resource = _resources.Find(resourceId);
            if (resource is null)
            {
                return Rejection.NotFound($"{BookingRules.CodePrefix}.resource_not_found", $"No existe el recurso {resourceId}.");
            }

            var vigencia = ttl ?? DefaultHoldTtl;
            if (vigencia <= TimeSpan.Zero || vigencia > MaxHoldTtl)
            {
                return Rejection.Invalid($"{BookingRules.CodePrefix}.bad_ttl",
                    $"El TTL del hold tiene que estar entre 0 y {MaxHoldTtl}.");
            }

            // El orden importa: primero lo que depende solo de la petición, después lo que
            // depende del estado. Así el llamador que manda una ventana mala recibe "arreglá
            // lo que mandaste" y no "no hay cupo", que lo mandaría a reintentar en vano.
            var motivo = BookingRules.CheckNotInThePast(window, Now)
                         ?? BookingRules.CheckOpeningHours(resource, window)
                         ?? BookingRules.CheckCapacity(resource, Occupied(resourceId), window);
            if (motivo is not null) return Result.Rejected<Hold>(motivo);

            var id = Guid.NewGuid().ToString("n");
            var hold = new Hold(id, resourceId, window, heldFor, Now, Now + vigencia);
            _holds.Put(hold);
            _idempotency.Remember("hold", key, id);
            return Result.Ok(hold);
        }
    }

    public Result<Hold> GetHold(string id)
        => _holds.Find(id) is { } h
            ? Result.Ok(h)
            : Rejection.NotFound($"{BookingRules.CodePrefix}.hold_not_found", $"No existe el hold {id}.");

    public Result<Hold> ReleaseHold(string id)
    {
        lock (_gate)
        {
            var hold = _holds.Find(id);
            if (hold is null)
            {
                return Rejection.NotFound($"{BookingRules.CodePrefix}.hold_not_found", $"No existe el hold {id}.");
            }
            if (hold.ConfirmedReservationId is not null)
            {
                return Rejection.Conflict($"{BookingRules.CodePrefix}.hold_already_confirmed",
                    $"El hold ya produjo la reserva {hold.ConfirmedReservationId}; se cancela la reserva, no el hold.");
            }

            // Soltar un hold ya suelto NO es un error: es el resultado que el llamador quería.
            // Rechazarlo obligaría a cada cliente a distinguir "no pude" de "ya estaba", que es
            // exactamente el ruido que un release debería ahorrarle.
            if (hold.Released) return Result.Ok(hold);

            var soltado = hold with { Released = true };
            _holds.Put(soltado);
            return Result.Ok(soltado);
        }
    }

    // ── Reservas ────────────────────────────────────────────────────────────

    public Result<Reservation> ConfirmHold(string holdId, IdempotencyKey key)
    {
        lock (_gate)
        {
            // Igual que en CreateHold: sin esto, reintentar una confirmación se topa con
            // hold_already_confirmed —el estado que el primer intento dejó— en vez de devolver
            // la reserva que ya existe.
            if (_idempotency.Find("reservation", key) is { } yaEra)
            {
                return _reservations.Find(yaEra) is { } previa
                    ? Result.Ok(previa)
                    : Rejection.Conflict($"{BookingRules.CodePrefix}.idempotency_orphan",
                        "La llave ya se usó pero la reserva no está.");
            }

            var hold = _holds.Find(holdId);
            if (hold is null)
            {
                return Rejection.NotFound($"{BookingRules.CodePrefix}.hold_not_found", $"No existe el hold {holdId}.");
            }

            var motivo = BookingRules.CheckHoldIsUsable(hold, Now);
            if (motivo is not null) return Result.Rejected<Reservation>(motivo);

            var id = Guid.NewGuid().ToString("n");
            var reserva = new Reservation(id, hold.ResourceId, hold.Id, hold.Window, hold.HeldFor, Now);
            _reservations.Put(reserva);
            _idempotency.Remember("reservation", key, id);
            // El hold apunta a su reserva: deja de contar como hold y el cupo lo ocupa ella.
            // Sin esto, cada confirmación consumiría dos plazas del recurso.
            _holds.Put(hold with { ConfirmedReservationId = id });
            return Result.Ok(reserva);
        }
    }

    public Result<Reservation> GetReservation(string id)
        => _reservations.Find(id) is { } r
            ? Result.Ok(r)
            : Rejection.NotFound($"{BookingRules.CodePrefix}.reservation_not_found", $"No existe la reserva {id}.");

    public Result<Page<Reservation>> ListReservations(string resourceId, int offset, int limit)
    {
        if (_resources.Find(resourceId) is null)
        {
            return Rejection.NotFound($"{BookingRules.CodePrefix}.resource_not_found", $"No existe el recurso {resourceId}.");
        }

        var todas = _reservations.ForResource(resourceId)
            .OrderBy(r => r.Window.Start)
            .ThenBy(r => r.Id, StringComparer.Ordinal)   // desempate estable: sin él, dos peticiones iguales devuelven órdenes distintos
            .ToList();

        return Result.Ok(new Page<Reservation>(todas.Skip(offset).Take(limit).ToList(), todas.Count, offset));
    }

    public Result<Reservation> CancelReservation(string id)
    {
        lock (_gate)
        {
            var reserva = _reservations.Find(id);
            if (reserva is null)
            {
                return Rejection.NotFound($"{BookingRules.CodePrefix}.reservation_not_found", $"No existe la reserva {id}.");
            }

            var resource = _resources.Find(reserva.ResourceId);
            if (resource is null)
            {
                return Rejection.Conflict($"{BookingRules.CodePrefix}.resource_gone",
                    "La reserva apunta a un recurso que ya no está.");
            }

            var motivo = BookingRules.CheckCancellable(reserva, resource.Policy, Now);
            if (motivo is not null) return Result.Rejected<Reservation>(motivo);

            var cancelada = reserva with { Status = ReservationStatus.Cancelled, CancelledAt = Now };
            _reservations.Put(cancelada);
            return Result.Ok(cancelada);
        }
    }

    // ── Ocupación ───────────────────────────────────────────────────────────

    /// <summary>
    /// Las ventanas que hoy ocupan cupo: holds activos <b>más</b> reservas vigentes.
    /// </summary>
    /// <remarks>
    /// Contar solo las reservas dejaría entrar sobrecupo mientras alguien está a mitad de pagar;
    /// contar solo los holds perdería las reservas ya confirmadas. Son las dos, y por eso el hold
    /// confirmado deja de contarse como hold (ver <see cref="Hold.IsActive"/>).
    /// </remarks>
    private IEnumerable<TimeWindow> Occupied(string resourceId)
    {
        var ahora = Now;
        return _holds.ForResource(resourceId).Where(h => h.IsActive(ahora)).Select(h => h.Window)
            .Concat(_reservations.ForResource(resourceId).Where(r => r.IsActive).Select(r => r.Window));
    }
}
