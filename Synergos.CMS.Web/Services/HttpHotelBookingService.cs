using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// El <see cref="IHotelBookingService"/> que reserva de verdad — contra
/// <c>Synergos.Bff.Viajes</c> (HU #36).
/// </summary>
/// <remarks>
/// <para><b>Contra el ORQUESTADOR, no contra las capacidades.</b> Apartar, cobrar y confirmar
/// pueden fallar a la mitad: si el cobro no sale hay que soltar el cupo, y si la confirmación
/// falla después de capturar hay que devolver la plata. Llamando a <c>Api.Booking</c> y
/// <c>Api.Payments</c> por separado, el CMS estaría reimplementando la máquina de sagas — y peor,
/// porque <b>no tiene dónde anotar una compensación pendiente</b>. Hay gate.</para>
///
/// <para><b>Los sustantivos de hotel se quedan de este lado.</b> <c>RoomTypeCode</c>,
/// <c>RatePlanCode</c>, <c>GuestName</c> y el precio que se le mostró al huésped no significan
/// nada en ninguna capacidad, así que el orquestador no los recibe: recibe un producto opaco y
/// una ventana. El CMS los anota y los une con lo que el orquestador devuelve. Es el mismo
/// reparto de la HU #35, por la misma razón.</para>
///
/// <para><b>El precio lo pone <c>Api.Pricing</c>, no el buscador.</b> El
/// <see cref="ReservationRequest.TotalPrice"/> que llega en la petición <b>no viaja</b>: si el
/// total lo pusiera el llamador, cualquiera reservaría la suite al precio de la estándar. Eso
/// tiene una consecuencia de despliegue que hay que saber: cada <c>tipo/tarifa</c> necesita su
/// precio cargado en <c>Api.Pricing</c> y su recurso en <c>Api.Booking</c>, o apartar se rechaza.
/// Está dicho en <c>.env.example</c>.</para>
///
/// <para><b>La penalidad de cancelación se calcula ACÁ y se manda ya calculada.</b> Depende de la
/// tarifa y de cuántos días falten, y eso es política comercial del hotel — no algo que un
/// orquestador que sirve a hoteles, vuelos y autos deba interpretar.</para>
///
/// <para><b>Con el orquestador apagado, el vertical sigue sirviendo.</b> Buscar y consultar una
/// reserva ya hecha no lo tocan; solo apartar, cobrar y cancelar fallan, con el motivo puesto.
/// </para>
/// </remarks>
public sealed class HttpHotelBookingService : IHotelBookingService
{
    /// <summary>Cabecera de la llave compartida. La misma que exige toda capacidad.</summary>
    public const string ApiKeyHeader = ViajesWire.ApiKeyHeader;

    /// <summary>Cliente nombrado que registra el composer.</summary>
    public const string ClientName = ViajesWire.ClientName;

    /// <summary>Familia de entidades en el store genérico (→ App_Data/syn-travel-stays/).</summary>
    public const string ResourceType = "travel-stays";

    private static readonly JsonSerializerOptions Disco = new() { WriteIndented = true };

    private readonly ViajesWire _wire;
    private readonly IOptionsMonitor<ViajesSettings> _settings;
    private readonly ICancellationPolicyEvaluator _cancellationPolicy;
    private readonly IJsonEntityStore _store;
    private readonly IAuditTrailWriter? _audit;
    private readonly ILogger<HttpHotelBookingService> _log;
    private readonly Func<DateTimeOffset> _now;

    public HttpHotelBookingService(
        IHttpClientFactory clients,
        IOptionsMonitor<ViajesSettings> settings,
        ICancellationPolicyEvaluator cancellationPolicy,
        IJsonEntityStore store,
        ILogger<HttpHotelBookingService> log,
        IAuditTrailWriter? audit = null,
        Func<DateTimeOffset>? now = null)
    {
        // El mensaje de caída es de este consumidor: quien está reservando una habitación
        // necesita saber que NO se le cobró, que es lo único accionable de un fallo de red.
        _wire = new ViajesWire(clients, log, "No pudimos procesar tu reserva. No se te cobró.");
        _settings = settings;
        _cancellationPolicy = cancellationPolicy;
        _store = store;
        _log = log;
        _audit = audit;
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    // ── Apartar ─────────────────────────────────────────────────────────────

    public async Task<Reservation> HoldAsync(
        ReservationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.CheckOut <= request.CheckIn)
        {
            throw new ArgumentException("CheckOut debe ser posterior a CheckIn.", nameof(request));
        }

        var s = _settings.CurrentValue;
        var producto = ProductRef(request);
        var viajero = TravellerId(request.GuestEmail);
        var llave = IdempotencyKeyFor(producto, viajero, request.CheckIn, request.CheckOut);

        using var req = new HttpRequestMessage(HttpMethod.Post, "v1/trips")
        {
            Content = JsonContent.Create(new
            {
                travellerKind = s.TravellerKind,
                travellerId = viajero,
                items = new[]
                {
                    new
                    {
                        productRef = producto,
                        productLabel = producto,
                        start = Entrada(request.CheckIn),
                        end = Salida(request.CheckOut),
                    },
                },
            }),
        };
        req.Headers.Add("Idempotency-Key", llave);

        var viaje = await EnviarAsync<TripDto>(req, "apartar la habitación", cancellationToken).ConfigureAwait(false);

        // Y acá se anota lo que el orquestador no lleva. Sin esto la reserva existiría allá y de
        // este lado no habría a quién nombrar ni qué mostrarle.
        var reserva = new Reservation(
            Id: viaje.Id,
            Status: ReservationStatus.Held,
            RoomTypeCode: request.RoomTypeCode.Trim(),
            RatePlanCode: request.RatePlanCode.Trim(),
            CheckIn: request.CheckIn,
            CheckOut: request.CheckOut,
            GuestName: request.GuestName.Trim(),
            GuestEmail: request.GuestEmail.Trim(),
            // El total que vale es el que cotizó Api.Pricing, no el que traía la petición.
            TotalPrice: viaje.Total.Amount,
            Currency: viaje.Total.Currency,
            PaymentSessionId: null,
            // El vencimiento del apartado lo lleva Api.Booking y el orquestador no lo expone —
            // hace bien, es un identificador interno más. Nulo es honesto: no lo sabemos.
            ExpiresAt: null,
            ProductType: TravelProductType.Hotel,
            ProductRef: producto,
            ProductLabel: producto);

        await GuardarAsync(reserva, cancellationToken).ConfigureAwait(false);
        return reserva;
    }

    // ── Cobrar ──────────────────────────────────────────────────────────────

    public async Task<HotelPaymentResult?> PayAsync(
        string reservationId, CancellationToken cancellationToken = default)
    {
        var reserva = await LeerAsync(reservationId, cancellationToken).ConfigureAwait(false);
        if (reserva is null) return null;

        if (reserva.Status == ReservationStatus.Confirmed)
        {
            return new HotelPaymentResult(reserva, PaymentStatus.Captured, reserva.PaymentSessionId,
                reserva.TotalPrice, null, HotelPaymentOutcome.AlreadyConfirmed);
        }

        if (reserva.Status is ReservationStatus.Cancelled or ReservationStatus.Expired)
        {
            return new HotelPaymentResult(reserva, PaymentStatus.Cancelled, null, 0m,
                reserva.Status == ReservationStatus.Cancelled
                    ? "La reserva está cancelada; no se puede cobrar."
                    : "El hold de la reserva venció; vuelve a apartar el cupo antes de pagar.",
                HotelPaymentOutcome.Conflict);
        }

        using var req = new HttpRequestMessage(
            HttpMethod.Post, $"v1/trips/{Uri.EscapeDataString(reserva.Id)}/confirm");

        TripDto viaje;
        try
        {
            viaje = await EnviarAsync<TripDto>(req, "cobrar la reserva", cancellationToken).ConfigureAwait(false);
        }
        catch (ViajesRejectedException ex)
        {
            // El apartado venció mientras el huésped pagaba, o el cobro no salió. Los dos son
            // rechazos del negocio y llevan su motivo — taparlos con «error» dejaría al huésped
            // sin saber qué hacer.
            var vencido = ex.Code is "booking.hold_expired" or "booking.hold_not_found";
            var estado = vencido ? reserva with { Status = ReservationStatus.Expired } : reserva;
            if (vencido)
            {
                await GuardarAsync(estado, cancellationToken).ConfigureAwait(false);
            }

            return new HotelPaymentResult(estado, PaymentStatus.Failed, null, 0m, ex.Message,
                vencido ? HotelPaymentOutcome.Conflict : HotelPaymentOutcome.NotCaptured);
        }

        // Una saga que responde 200 pero no quedó Completed NO es una reserva confirmada. Dar
        // por buena una habitación que el orquestador está soltando sería lo peor que puede
        // pasar acá: el huésped llega y no hay cuarto.
        if (!string.Equals(viaje.Status, "Completed", StringComparison.Ordinal))
        {
            _log.LogWarning("La reserva {Id} quedó en {Estado}: {Motivo}",
                reserva.Id, viaje.Status ?? "-", viaje.LastError ?? "sin detalle");

            return new HotelPaymentResult(reserva, PaymentStatus.Failed, null, 0m,
                viaje.LastError ?? "No pudimos confirmar tu reserva. Si se te cobró, se devolverá.",
                HotelPaymentOutcome.NotCaptured);
        }

        var confirmada = reserva with
        {
            Status = ReservationStatus.Confirmed,
            // El orquestador no expone el identificador del pago —es interno de Api.Payments—
            // así que se liga con el de la saga, que es lo que sí identifica esta transacción.
            PaymentSessionId = viaje.Id,
        };
        await GuardarAsync(confirmada, cancellationToken).ConfigureAwait(false);

        return new HotelPaymentResult(confirmada, PaymentStatus.Captured, viaje.Id,
            confirmada.TotalPrice, null, HotelPaymentOutcome.Confirmed);
    }

    // ── Cancelar ────────────────────────────────────────────────────────────

    public async Task<HotelCancellationResult?> CancelAsync(
        string reservationId, string? reason, CancellationToken cancellationToken = default)
    {
        var reserva = await LeerAsync(reservationId, cancellationToken).ConfigureAwait(false);
        if (reserva is null) return null;

        var hoy = DateOnly.FromDateTime(_now().UtcDateTime);
        var politica = _cancellationPolicy.Evaluate(reserva.RatePlanCode, reserva.CheckIn, hoy);

        // Cancelar dos veces NO devuelve dos veces. La guarda mira el ESTADO que este lado
        // guardó — el orquestador también es idempotente, pero no se delega en eso: la llave de
        // la devolución la pone él, y si un día cambiara, el doble cobro volvería por acá.
        if (reserva.Status == ReservationStatus.Cancelled)
        {
            return new HotelCancellationResult(
                reserva, politica.Refundable, politica.PenaltyAmount, politica.Description, null);
        }

        var penalidad = politica.Refundable ? politica.PenaltyAmount : reserva.TotalPrice;

        using var req = new HttpRequestMessage(
            HttpMethod.Post, $"v1/trips/{Uri.EscapeDataString(reserva.Id)}/cancel")
        {
            Content = JsonContent.Create(new
            {
                retain = new { amount = penalidad, currency = reserva.Currency },
            }),
        };

        var viaje = await EnviarAsync<TripDto>(req, "cancelar la reserva", cancellationToken).ConfigureAwait(false);

        var cancelada = reserva with { Status = ReservationStatus.Cancelled };
        await GuardarAsync(cancelada, cancellationToken).ConfigureAwait(false);

        // Lo que el orquestador no pudo deshacer sale en su `lastError`, y se propaga en vez de
        // callarse: una devolución colgada tiene que verse.
        var estadoDevolucion = viaje.LastError is { Length: > 0 } malo
            ? $"Pendiente: {malo}"
            : (politica.Refundable && reserva.PaymentSessionId is not null
                ? PaymentStatus.Refunded.ToString()
                : null);

        if (_audit is not null)
        {
            await BestEffortAudit(cancelada, reason, politica.PenaltyAmount, estadoDevolucion, cancellationToken)
                .ConfigureAwait(false);
        }

        return new HotelCancellationResult(
            cancelada, politica.Refundable, politica.PenaltyAmount, politica.Description, estadoDevolucion);
    }

    public Task<Reservation?> GetAsync(string reservationId, CancellationToken cancellationToken = default)
        => LeerAsync(reservationId, cancellationToken);

    // ── Lo que este lado recuerda ───────────────────────────────────────────

    private Task GuardarAsync(Reservation reserva, CancellationToken ct)
        => _store.WriteAsync(ResourceType, reserva.Id, JsonSerializer.Serialize(reserva, Disco), ct);

    private async Task<Reservation?> LeerAsync(string? id, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        var json = await _store.ReadAsync(ResourceType, id.Trim(), ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<Reservation>(json, Disco); }
        catch (JsonException) { return null; }   // archivo corrupto → como si no existiera
    }

    private async Task BestEffortAudit(
        Reservation cancelada, string? reason, decimal penalidad, string? devolucion, CancellationToken ct)
    {
        try
        {
            await _audit!.WriteAsync(
                new AuditEvent(
                    Id: Guid.NewGuid().ToString("N"),
                    OccurredAtUtc: _now().UtcDateTime,
                    ActorEmail: cancelada.GuestEmail,
                    ActorName: cancelada.GuestName,
                    Action: "booking.reservation.cancelled",
                    Resource: cancelada.Id,
                    Outcome: "success",
                    Detail: $"Cancelada por URL-credencial; motivo '{reason ?? "guest-requested"}'; "
                        + $"penalidad {penalidad} {cancelada.Currency}; reembolso {devolucion ?? "no aplica"}."),
                ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Reserva {Id}: cancelada pero no se pudo dejar el rastro.", cancelada.Id);
        }
    }

    // ── El cable ────────────────────────────────────────────────────────────
    //
    // Vive en ViajesWire desde que hay un SEGUNDO consumidor —el carrito multi-producto (#40)—
    // y no antes. Con uno solo estaba bien acá; copiarlo para el segundo habría dejado dos
    // manejos de error contra el mismo orquestador, divergiendo justo donde menos se nota.

    private Task<T> EnviarAsync<T>(HttpRequestMessage req, string queHacia, CancellationToken ct)
        => _wire.SendAsync<T>(req, queHacia, ct);

    // ── Traducciones ────────────────────────────────────────────────────────

    /// <summary>El producto tal como lo conoce <c>Api.Booking</c>: opaco y sin sustantivos.</summary>
    internal static string ProductRef(ReservationRequest r)
        => $"{r.RoomTypeCode.Trim()}/{r.RatePlanCode.Trim()}";

    /// <summary>
    /// La noche de hotel como ventana, de medianoche a medianoche UTC.
    /// </summary>
    /// <remarks>
    /// <b>No son las 15:00 y las 11:00 de verdad</b>, y da igual: lo que <c>Api.Booking</c> hace
    /// con la ventana es contar solapes contra la capacidad del tipo de habitación, y para eso
    /// dos estadías con las mismas noches tienen que solaparse — cosa que medianoche a medianoche
    /// cumple exactamente. Las horas reales son política de cada hotel y el CMS no las tiene;
    /// inventárselas por producto sería la clase de convención adivinada que costó una vuelta en
    /// la HU #25.
    /// </remarks>
    internal static DateTimeOffset Entrada(DateOnly checkIn)
        => new(checkIn.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

    /// <inheritdoc cref="Entrada"/>
    internal static DateTimeOffset Salida(DateOnly checkOut)
        => new(checkOut.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

    /// <inheritdoc cref="ViajesWire.TravellerId"/>
    internal static string TravellerId(string? guestEmail) => ViajesWire.TravellerId(guestEmail);

    /// <summary>
    /// La llave de idempotencia: <b>determinista sobre lo que se reserva</b>, no sobre cuándo.
    /// </summary>
    /// <remarks>
    /// Es lo que hace que un reintento tras un timeout no aparte una segunda habitación. Que dos
    /// peticiones idénticas del mismo huésped compartan llave es intencional: es indistinguible
    /// de un reintento, y ante la duda se prefiere no apartar dos veces. Comparte el defecto
    /// conocido de #38 — la llave no caduca, así que tras una reserva deshecha el mismo huésped
    /// no puede repetirla igual.
    /// </remarks>
    internal static string IdempotencyKeyFor(string producto, string viajero, DateOnly desde, DateOnly hasta)
    {
        var semilla = $"{producto}|{viajero}|{desde:yyyy-MM-dd}|{hasta:yyyy-MM-dd}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(semilla));
        return "stay-" + Convert.ToHexString(hash)[..32].ToLowerInvariant();
    }

    // Los DTO del viaje viven en ViajesWire y NO en Synergos.CMS.Interfaces: son la forma del
    // contrato HTTP con otro servicio, no vocabulario del dominio del CMS. Están allá desde que
    // los leen los dos consumidores — uno solo por lado dejaría al carrito redeclarando la misma
    // respuesta, y una de las dos copias se quedaría vieja al primer campo nuevo.
}
