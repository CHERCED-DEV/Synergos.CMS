using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// El <see cref="IEventTicketingService"/> que compra de verdad — contra
/// <c>Synergos.Bff.Eventos</c> (HU #35).
/// </summary>
/// <remarks>
/// <para><b>Solo la mitad de la compra viaja.</b> El orquestador mueve aforo y plata; el
/// artefacto —la entrada, su QR, su portador, su check-in— se queda de este lado, en
/// <see cref="EventTicketLedger"/>, porque el firmante vive acá. Así que este cliente hace dos
/// cosas: llamar al BFF y <b>anotar de su lado lo que el BFF no lleva</b>.</para>
///
/// <para><b>Y lo que no lleva es la lista de asistentes, a propósito.</b> La saga sabe que se
/// apartaron dos butacas de la localidad general, no cómo se llama quien va a sentarse ni con qué
/// documento entra. Eso es correcto —un orquestador que acumule nombres y correos es un incidente
/// de privacidad esperando— y tiene una consecuencia práctica: <b>el CMS tiene que recordar
/// <c>sagaId → asistentes</c></b>. Se anota al comprar, antes de que haya nada que confirmar.</para>
///
/// <para>Lo único que sí viaja de quien compra es su <b>seudónimo</b>: el orquestador necesita un
/// comprador estable para poder decir de quién es cada compra, y no necesita saber quién es. Ver
/// <see cref="BuyerId"/> — mandar el correo en crudo lo dejaba escrito en el disco de otro
/// servicio, y lo destapó una verificación con procesos reales.</para>
///
/// <para><b>Contra el ORQUESTADOR, no contra las capacidades</b>, al revés que la visita al
/// inmueble (#33a). Acá sí hay algo que deshacer: si el cobro falla hay que soltar el aforo, y si
/// el consumo falla después de capturar hay que devolver la plata. Llamando a
/// <c>Api.Inventory</c> y <c>Api.Payments</c> por separado el CMS estaría reimplementando la
/// máquina de sagas — y peor, porque <b>no tiene dónde anotar una compensación pendiente</b>.
/// Hay gate: <c>EventosWiringTests</c>.</para>
///
/// <para><b>El peor resultado posible es un cobro sin entradas</b>, así que un timeout NO se
/// resuelve mostrando un error y ya. La llave de idempotencia se deriva del comprador y de lo
/// que compra ANTES de la primera llamada, y es el identificador de la saga: si la petición se
/// pierde en el aire, se pregunta si la compra llegó a existir en vez de crear una segunda.</para>
///
/// <para><b>Con el orquestador apagado, el vertical sigue sirviendo.</b> «Mis entradas»,
/// transferir y la puerta no lo tocan —salen del registro—; solo comprar y confirmar fallan, con
/// el motivo puesto. Un BFF caído no puede dejar a nadie fuera de un concierto que ya pagó.</para>
/// </remarks>
public sealed class HttpEventTicketingService : IEventTicketingService
{
    /// <summary>Cabecera de la llave compartida. La misma que exige toda capacidad.</summary>
    public const string ApiKeyHeader = "X-Synergos-Key";

    /// <summary>Cliente nombrado que registra el composer.</summary>
    public const string ClientName = "synergos-bff-eventos";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IHttpClientFactory _clients;
    private readonly IOptionsMonitor<EventosSettings> _settings;
    private readonly EventTicketLedger _ledger;
    private readonly ITransactionalNotifier? _notifier;
    private readonly ILogger<HttpEventTicketingService> _log;
    private readonly Func<DateTimeOffset> _now;

    public HttpEventTicketingService(
        IHttpClientFactory clients,
        IOptionsMonitor<EventosSettings> settings,
        EventTicketLedger ledger,
        ILogger<HttpEventTicketingService> log,
        ITransactionalNotifier? notifier = null,
        Func<DateTimeOffset>? now = null)
    {
        _clients = clients;
        _settings = settings;
        _ledger = ledger;
        _log = log;
        _notifier = notifier;
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    // ── Comprar ─────────────────────────────────────────────────────────────

    public async Task<EventCheckoutResult> CheckoutAsync(
        string eventId,
        IReadOnlyList<EventCheckoutItem> items,
        IReadOnlyList<EventAttendeeInfo> attendees,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(eventId)) throw new ArgumentException("El evento es obligatorio.", nameof(eventId));
        if (items is null || items.Count == 0) throw new ArgumentException("El carrito requiere al menos un ticket.", nameof(items));
        if (attendees is null || attendees.Count == 0) throw new ArgumentException("Se requiere al menos un asistente.", nameof(attendees));

        // Las líneas se normalizan ACÁ y en el mismo orden en que llegan, porque de ese orden
        // depende con qué asistente se empareja cada butaca cuando el orquestador responda.
        var lineas = items.Select(Normalizar).ToList();
        var totalUnidades = lineas.Sum(l => l.Quantity);
        if (totalUnidades != attendees.Count)
        {
            throw new ArgumentException(
                $"El número de asistentes ({attendees.Count}) debe igualar el número de tickets ({totalUnidades}).",
                nameof(attendees));
        }

        var comprador = attendees[0];
        if (string.IsNullOrWhiteSpace(comprador.Name) || string.IsNullOrWhiteSpace(comprador.Email))
        {
            throw new ArgumentException("El nombre y el email del comprador son obligatorios.", nameof(attendees));
        }

        var s = _settings.CurrentValue;
        var buyerId = BuyerId(comprador);

        // La llave ANTES de la primera llamada, y derivada de lo que se compra — no del reloj ni
        // de un Guid nuevo. Con un identificador fresco por intento, un reintento tras un timeout
        // crearía una SEGUNDA compra sobre las mismas butacas.
        var key = IdempotencyKeyFor(eventId, buyerId, lineas);

        var compra = await ComprarAsync(eventId, buyerId, s.BuyerKind, lineas, key, cancellationToken)
            .ConfigureAwait(false);

        // Y acá se anota lo que la saga no lleva. Si esto no ocurriera, la compra existiría del
        // lado del orquestador y no habría de dónde emitir ni a quién nombrar en la entrada.
        var orden = new PersistedEventOrder(
            OrderRef: compra.Id,
            EventId: eventId.Trim(),
            // El identificador de la saga ES lo que hay que llamar para confirmar. No hay sesión
            // de PSP a la que redirigir: la autorización ocurre servidor adentro.
            PaymentSessionId: compra.Id,
            Total: compra.Total.Amount,
            Currency: compra.Total.Currency,
            Units: Emparejar(compra, attendees),
            CreatedAt: _now());

        await _ledger.SaveAsync(orden, cancellationToken).ConfigureAwait(false);

        return new EventCheckoutResult(compra.Id, compra.Id, compra.Total.Amount, compra.Total.Currency);
    }

    private async Task<PurchaseDto> ComprarAsync(
        string eventId, string buyerId, string buyerKind,
        IReadOnlyList<EventCheckoutItem> lineas, string key, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "v1/ticket-purchases")
        {
            Content = JsonContent.Create(new
            {
                eventId = eventId.Trim(),
                buyerKind,
                buyerId,
                lines = lineas.Select(l => new { tier = l.Tier, seat = l.Seat, quantity = l.Quantity }),
            }),
        };
        req.Headers.Add("Idempotency-Key", key);

        try
        {
            return await EnviarAsync<PurchaseDto>(req, "comprar las entradas", ct).ConfigureAwait(false);
        }
        catch (InvalidOperationException) when (!ct.IsCancellationRequested)
        {
            // Un timeout no dice «no se cobró»: dice «no sé». Y no saberlo es justo lo que no
            // podemos permitirnos, así que se PREGUNTA. La llave es el identificador de la saga.
            _log.LogWarning("La compra de entradas {Key} no respondió; se consulta si llegó a existir.", key);

            var existente = await LeerCompraAsync(key, ct).ConfigureAwait(false);
            if (existente is not null)
            {
                _log.LogWarning("La compra {Key} SÍ existía: se sigue con ella en vez de crear otra.", key);
                return existente;
            }
            throw;
        }
    }

    /// <summary>
    /// Empareja lo que el orquestador apartó con los asistentes que solo conoce el CMS.
    /// </summary>
    /// <remarks>
    /// <para><b>Por orden, y el orden está garantizado</b>: el orquestador aparta un cupo por
    /// línea y las devuelve en el mismo orden en que se mandaron. Una línea de tres entradas es
    /// UN apartado de cantidad tres, así que se expande antes de emparejar — exactamente como
    /// hace el motor en proceso.</para>
    ///
    /// <para><b>El identificador de lo apartado se DERIVA, no se pide.</b> El orquestador no
    /// expone los identificadores internos de <c>Api.Inventory</c> y hace bien: sacarlos solo
    /// invita a que alguien los cablee río arriba. Sirve cualquier cosa estable y única dentro de
    /// la compra, así que se usa la saga más el ordinal — y como la llave de idempotencia es
    /// determinista, un reintento reproduce los mismos identificadores y por tanto las mismas
    /// entradas.</para>
    /// </remarks>
    internal static List<PersistedEventUnit> Emparejar(PurchaseDto compra, IReadOnlyList<EventAttendeeInfo> attendees)
    {
        var unidades = new List<PersistedEventUnit>(attendees.Count);
        var n = 0;

        foreach (var apartado in compra.Held ?? Array.Empty<HeldDto>())
        {
            for (var i = 0; i < Math.Max(1, apartado.Quantity); i++)
            {
                if (n >= attendees.Count)
                {
                    // El orquestador apartó más de lo que se pidió. No se inventa un asistente:
                    // una entrada sin portador no se puede emitir ni escanear.
                    break;
                }
                var quien = attendees[n];
                unidades.Add(new PersistedEventUnit(
                    TierCode: apartado.Tier,
                    TierName: apartado.Tier,
                    Seat: apartado.Seat,
                    // El desglose por butaca no vuelve del orquestador —su respuesta lleva el
                    // total— y repartirlo a ojo sería inventar. Cero es honesto; el total de la
                    // compra, que es lo que se cobró, sí está y va en la orden.
                    Price: 0m,
                    Currency: compra.Total.Currency,
                    AttendeeName: (quien.Name ?? string.Empty).Trim(),
                    AttendeeEmail: (quien.Email ?? string.Empty).Trim(),
                    AttendeeDocument: quien.DocumentId?.Trim(),
                    ReservationId: SeatRef(compra.Id, n)));
                n++;
            }
        }

        return unidades;
    }

    /// <summary>El identificador de lo apartado, determinista dentro de la compra.</summary>
    /// <remarks>
    /// <b>Sin guiones, y lo descubrió un test</b>: el payload del token es
    /// <c>SYN-TKT-{evento}-{entrada}-v{n}</c> y al deshacerlo se corta por el último guion, así
    /// que un id de entrada con guiones verifica bien y devuelve OTRA entrada — QR firmado,
    /// puerta cerrada, cero errores en el log. El identificador de la saga sí los lleva
    /// (<c>evt-…</c>), así que se quitan acá. <c>EventTicketIssuer.TicketIdOf</c> lo exige de
    /// todos modos: esto es cumplir el contrato, no esquivarlo.
    /// </remarks>
    internal static string SeatRef(string sagaId, int ordinal)
    {
        var limpio = new string(sagaId.Where(char.IsAsciiLetterOrDigit).ToArray());
        return $"{limpio}{ordinal:D2}";
    }

    // ── Confirmar ───────────────────────────────────────────────────────────

    public async Task<EventConfirmationResult> ConfirmAsync(
        string orderRef, CancellationToken cancellationToken = default)
    {
        var orden = await _ledger.LoadAsync(orderRef, cancellationToken).ConfigureAwait(false)
            ?? throw new ArgumentException("Orden no encontrada.", nameof(orderRef));

        // Idempotente, y sin salir a la red: si ya está confirmada, las entradas son las mismas.
        // Se re-emite el aviso porque el libro del dispatcher deduplica, y eso rescata el caso en
        // que la primera confirmación no llegó a notificar.
        if (orden.Status == EventOrderStatus.Confirmed)
        {
            await AvisarAsync(orden, cancellationToken).ConfigureAwait(false);
            return _ledger.ConfirmationOf(orden);
        }

        using var req = new HttpRequestMessage(
            HttpMethod.Post, $"v1/ticket-purchases/{Uri.EscapeDataString(orden.OrderRef)}/confirm");

        var compra = await EnviarAsync<PurchaseDto>(req, "confirmar la compra", cancellationToken)
            .ConfigureAwait(false);

        // Una saga que responde 200 pero no quedó Completed NO es una compra confirmada. Emitir
        // entradas ahí sería dar por buenas unas butacas que el orquestador está deshaciendo.
        if (!string.Equals(compra.Status, "Completed", StringComparison.Ordinal))
        {
            _log.LogWarning("La compra {Id} quedó en {Estado}: {Motivo}",
                orden.OrderRef, compra.Status ?? "-", compra.LastError ?? "sin detalle");
            throw new InvalidOperationException(
                compra.LastError ?? "No pudimos confirmar tus entradas. Si se te cobró, se devolverá.");
        }

        var confirmada = orden with { Status = EventOrderStatus.Confirmed };
        await _ledger.SaveAsync(confirmada, cancellationToken).ConfigureAwait(false);

        // Best-effort: un correo caído JAMÁS puede tumbar una compra ya pagada y persistida.
        await AvisarAsync(confirmada, cancellationToken).ConfigureAwait(false);

        return _ledger.ConfirmationOf(confirmada);
    }

    private Task AvisarAsync(PersistedEventOrder orden, CancellationToken ct)
        => EventPurchaseNotification.EmitAsync(_notifier, orden, _now(), ct);

    // ── Ciclo de vida del artefacto: no pasa por el orquestador ──────────────
    // Ni «mis entradas» ni transferir tocan la red, y ésa es la mitad del punto: quien ya compró
    // no se queda sin su entrada porque el BFF esté caído.

    public Task<IReadOnlyList<EventTicket>> GetTicketsAsync(
        string holderEmail, CancellationToken cancellationToken = default)
        => _ledger.TicketsOfAsync(holderEmail, cancellationToken);

    public Task<EventTicketTransferResult> TransferTicketAsync(
        string ticketId, string toEmail, CancellationToken cancellationToken = default)
        => _ledger.TransferAsync(ticketId, toEmail, cancellationToken);

    // ── El cable ────────────────────────────────────────────────────────────

    /// <summary>Una escritura: o sale bien, o se traduce el motivo. Nunca «éxito» a secas.</summary>
    private async Task<T> EnviarAsync<T>(HttpRequestMessage req, string queHacia, CancellationToken ct)
    {
        var http = _clients.CreateClient(ClientName);

        HttpResponseMessage res;
        try
        {
            res = await http.SendAsync(req, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;   // el comprador cerró la pestaña; no es un fallo del servicio
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            // NO se traga la excepción. Un fallo de red que devolviera «compra exitosa» es el
            // peor defecto posible de este camino.
            _log.LogError(ex, "No se pudo {Que}: el orquestador de Eventos no respondió.", queHacia);
            throw new InvalidOperationException("No pudimos procesar tu compra. No se te cobró.", ex);
        }

        using (res)
        {
            if (res.IsSuccessStatusCode)
            {
                var cuerpo = await res.Content.ReadFromJsonAsync<T>(Json, ct).ConfigureAwait(false);
                return cuerpo ?? throw new InvalidOperationException($"No pudimos {queHacia}: la respuesta vino vacía.");
            }

            var problema = await LeerProblemaAsync(res, ct).ConfigureAwait(false);

            // SOLO 401, y la distinción importa. Es un defecto de DESPLIEGUE, no del visitante:
            // la llave compartida está mal o no está. Se grita en el log y afuera sale un error
            // genérico — quien compra no puede hacer nada con «401».
            //
            // El 403 NO entra acá: `SharedKeyAuth` responde 401 cuando la llave falla, nunca 403.
            // Un 403 del árbol de servicios es un rechazo de negocio, y tragarse su motivo
            // dejaría al comprador sin saber qué hacer.
            if (res.StatusCode == HttpStatusCode.Unauthorized)
            {
                _log.LogError(
                    "Eventos respondió 401 al {Que}: la llave compartida es inválida o falta. "
                    + "Revisar Synergos:Eventos:ApiKey.", queHacia);
                throw new InvalidOperationException("No pudimos procesar tu compra. No se te cobró.");
            }

            _log.LogWarning("Eventos rechazó {Que} con {Status} ({Code}): {Detalle}",
                queHacia, (int)res.StatusCode, problema.Code ?? "-", problema.Detail ?? "-");

            // El motivo del rechazo SÍ es del comprador: «esa butaca ya no está» es accionable y
            // «error» no lo es. Va como ArgumentException porque es lo que el controller traduce
            // a 400 con el mensaje visible.
            throw new ArgumentException(
                string.IsNullOrWhiteSpace(problema.Detail) ? $"No pudimos {queHacia}." : problema.Detail!);
        }
    }

    /// <summary>Una lectura: si no está o no responde, es null. Nunca revienta la página.</summary>
    private async Task<PurchaseDto?> LeerCompraAsync(string sagaId, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(
                HttpMethod.Get, $"v1/ticket-purchases/{Uri.EscapeDataString(sagaId)}");
            using var res = await _clients.CreateClient(ClientName).SendAsync(req, ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
            {
                if (res.StatusCode != HttpStatusCode.NotFound)
                {
                    _log.LogWarning("Eventos respondió {Status} al consultar la compra.", (int)res.StatusCode);
                }
                return null;
            }
            return await res.Content.ReadFromJsonAsync<PurchaseDto>(Json, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Eventos no respondió al consultar la compra {Saga}.", sagaId);
            return null;
        }
    }

    private static async Task<ProblemDto> LeerProblemaAsync(HttpResponseMessage res, CancellationToken ct)
    {
        try
        {
            return await res.Content.ReadFromJsonAsync<ProblemDto>(Json, ct).ConfigureAwait(false) ?? new ProblemDto();
        }
        catch (Exception ex) when (ex is JsonException or HttpRequestException or NotSupportedException)
        {
            return new ProblemDto();
        }
    }

    // ── Traducciones ────────────────────────────────────────────────────────

    /// <summary>Una línea, con la misma regla del motor en proceso: con butaca, una entrada.</summary>
    private static EventCheckoutItem Normalizar(EventCheckoutItem item)
    {
        if (item is null || string.IsNullOrWhiteSpace(item.Tier))
        {
            throw new ArgumentException("Cada línea requiere un tier.", nameof(item));
        }
        var conButaca = !string.IsNullOrWhiteSpace(item.Seat);
        var cantidad = conButaca ? 1 : item.Quantity;
        if (cantidad <= 0)
        {
            throw new ArgumentException("La cantidad de cada línea debe ser mayor a cero.", nameof(item));
        }
        return new EventCheckoutItem(item.Tier.Trim(), conButaca ? item.Seat!.Trim() : null, cantidad);
    }

    /// <summary>Quién compra, en el vocabulario del árbol de servicios.</summary>
    /// <remarks>
    /// <para><b>Un seudónimo del correo, no el correo</b> — y esto lo corrigió una verificación
    /// con procesos reales, no una revisión. Mandando el correo en crudo, la saga lo persiste en
    /// el disco del orquestador y el <c>buyerId</c> del listado de compras pasa a ser un dato
    /// personal en un servicio que no tiene ninguna razón para tenerlo.</para>
    ///
    /// <para><b>Y no se pierde nada</b>, que es lo que hace la decisión fácil: el orquestador solo
    /// necesita que el comprador sea estable y opaco, y el CMS conserva de su lado quién es cada
    /// quien. Mismo procedimiento que la visita al inmueble (#33a).</para>
    ///
    /// <para>Nunca el nombre: dos personas se llaman igual.</para>
    /// </remarks>
    internal static string BuyerId(EventAttendeeInfo comprador)
    {
        var correo = (comprador.Email ?? string.Empty).Trim().ToLowerInvariant();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(correo)))[..16].ToLowerInvariant();
    }

    /// <summary>
    /// La llave de idempotencia: <b>determinista sobre lo que se compra</b>, no sobre cuándo.
    /// </summary>
    /// <remarks>
    /// Es lo que hace que un reintento tras un timeout no cree una segunda compra. Entran el
    /// evento, el comprador y las líneas ORDENADAS —así reordenar el carrito en pantalla no
    /// cambia la llave— y sale un hash estable. Que dos compras idénticas del mismo comprador
    /// compartan llave es intencional: es indistinguible de un reintento, y ante la duda se
    /// prefiere no cobrar dos veces.
    ///
    /// <para><b>Y tiene un costo que hay que saber, verificado con procesos reales:</b> la llave
    /// no caduca, así que si una compra se DESHACE —el cobro falló y la saga compensó— el mismo
    /// comprador que vuelva a intentar exactamente lo mismo recibe la saga muerta y se queda
    /// encerrado. Lo comparte la tienda (#24), que deriva su llave igual, y arreglarlo bien es
    /// decidir a qué se ata la idempotencia: al carrito para siempre, o al intento. Está anotado
    /// como defecto aparte; no se toca acá porque cambiarlo sin decidir eso reintroduce el cobro
    /// doble, que es lo que esta llave existe para impedir.</para>
    /// </remarks>
    internal static string IdempotencyKeyFor(string eventId, string buyerId, IReadOnlyList<EventCheckoutItem> lineas)
    {
        var partes = lineas
            .Select(l => $"{l.Tier}/{l.Seat ?? "-"}x{l.Quantity}")
            .OrderBy(x => x, StringComparer.Ordinal);

        var semilla = $"{eventId.Trim()}|{buyerId}|{string.Join('|', partes)}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(semilla));
        return "evt-" + Convert.ToHexString(hash)[..32].ToLowerInvariant();
    }

    // Los DTO viven acá y NO en Synergos.CMS.Interfaces: son la forma del contrato HTTP con otro
    // servicio, no vocabulario del dominio del CMS.

    internal sealed record MoneyDto(decimal Amount, string Currency);

    internal sealed record HeldDto(string Tier, string? Seat, int Quantity);

    internal sealed record PurchaseDto(
        string Id, string? BuyerKind, string? BuyerId, string? EventId, string? Status,
        MoneyDto Total, IReadOnlyList<HeldDto>? Held, int PendingCompensations, string? LastError);

    private sealed record ProblemDto
    {
        public string? Title { get; init; }
        public string? Detail { get; init; }
        public string? Code { get; init; }
    }
}
