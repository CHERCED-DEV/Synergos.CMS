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

    /// <summary>Busca el recurso que lleva la agenda de un <see cref="Ref"/>.</summary>
    /// <remarks>
    /// <para><b>Sin esto, un consumidor no puede llegar de su entidad a su agenda.</b> Quien
    /// tiene una referencia —un orquestador que sabe qué profesional, qué sala, qué cancha—
    /// <b>no</b> tiene el identificador interno del recurso, que esta capacidad genera al
    /// registrarlo. Tendría que mantener un mapa entidad→recurso por su cuenta, y ese mapa es una
    /// segunda verdad que se desincroniza — justo lo que una capacidad existe para evitar: la
    /// referencia que guarda tiene que poder consultarse.</para>
    ///
    /// <para>Es el mismo razonamiento —y el mismo remedio— que <c>Api.Inventory.GetBySubject</c>,
    /// que ya lo había resuelto para las existencias. Que faltara acá lo destapó cablear la cita
    /// clínica contra los procesos vivos (HU #25): <c>Bff.Salud</c> exigía un <c>resourceId</c>
    /// que nadie río arriba podía conocer.</para>
    /// </remarks>
    public Result<Resource> GetResourceBySubject(Ref subject)
        => _resources.All().FirstOrDefault(r => r.Subject == subject) is { } r
            ? Result.Ok(r)
            : Rejection.NotFound($"{BookingRules.CodePrefix}.resource_not_found",
                $"No hay recurso registrado para {subject}.");

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

    /// <summary>
    /// Las reservas de un recurso, las de un <see cref="Ref"/>, o las de los dos a la vez.
    /// </summary>
    /// <param name="resourceId">Sobre qué recurso. Nulo = cualquiera.</param>
    /// <param name="forWhom">Para quién se tomaron. Nulo = cualquiera.</param>
    /// <param name="offset">Desde qué posición.</param>
    /// <param name="limit">Cuántas.</param>
    /// <remarks>
    /// <para><b>Faltaba la dirección que se usa más.</b> Hasta acá sólo se podía preguntar por
    /// <paramref name="resourceId"/>, así que «mis citas», «mis visitas al inmueble» y «mis
    /// reservas de viaje» exigían saber de antemano por qué recurso preguntar — y el
    /// identificador del recurso <b>lo genera esta capacidad</b>, no lo tiene nadie río arriba.
    /// Es el simétrico exacto de lo que <see cref="GetResourceBySubject"/> cerró en la otra
    /// dirección (HU #25): allá se iba del sujeto a su agenda, acá del actor a sus reservas.</para>
    ///
    /// <para><b>Sin ninguno de los dos se rechaza, y eso NO cambió con esta HU</b> — lo que
    /// cambió es que la razón ya se puede escribir bien. Antes el rechazo se llamaba
    /// <c>resource_id_required</c>: el instinto correcto («sin filtro esto es un volcado del
    /// almacén entero por HTTP») dicho sobre el único filtro que existía. Con dos filtros ese
    /// nombre pasaba a MENTIR —«hace falta resourceId» cuando un <c>for</c> también sirve—, así
    /// que el código es <c>filter_required</c> y nombra la regla en vez del campo. Listar todo
    /// sigue sin ser una opción: crece con el almacén, no cabe en una página, y el llamador que
    /// de verdad lo quisiera está pidiendo un informe, que no es esto.</para>
    ///
    /// <para><b>Un actor sin reservas devuelve VACÍO, no <c>not_found</c>.</b> La asimetría con
    /// <paramref name="resourceId"/> es deliberada: un identificador de recurso lo generó esta
    /// capacidad, así que preguntar por uno que no existe es un error del llamador y vale la pena
    /// nombrarlo; un <see cref="Ref"/> es vocabulario de QUIEN LLAMA y esta capacidad no puede
    /// saber si existe —comprobarlo exigiría interpretarlo, que es justo lo que §0.B.13
    /// prohíbe—. Y «esta persona no tiene citas» es una respuesta verdadera y útil: con
    /// <c>not_found</c>, cada portal tendría que tratar una bandeja vacía como un fallo, que es
    /// como se acaba enseñando un error a quien simplemente todavía no reservó.</para>
    ///
    /// <para><b>Los dos juntos intersecan</b> («las citas de esta persona con este profesional»),
    /// que es lo único que pueden significar dos filtros sobre la misma lista.</para>
    ///
    /// <para><b>Atribuir no es autorizar, y esta HU no contesta lo segundo.</b> Quien tenga la
    /// llave compartida puede listar las reservas de otro si adivina su <see cref="Ref"/>. Es la
    /// misma pregunta que <c>Api.Cart</c> dejó abierta en la HU #14 y por la misma razón: la
    /// puerta de identidad se cablea cuando hay un consumidor que PRESENTE identidad contra el
    /// que verificarla, no contra un fake. Queda dicho en vez de omitido para que la próxima
    /// auditoría no lo dé por resuelto.</para>
    ///
    /// <para><b>El coste, MEDIDO y no supuesto.</b> Desde el #112 el almacén es un fichero por
    /// documento y <c>JsonCollectionStore.Where</c> es <c>All().Where(...)</c>: recorre el
    /// directorio entero, así que lo que domina es la N y el predicado es ruido. Medido contra
    /// <c>FileSystemReservationStore</c> de verdad (Release, caché del sistema de ficheros
    /// caliente, un proceso, media de 5 vueltas):</para>
    /// <code>
    /// N = 100    ForWhom   4,1 ms    ForResource   3,4 ms
    /// N = 1.000  ForWhom  37,1 ms    ForResource  34,8 ms
    /// N = 5.000  ForWhom 141,4 ms    ForResource 112,5 ms
    /// </code>
    /// <para>O sea que filtrar por actor cuesta <b>lo mismo</b> que filtrar por recurso —la misma
    /// N, el mismo recorrido—, y este endpoint <b>no abre una clase de coste nueva</b>: la N ya
    /// estaba ahí desde que existía el listado por recurso. Lo que sí hace es acercar el
    /// disparador escrito, y eso va dicho en vez de escondido: «la agenda del consultorio» la
    /// mira el profesional unas cuantas veces al día, y <b>«mis citas» se pinta en cada carga del
    /// portal</b>. Con 5.000 reservas eso son ~140 ms de disco por carga, que ya es un trozo
    /// visible de una página. <b>El disparador sigue siendo el que CLAUDE.md §11 tiene escrito
    /// —el día que listar duela— y este cambio lo acerca sin cruzarlo.</b></para>
    ///
    /// <para><b>No se añade índice, y la razón es la de siempre.</b> Un mapa
    /// <c>Ref → reservas</c> es estado duplicado que hay que mantener en cada confirmación y cada
    /// cancelación; el día que se desincronice, una cita que existe deja de aparecer en la
    /// bandeja de su dueño <b>sin que nada falle</b> — el peor modo de fallo de los dos, porque
    /// un listado lento se nota y uno incompleto no. Es el mismo razonamiento que
    /// <c>LoadByOrderRefAsync</c> del CMS dejó escrito para preferir filtrar antes que duplicar
    /// estado. Cuando la N duela, lo que corresponde es cambiar de almacén —donde el índice lo
    /// mantiene quien escribe— y no fabricar acá medio motor de base de datos.</para>
    /// </remarks>
    public Result<Page<Reservation>> ListReservations(string? resourceId, Ref? forWhom, int offset, int limit)
    {
        if (string.IsNullOrWhiteSpace(resourceId) && forWhom is null)
        {
            return Rejection.Invalid($"{BookingRules.CodePrefix}.filter_required",
                "Hace falta filtrar por recurso o por actor: sin filtro esto es un volcado del almacén.");
        }

        IEnumerable<Reservation> candidatas;
        if (!string.IsNullOrWhiteSpace(resourceId))
        {
            if (_resources.Find(resourceId) is null)
            {
                return Rejection.NotFound($"{BookingRules.CodePrefix}.resource_not_found", $"No existe el recurso {resourceId}.");
            }

            candidatas = _reservations.ForResource(resourceId);
            if (forWhom is not null) candidatas = candidatas.Where(r => r.For == forWhom);
        }
        else
        {
            candidatas = _reservations.ForWhom(forWhom!);
        }

        var todas = candidatas
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
