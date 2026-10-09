using System.Text.Encodings.Web;
using System.Text.Json;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// El registro de las entradas emitidas: quién las tiene, cuáles ya pasaron por la puerta, y a
/// quién se transfirieron.
/// </summary>
/// <remarks>
/// <para><b>Está afuera del motor de compra por una razón que se descubrió al ir a cablear el
/// orquestador</b> (HU #35, rebanada 2b), y no es de estilo. La cara de organizador —el escaneo
/// en la puerta, la lista de asistentes— colgaba del motor de compra CONCRETO. Cambiar por dónde
/// se compra habría dejado la puerta leyendo un almacén vacío: las entradas existirían, el
/// escáner diría <c>invalid</c>, y nada en el build habría avisado.</para>
///
/// <para><b>El reparto que sale de ahí es el mismo de todo este repo:</b> comprar y emitir son
/// dos cosas. Comprar cambia según por dónde se compre —el motor en proceso o el orquestador—;
/// el artefacto no cambia nunca, porque el firmante del QR vive de este lado y ahí se queda. Así
/// que <b>el registro es UNO</b> y los caminos de compra son dos.</para>
///
/// <para><b>Ciclo de vida del artefacto, y nada más:</b> acá no hay catálogo, ni reservas, ni
/// cobros, ni orquestador. Lo que entra ya está comprado. Lo que sale es lo que el portador ve y
/// lo que la puerta comprueba.</para>
///
/// <para>La familia del almacén (<c>event-orders</c>) es la MISMA de antes a propósito: lo ya
/// emitido sigue leyéndose sin migrar nada.</para>
/// </remarks>
public sealed class EventTicketLedger
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,   // acentos es-CO legibles en disco
    };

    /// <summary>Familia de entidades en el store genérico (→ App_Data/syn-event-orders/).</summary>
    public const string ResourceType = "event-orders";

    private readonly IJsonEntityStore _store;
    private readonly EventTicketIssuer _issuer;
    private readonly ITicketSigner? _signer;
    private readonly IAuditTrailWriter? _audit;
    private readonly Func<DateTimeOffset> _now;

    /// <param name="store">Dónde viven las compras. Null ≡ en memoria del proceso.</param>
    /// <param name="signer">Verifica en la PUERTA. Null ≡ fail-closed: no se abre nada.</param>
    /// <param name="audit">Asienta las transferencias. Null ≡ no auditar.</param>
    /// <param name="now">Reloj inyectable para determinismo en tests.</param>
    public EventTicketLedger(
        IJsonEntityStore? store = null,
        ITicketSigner? signer = null,
        IAuditTrailWriter? audit = null,
        Func<DateTimeOffset>? now = null)
    {
        _store = store ?? new InMemoryJsonEntityStore();
        _signer = signer;
        _issuer = new EventTicketIssuer(signer);
        _audit = audit;
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    // ── Lo que el camino de compra escribe y lee ────────────────────────────

    /// <summary>Guarda la compra tal cual. Sobrescribe la que hubiera con el mismo <c>OrderRef</c>.</summary>
    public Task SaveAsync(PersistedEventOrder order, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(order);
        return _store.WriteAsync(ResourceType, order.OrderRef, JsonSerializer.Serialize(order, Json), cancellationToken);
    }

    /// <summary>La compra, o <c>null</c> si no existe o el fichero está corrupto.</summary>
    public async Task<PersistedEventOrder?> LoadAsync(string? orderRef, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(orderRef))
        {
            return null;
        }
        var json = await _store.ReadAsync(ResourceType, orderRef.Trim(), cancellationToken);
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }
        try { return JsonSerializer.Deserialize<PersistedEventOrder>(json, Json); }
        catch (JsonException) { return null; }   // archivo corrupto → como si no existiera
    }

    // ── Una saga, una orden (ADR 0140 F3) ───────────────────────────────────

    /// <summary>
    /// Los cerrojos de las sagas: 64, fijos, y una saga cae siempre en el mismo.
    /// </summary>
    /// <remarks>
    /// <para><b>Por qué hacen falta.</b> Anotar y confirmar la orden de una saga son leer el registro,
    /// decidir y escribir. Sin exclusión, dos peticiones a la vez —el doble render que pide las entradas
    /// dos veces, dos checkouts con los mismos datos, «mis entradas» reconciliando mientras alguien
    /// confirma— leían las dos «no hay orden» y escribían dos, o confirmaban la gemela que la otra ya
    /// había dejado atrás. Dos órdenes confirmadas de una saga son las mismas butacas dos veces.</para>
    ///
    /// <para><b>De proceso, y alcanza</b>: el almacén de estas órdenes es de una sola instancia
    /// (<c>FileSystemJsonEntityStore</c> lo dice), así que todo el que escribe pasa por acá. Estáticos
    /// para que dos registros sobre el mismo almacén no se crean dueños de la misma saga a la vez. Un
    /// número fijo y no uno por saga: un diccionario de cerrojos crece con cada compra para siempre, y
    /// compartir uno entre dos sagas sólo las pone en fila.</para>
    /// </remarks>
    private static readonly SemaphoreSlim[] Cerrojos =
        Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    private static SemaphoreSlim CerrojoDe(string sagaId)
        => Cerrojos[(int)((uint)StringComparer.Ordinal.GetHashCode(sagaId) % (uint)Cerrojos.Length)];

    /// <summary>
    /// La orden de una saga tal como la ve el registro: la confirmada si la hay; si no, la pendiente más
    /// antigua. Nula si ninguna orden la nombra.
    /// </summary>
    private static PersistedEventOrder? DeLaSaga(IEnumerable<PersistedEventOrder> todas, string sagaId)
    {
        var suyas = todas
            .Where(o => string.Equals(o.PaymentSessionId, sagaId, StringComparison.Ordinal))
            .OrderBy(o => o.CreatedAt)
            .ToList();
        return suyas.FirstOrDefault(o => o.Status == EventOrderStatus.Confirmed) ?? suyas.FirstOrDefault();
    }

    /// <summary>
    /// Anota la orden PENDIENTE de una saga, bajo su cerrojo: <paramref name="anotar"/> recibe la que ya hay
    /// (o nula) y devuelve la que se guarda, o nula para no guardar nada.
    /// </summary>
    /// <remarks>
    /// <b>Una saga tiene una orden</b>: si ya hay una, la que se guarda tiene que ser ESA —su
    /// <c>OrderRef</c>—, y si ya está confirmada no se toca ni se llama a <paramref name="anotar"/>: una
    /// orden confirmada no vuelve a pendiente.
    /// </remarks>
    /// <returns>La orden guardada, o nula si no se guardó nada.</returns>
    /// <exception cref="InvalidOperationException">Si <paramref name="anotar"/> devuelve una orden de otra
    /// saga, o una segunda orden para una saga que ya tiene la suya.</exception>
    public async Task<PersistedEventOrder?> AnotarPendienteAsync(
        string sagaId, Func<PersistedEventOrder?, PersistedEventOrder?> anotar, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sagaId);
        ArgumentNullException.ThrowIfNull(anotar);

        var cerrojo = CerrojoDe(sagaId);
        await cerrojo.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var actual = DeLaSaga(await LoadAllAsync(cancellationToken).ConfigureAwait(false), sagaId);
            if (actual is { Status: EventOrderStatus.Confirmed }) return null;

            if (anotar(actual) is not { } nueva) return null;
            if (!string.Equals(nueva.PaymentSessionId, sagaId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Se quiso anotar bajo la saga {sagaId} una orden de {nueva.PaymentSessionId}.");
            }
            if (actual is not null && !string.Equals(nueva.OrderRef, actual.OrderRef, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"La saga {sagaId} ya tiene su orden: no se anota una segunda.");
            }

            var pendiente = nueva with { Status = EventOrderStatus.Pending };
            await SaveAsync(pendiente, cancellationToken).ConfigureAwait(false);
            return pendiente;
        }
        finally
        {
            cerrojo.Release();
        }
    }

    /// <summary>
    /// Confirma LA orden de una saga, bajo su cerrojo: si ya hay una confirmada la devuelve tal cual; si no,
    /// <paramref name="confirmar"/> recibe la pendiente que haya (o nula) y devuelve la que se confirma.
    /// </summary>
    /// <remarks>
    /// <para><b>Una saga emite sus entradas una vez</b>, y la regla vive acá y no en cada camino que confirma:
    /// la ruta vieja, el artefacto de la puerta y la reconciliación de «mis entradas» pasan por este método,
    /// así que ninguno se la puede saltar. Quien llama compara el <c>OrderRef</c> devuelto con el suyo: si
    /// es otro, su saga ya se confirmó con otra orden y la suya no emite nada.</para>
    ///
    /// <para>Lo confirmado se devuelve SIN reescribir: una transferencia o un check-in hechos entre medias
    /// no se pisan con una copia vieja.</para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">Si <paramref name="confirmar"/> devuelve una orden de otra saga.</exception>
    public async Task<PersistedEventOrder> ConfirmarAsync(
        string sagaId, Func<PersistedEventOrder?, PersistedEventOrder> confirmar, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sagaId);
        ArgumentNullException.ThrowIfNull(confirmar);

        var cerrojo = CerrojoDe(sagaId);
        await cerrojo.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var actual = DeLaSaga(await LoadAllAsync(cancellationToken).ConfigureAwait(false), sagaId);
            if (actual is { Status: EventOrderStatus.Confirmed }) return actual;

            var orden = confirmar(actual);
            if (!string.Equals(orden.PaymentSessionId, sagaId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Se quiso confirmar bajo la saga {sagaId} una orden de {orden.PaymentSessionId}.");
            }

            var confirmada = orden with { Status = EventOrderStatus.Confirmed };
            await SaveAsync(confirmada, cancellationToken).ConfigureAwait(false);
            return confirmada;
        }
        finally
        {
            cerrojo.Release();
        }
    }

    /// <summary>Todas las compras. Un fichero ilegible se salta, no tumba la lista.</summary>
    public async Task<List<PersistedEventOrder>> LoadAllAsync(CancellationToken cancellationToken = default)
    {
        var raws = await _store.ListAsync(ResourceType, cancellationToken);
        var orders = new List<PersistedEventOrder>(raws.Count);
        foreach (var json in raws)
        {
            if (string.IsNullOrWhiteSpace(json)) continue;
            PersistedEventOrder? order;
            try { order = JsonSerializer.Deserialize<PersistedEventOrder>(json, Json); }
            catch (JsonException) { continue; }
            if (order is not null) orders.Add(order);
        }
        return orders;
    }

    /// <summary>La compra proyectada a su resultado: estado + las entradas emitidas.</summary>
    public EventConfirmationResult ConfirmationOf(PersistedEventOrder order)
    {
        ArgumentNullException.ThrowIfNull(order);
        return new EventConfirmationResult(
            order.Status.ToString(),
            order.Units.Select(u => Issue(order.EventId, u)).ToList());
    }

    /// <summary>Una unidad guardada, proyectada a la entrada que ve su portador.</summary>
    /// <remarks>
    /// Es toda la frontera con el emisor: acá se sabe de compras, allá solo de entradas. Un
    /// camino de compra que aparte el aforo en otro sitio construye sus propias unidades y
    /// obtiene el MISMO formato de QR.
    /// </remarks>
    public EventTicket Issue(string eventId, PersistedEventUnit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        return _issuer.Issue(new EventTicketFacts(
            EventId: eventId,
            SeatRef: unit.ReservationId,
            HolderName: unit.HolderName,
            HolderEmail: unit.HolderEmail,
            Tier: unit.TierCode,
            Seat: unit.Seat,
            QrVersion: unit.QrVersion,
            CheckedIn: unit.CheckedIn));
    }

    // ── Cara de asistente ───────────────────────────────────────────────────

    /// <summary>«Mis entradas»: las del portador ACTUAL, compradas o recibidas.</summary>
    public async Task<IReadOnlyList<EventTicket>> TicketsOfAsync(
        string holderEmail, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(holderEmail))
        {
            return Array.Empty<EventTicket>();
        }

        var email = holderEmail.Trim();
        var all = await LoadAllAsync(cancellationToken);
        return all
            .Where(o => o.Status == EventOrderStatus.Confirmed)
            .OrderBy(o => o.CreatedAt)
            .SelectMany(o => o.Units.Select(u => (o.EventId, Unit: u)))
            .Where(x => string.Equals(x.Unit.HolderEmail, email, StringComparison.OrdinalIgnoreCase))
            .Select(x => Issue(x.EventId, x.Unit))
            .ToList();
    }

    /// <summary>
    /// Transfiere la entrada: reasigna el portador, ROTA el QR (el viejo muere) y lo audita.
    /// </summary>
    /// <remarks>
    /// Idempotente: transferir al portador actual no rota ni re-audita. Una entrada ya usada no
    /// se transfiere — es el hueco por el que se colaría revender lo que ya entró.
    /// </remarks>
    public async Task<EventTicketTransferResult> TransferAsync(
        string ticketId, string toEmail, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ticketId))
        {
            throw new ArgumentException("El ticket es obligatorio.", nameof(ticketId));
        }
        if (string.IsNullOrWhiteSpace(toEmail) || !toEmail.Contains('@', StringComparison.Ordinal))
        {
            throw new ArgumentException("El email de destino es inválido.", nameof(toEmail));
        }

        var id = ticketId.Trim();
        var newEmail = toEmail.Trim();

        // Localizar la unidad confirmada correspondiente al ticket (read-modify-write sobre la
        // orden dueña; el resto de órdenes no se toca).
        var all = await LoadAllAsync(cancellationToken);
        foreach (var order in all.OrderBy(o => o.CreatedAt))
        {
            if (order.Status != EventOrderStatus.Confirmed)
            {
                continue;
            }
            for (var i = 0; i < order.Units.Count; i++)
            {
                var unit = order.Units[i];
                if (!string.Equals(EventTicketIssuer.TicketIdOf(unit.ReservationId), id, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Ya usado/cancelado → no transferible.
                if (unit.CheckedIn)
                {
                    throw new ArgumentException("El ticket ya fue usado y no puede transferirse.", nameof(ticketId));
                }

                // Idempotente: transferir al portador actual no rota ni re-audita.
                if (string.Equals(unit.HolderEmail, newEmail, StringComparison.OrdinalIgnoreCase))
                {
                    var same = Issue(order.EventId, unit);
                    return new EventTicketTransferResult(same, same.Qr);
                }

                // Reasignar holder + rotar el QR (bump de versión ⇒ QR viejo inválido).
                var updatedUnit = unit with
                {
                    HolderEmail = newEmail,
                    HolderName = DeriveNameFromEmail(newEmail),
                    QrVersion = unit.QrVersion + 1,
                };
                var updatedUnits = order.Units.ToList();
                updatedUnits[i] = updatedUnit;
                await SaveAsync(order with { Units = updatedUnits }, cancellationToken);

                var ticket = Issue(order.EventId, updatedUnit);

                // Auditar la transferencia (append-only, ADR 0037).
                if (_audit is not null)
                {
                    // best-effort: el ticket YA fue transferido y persistido.
                    await BestEffort.RunAsync(() => _audit.WriteAsync(
                            new AuditEvent(
                                Id: Guid.NewGuid().ToString("N"),
                                OccurredAtUtc: _now().UtcDateTime,
                                ActorEmail: unit.HolderEmail,
                                ActorName: unit.HolderName,
                                Action: "event.ticket.transfer",
                                Resource: $"{order.EventId}/{id}",
                                Outcome: "success",
                                Detail: $"Ticket transferido de '{unit.HolderEmail}' a '{newEmail}'; QR rotado a v{updatedUnit.QrVersion}."),
                            cancellationToken), cancellationToken);
                }

                return new EventTicketTransferResult(ticket, ticket.Qr);
            }
        }

        throw new ArgumentException($"Ticket '{ticketId}' no encontrado.", nameof(ticketId));
    }

    // Deriva un nombre razonable del email del nuevo portador (la parte local, capitalizada).
    // El adapter real resolvería el nombre del Member/cuenta.
    private static string DeriveNameFromEmail(string email)
    {
        var local = email.Split('@', 2)[0].Replace('.', ' ').Replace('_', ' ').Trim();
        if (string.IsNullOrEmpty(local))
        {
            return email;
        }
        return char.ToUpperInvariant(local[0]) + local[1..];
    }

    // ── Cara de organizador ─────────────────────────────────────────────────

    /// <summary>Los asistentes confirmados de un evento, con su estado de check-in.</summary>
    public async Task<IReadOnlyList<EventAttendee>> ConfirmedAttendeesAsync(
        string eventId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(eventId))
        {
            return Array.Empty<EventAttendee>();
        }

        var id = eventId.Trim();
        var all = await LoadAllAsync(cancellationToken);
        return all
            .Where(o => o.Status == EventOrderStatus.Confirmed
                && string.Equals(o.EventId, id, StringComparison.OrdinalIgnoreCase))
            .OrderBy(o => o.CreatedAt)
            .SelectMany(o => o.Units)
            .Select(u => new EventAttendee(
                TicketId: EventTicketIssuer.TicketIdOf(u.ReservationId),
                Name: u.HolderName,
                Email: u.HolderEmail,
                Tier: u.TierCode,
                Seat: u.Seat,
                CheckedIn: u.CheckedIn))
            .ToList();
    }

    /// <summary>
    /// La puerta: marca la entrada como usada. <c>valid</c> el primer escaneo bueno,
    /// <c>already-used</c> si ya había entrado, <c>invalid</c> en todo lo demás.
    /// </summary>
    /// <remarks>
    /// <para><b>Se admite ÚNICAMENTE un token firmado</b> (T9/ADR 0110). Antes esto comparaba el
    /// input contra el <c>ticketId</c> y no miraba el QR: escanear devolvía <c>invalid</c> y lo
    /// único que funcionaba era teclear el id… que la UI imprime bajo el propio código. O sea,
    /// una foto de la entrada ajena servía para entrar en su lugar.</para>
    ///
    /// <para>Se comprueba además que la <c>QrVersion</c> del token sea la vigente: al transferir
    /// la entrada el QR rota, y el del dueño anterior debe morir (anti-reventa).</para>
    ///
    /// <para>Cuando el token no verifica se devuelve <c>invalid</c> <b>sin</b> datos: no hay
    /// entrada de la que hablar, y rellenar el evento con lo que el escáner afirmó sería dar por
    /// cierto lo que justamente no se pudo comprobar.</para>
    /// </remarks>
    public async Task<EventCheckInResult> CheckInAsync(string? rawToken, CancellationToken cancellationToken = default)
    {
        var token = _signer?.Verify(rawToken);
        if (token is null)
        {
            // Sin firma válida no hay entrada. Incluye el id suelto y el QR de otro evento.
            return new EventCheckInResult("invalid");
        }

        var all = await LoadAllAsync(cancellationToken);
        foreach (var order in all.OrderBy(o => o.CreatedAt))
        {
            if (order.Status != EventOrderStatus.Confirmed)
            {
                continue;
            }
            for (var i = 0; i < order.Units.Count; i++)
            {
                var unit = order.Units[i];
                if (!string.Equals(EventTicketIssuer.TicketIdOf(unit.ReservationId), token.TicketId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (unit.QrVersion != token.QrVersion)
                {
                    // QR de una versión anterior: la entrada se transfirió y esta copia ya no
                    // vale. Es el caso de la reventa del mismo QR.
                    return new EventCheckInResult("invalid");
                }

                var found = new EventCheckInResult(
                    Status: unit.CheckedIn ? "already-used" : "valid",
                    EventId: order.EventId,
                    TicketId: token.TicketId,
                    AttendeeName: unit.AttendeeName);

                if (unit.CheckedIn)
                {
                    return found;
                }

                var updatedUnits = order.Units.ToList();
                updatedUnits[i] = unit with { CheckedIn = true };
                await SaveAsync(order with { Units = updatedUnits }, cancellationToken);
                return found;
            }
        }
        return new EventCheckInResult("invalid");
    }
}

/// <summary>Estado de una compra de tickets. Serializado como número en el store.</summary>
public enum EventOrderStatus { Pending, Confirmed }

/// <summary>
/// La forma SERIALIZADA de una unidad de ticket. Guarda MÁS que el <see cref="EventTicket"/>
/// público: <see cref="ReservationId"/> es necesario para que la confirmación sobreviva un
/// reinicio, y <see cref="QrVersion"/>/<see cref="CheckedIn"/> son el estado anti-reventa y
/// anti-doble-entrada.
/// </summary>
/// <remarks>
/// <b><see cref="ReservationId"/> es el identificador de lo que se apartó para esta entrada</b>,
/// sea lo que sea que lo apartó: la reserva del motor en proceso o el apartado de aforo de un
/// orquestador. El nombre del campo se conserva porque es el que ya está escrito en disco —
/// renombrarlo no compraría nada y obligaría a migrar lo emitido.
/// </remarks>
public sealed record PersistedEventUnit(
    string TierCode,
    string TierName,
    string? Seat,
    decimal Price,
    string Currency,
    string AttendeeName,
    string AttendeeEmail,
    string? AttendeeDocument,
    string ReservationId)
{
    public bool CheckedIn { get; init; }

    /// <summary>Email del portador ACTUAL del ticket (cambia al transferir).</summary>
    public string HolderEmail { get; init; } = AttendeeEmail;

    /// <summary>Nombre del portador actual (cambia al transferir).</summary>
    public string HolderName { get; init; } = AttendeeName;

    /// <summary>
    /// Versión del QR — arranca en 0 y se incrementa en cada transferencia (SafeTix-like: el QR
    /// es determinista por ticket+versión, así que bumpearla INVALIDA el QR viejo).
    /// </summary>
    public int QrVersion { get; init; }
}

/// <summary>
/// La forma SERIALIZADA de una compra de tickets.
/// </summary>
/// <remarks>
/// <b><see cref="PaymentSessionId"/> es con qué se cobró.</b> En el motor en proceso, la sesión
/// del PSP —necesaria para que la confirmación sobreviva un reinicio (T1/ADR 0105)—; en el
/// camino del orquestador, el identificador de la saga, que es lo que hay que llamar para
/// confirmar.
/// </remarks>
public sealed record PersistedEventOrder(
    string OrderRef,
    string EventId,
    string PaymentSessionId,
    decimal Total,
    string Currency,
    IReadOnlyList<PersistedEventUnit> Units,
    DateTimeOffset CreatedAt)
{
    public EventOrderStatus Status { get; init; } = EventOrderStatus.Pending;

    /// <summary>
    /// Quién compró, en el vocabulario del orquestador: el <c>Kind</c> y el <c>Id</c> del sujeto con
    /// que se abrió la saga. Nulo en las del motor en proceso y en las anteriores a la ADR 0140 F3.
    /// </summary>
    /// <remarks>
    /// Desde la F3 el orquestador comprueba que quien confirma sea quien compró, y confirmar sólo
    /// recibe la orden: el sujeto tiene que estar guardado acá. Opcional y fuera de la lista
    /// posicional a propósito —se pone con un inicializador o con <c>with</c>, nunca por posición—,
    /// así lo ya escrito en disco se sigue leyendo.
    /// </remarks>
    public string? BuyerKind { get; init; }

    /// <inheritdoc cref="BuyerKind"/>
    public string? BuyerId { get; init; }

    /// <summary>
    /// A quién se le confirma la compra: quien la pagó, que no tiene por qué ir al evento (#107).
    /// Nulo en las anotadas antes de guardarlo.
    /// </summary>
    /// <remarks>
    /// <para><b>Existe porque el aviso se lo mandaba al primer asistente.</b> El checkout ya abría la
    /// sesión de pago a nombre del comprador, pero la orden no lo guardaba, así que al confirmar
    /// no quedaba de dónde sacarlo y <see cref="EventPurchaseNotification"/> tomaba la primera
    /// unidad: quien compra cuatro entradas para su familia y se queda en casa no recibía su
    /// propia confirmación. <see cref="BuyerId"/> no sirve para esto: en el camino del orquestador
    /// es un seudónimo, y del seudónimo no se vuelve al correo.</para>
    ///
    /// <para>Opcional y fuera de la lista posicional por la misma razón que
    /// <see cref="BuyerKind"/>: lo ya escrito en disco se sigue leyendo, y una orden sin comprador
    /// guardado avisa al primer asistente, que es lo que hacía antes.</para>
    /// </remarks>
    public string? BuyerName { get; init; }

    /// <inheritdoc cref="BuyerName"/>
    public string? BuyerEmail { get; init; }
}
