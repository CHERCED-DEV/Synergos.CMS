using Microsoft.AspNetCore.Mvc;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Controllers;

/// <summary>
/// API JSON del MOTOR de reservas (vertical Hoteles). Orquesta los 4 seams
/// stub-first del motor (<see cref="IRoomAvailabilityProvider"/>,
/// <see cref="IReservationService"/>, <see cref="IPaymentProvider"/>,
/// <see cref="ICancellationPolicyEvaluator"/>) + el formateo es-CO de
/// <see cref="IPriceFormatter"/>, exponiendo el flujo del wizard:
/// search → hold → pay (createSession + capture + confirm) → cancel.
/// </summary>
/// <remarks>
/// API pública del booking (sin auth-gate): el huésped no necesita login para
/// buscar/apartar/pagar. El estado de la reserva vive en el motor
/// (<see cref="IReservationService"/>, hoy <c>StubReservationService</c> en
/// memoria); el id de reserva es la credencial para pay/cancel/get.
///
/// La capa Web SOLO orquesta y mapea a DTOs de respuesta JSON estables — toda
/// la lógica vive en los seams (Application, sin Umbraco — ADR 0002). Los seams
/// se cambian por adapters reales (PMS / channel-manager / Stripe-Wompi-PayU)
/// sin tocar este controller.
/// </remarks>
[ApiController]
[Route("api/booking")]
public sealed class BookingController : ControllerBase
{
    private readonly IRoomAvailabilityProvider _availability;

    /// <summary>El flujo transaccional. <b>Este borde ya NO orquesta el cobro</b> (HU #36).</summary>
    /// <remarks>
    /// Apartar, cobrar y confirmar vivían acá dentro: unas doscientas líneas que decidían en qué
    /// orden se abre la caja, con dos defectos ya corregidos que ningún test cubría porque no
    /// había dónde ponerlos. Lo que queda de este lado es lo que sí es del borde — validar la
    /// petición, formatear precios y elegir el código de estado.
    /// </remarks>
    private readonly IHotelBookingService _booking;
    private readonly ICancellationPolicyEvaluator _cancellationPolicy;
    private readonly IPriceFormatter _priceFormatter;
    private readonly ILogger<BookingController> _logger;

    public BookingController(
        IRoomAvailabilityProvider availability,
        IHotelBookingService booking,
        ICancellationPolicyEvaluator cancellationPolicy,
        IPriceFormatter priceFormatter,
        ILogger<BookingController> logger)
    {
        _availability = availability;
        _booking = booking;
        _cancellationPolicy = cancellationPolicy;
        _priceFormatter = priceFormatter;
        _logger = logger;
    }

    // ── 1. Search ──────────────────────────────────────────────────
    // Resuelve "qué hay disponible" para el rango + ocupación, y enriquece
    // cada oferta con el precio formateado es-CO + un texto corto de la
    // política de cancelación (evaluada al check-in, cancelando "hoy").
    [HttpPost("search")]
    public async Task<IActionResult> Search(
        [FromBody] SearchRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new { error = "Cuerpo de la solicitud requerido." });
        }
        if (request.CheckOut <= request.CheckIn)
        {
            return BadRequest(new { error = "CheckOut debe ser posterior a CheckIn." });
        }

        var rooms = MapRooms(request.Rooms);
        if (rooms.Count == 0)
        {
            return BadRequest(new { error = "Debe solicitar al menos una habitación." });
        }

        IReadOnlyList<RoomOffer> offers;
        try
        {
            offers = await _availability.SearchAsync(
                new AvailabilityQuery(request.CheckIn, request.CheckOut, rooms),
                cancellationToken);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var results = offers.Select(offer =>
        {
            var policy = _cancellationPolicy.Evaluate(offer.RatePlanCode, request.CheckIn, today);
            return new RoomOfferResponse(
                // La oferta que la UI lee por `offerId` (ADR 0083). Es lo que identifica una
                // oferta en este dominio —un tipo de habitación sobre un plan tarifario— y no
                // un id nuevo: se compone de los dos códigos y se vuelve a partir en `hold`.
                OfferId: ComposeOfferId(offer.RoomTypeCode, offer.RatePlanCode),
                RoomTypeCode: offer.RoomTypeCode,
                RoomTypeName: offer.RoomTypeName,
                RatePlanCode: offer.RatePlanCode,
                BoardBasis: offer.BoardBasis,
                // `board` es la clave que lee la UI; `boardBasis` se conserva.
                Board: offer.BoardBasis,
                TotalPrice: offer.TotalPrice,
                TotalPriceFormatted: _priceFormatter.Format(offer.TotalPrice, offer.Currency),
                Currency: offer.Currency,
                Refundable: offer.Refundable,
                CancellationPolicy: policy.Description,
                MinStayNights: offer.MinStayNights,
                RoomsLeft: offer.RoomsLeft);
        }).ToList();

        return Ok(new SearchResponse(
            CheckIn: request.CheckIn,
            CheckOut: request.CheckOut,
            Offers: results));
    }

    // ── 2. Hold ────────────────────────────────────────────────────
    // Aparta la oferta elegida (estado Held) mientras el huésped paga.
    [HttpPost("hold")]
    public async Task<IActionResult> Hold(
        [FromBody] HoldRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new { error = "Cuerpo de la solicitud requerido." });
        }
        if (request.CheckOut <= request.CheckIn)
        {
            return BadRequest(new { error = "CheckOut debe ser posterior a CheckIn." });
        }
        // La oferta llega en DOS formas y las dos son válidas. La plana —roomTypeCode +
        // ratePlanCode + totalPrice sueltos— es la de este borde desde el primer día. La
        // anidada —`offerId` + el objeto `offer` + `guest`— es la que manda el asistente
        // Angular, que reenvía la oferta tal como la recibió de `search` (ADR 0083: la UI es
        // la fuente de verdad). Sin esto el cuerpo del asistente NO liga NINGÚN campo
        // requerido y `hold` contesta 400 siempre, que es lo que pasaba.
        var (roomTypeCode, ratePlanCode) = ResolveOffer(request);
        var guestName = FirstNonBlank(request.GuestName, request.Guest?.Name);
        var guestEmail = FirstNonBlank(request.GuestEmail, request.Guest?.Email);
        var totalPrice = request.TotalPrice > 0m ? request.TotalPrice : (request.Offer?.TotalPrice ?? 0m);

        if (string.IsNullOrWhiteSpace(roomTypeCode) || string.IsNullOrWhiteSpace(ratePlanCode))
        {
            return BadRequest(new { error = "RoomTypeCode y RatePlanCode son requeridos." });
        }
        if (string.IsNullOrWhiteSpace(guestName) || string.IsNullOrWhiteSpace(guestEmail))
        {
            return BadRequest(new { error = "GuestName y GuestEmail son requeridos." });
        }
        if (totalPrice <= 0m)
        {
            return BadRequest(new { error = "TotalPrice debe ser mayor que cero." });
        }

        var rooms = MapRooms(request.Rooms);
        if (rooms.Count == 0)
        {
            return BadRequest(new { error = "Debe apartar al menos una habitación." });
        }

        var currency = FirstNonBlank(request.Currency, request.Offer?.Currency) is { } moneda
            && !string.IsNullOrWhiteSpace(moneda)
            ? moneda.Trim()
            : "COP";

        Reservation reservation;
        try
        {
            reservation = await _booking.HoldAsync(
                new ReservationRequest(
                    RoomTypeCode: roomTypeCode.Trim(),
                    RatePlanCode: ratePlanCode.Trim(),
                    CheckIn: request.CheckIn,
                    CheckOut: request.CheckOut,
                    Rooms: rooms,
                    GuestName: guestName.Trim(),
                    GuestEmail: guestEmail.Trim(),
                    TotalPrice: totalPrice,
                    Currency: currency),
                cancellationToken);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        return Ok(MapReservation(reservation));
    }

    // ── 3. Pay ─────────────────────────────────────────────────────
    // Recupera la reserva, abre la sesión de pago por el total, captura, y si
    // queda Captured confirma la reserva ligándola a la sesión. Devuelve el
    // estado final (Confirmed + paymentSessionId) o el fallo del PSP.
    [HttpPost("pay")]
    public async Task<IActionResult> Pay(
        [FromBody] PayRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.ReservationId))
        {
            return BadRequest(new { error = "ReservationId es requerido." });
        }

        var pago = await _booking.PayAsync(request.ReservationId, cancellationToken);
        if (pago is null)
        {
            return NotFound(new { error = $"Reserva '{request.ReservationId}' no encontrada." });
        }

        // Reintentar un pago ya confirmado responde con la forma de una RESERVA y no con la de
        // un cobro. Es una verruga de contrato —quien lea `paymentStatus` del reintento no lo
        // encuentra— y está así a propósito: la UI ya la consume, y arreglarla es un cambio de
        // API con su propio ticket, no un efecto colateral de sacar la orquestación del borde.
        if (pago.Outcome == HotelPaymentOutcome.AlreadyConfirmed)
        {
            return Ok(MapReservation(pago.Reservation));
        }

        // Un apartado vencido se grita desde acá, que es donde hay logger. La decisión de NO
        // abrir la caja ya la tomó el flujo; esto solo deja constancia.
        if (pago.Outcome == HotelPaymentOutcome.Conflict
            && pago.Reservation.Status == ReservationStatus.Expired)
        {
            _logger.LogWarning(
                "Reserva {ReservationId}: intento de cobro sobre un hold vencido ({ExpiresAt:o}); "
                + "no se abrió sesión de pago.",
                pago.Reservation.Id, pago.Reservation.ExpiresAt);
        }

        var respuesta = new PayResponse(
            ReservationId: pago.Reservation.Id,
            Status: pago.Reservation.Status.ToString(),
            PaymentStatus: pago.PaymentStatus.ToString(),
            PaymentSessionId: pago.PaymentSessionId,
            AmountCaptured: pago.AmountCaptured,
            AmountFormatted: _priceFormatter.Format(pago.AmountCaptured, pago.Reservation.Currency),
            FailureReason: pago.FailureReason,
            // El COMPROBANTE se arma con esto, y sin ello la UI lo rellenaba con los números
            // que ella misma traía de `search` (ADR 0083, claves `totalPrice` ·
            // `totalPriceFormatted` · `currency` · `checkIn` · `checkOut`). Mientras coincidan
            // no se nota; el día que el motor cobre otra cosa, el voucher miente y nadie ve la
            // diferencia. La reserva las tiene todas: se emiten desde ella.
            TotalPrice: pago.Reservation.TotalPrice,
            TotalPriceFormatted: _priceFormatter.Format(pago.Reservation.TotalPrice, pago.Reservation.Currency),
            Currency: pago.Reservation.Currency,
            CheckIn: pago.Reservation.CheckIn,
            CheckOut: pago.Reservation.CheckOut);

        // Traducir los tres finales a HTTP es trabajo del borde, y la distinción importa: un
        // apartado vencido es 409 —conflicto con el estado del recurso— y no 400, que diría que
        // la petición está mal formada. Una reserva cancelada sí es 400: pedir cobrar algo
        // cancelado es una petición sin sentido.
        return pago.Outcome switch
        {
            HotelPaymentOutcome.Conflict when pago.Reservation.Status == ReservationStatus.Expired
                => Conflict(respuesta),
            HotelPaymentOutcome.Conflict => BadRequest(respuesta),
            _ => Ok(respuesta),
        };
    }

    // ── 4. Cancel ──────────────────────────────────────────────────
    // Libera la reserva y devuelve la penalidad calculada por la política de
    // cancelación del rate plan (evaluada al día de hoy).
    [HttpPost("cancel")]
    public async Task<IActionResult> Cancel(
        [FromBody] CancelRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.ReservationId))
        {
            return BadRequest(new { error = "ReservationId es requerido." });
        }

        var cancelacion = await _booking.CancelAsync(request.ReservationId, request.Reason, cancellationToken);
        if (cancelacion is null)
        {
            return NotFound(new { error = $"Reserva '{request.ReservationId}' no encontrada." });
        }

        // Si el reembolso salió mal se grita desde acá, que es donde hay logger. La reserva YA
        // quedó cancelada y eso no se deshace —el cupo volvió al inventario—, así que callarlo
        // reproduciría el defecto que este flujo cierra.
        if (cancelacion.RefundStatus is { } estado
            && !string.Equals(estado, PaymentStatus.Refunded.ToString(), StringComparison.Ordinal))
        {
            _logger.LogError(
                "Reserva {ReservationId}: cancelada pero el reembolso quedó en {Status}.",
                cancelacion.Reservation.Id, estado);
        }

        // Se devuelve 200 aunque no se haya movido nada: cancelar lo ya cancelado es el
        // resultado que el huésped pidió, y reintentar tras un timeout de red no puede parecer
        // un fallo.
        return Ok(new CancelResponse(
            ReservationId: cancelacion.Reservation.Id,
            Status: cancelacion.Reservation.Status.ToString(),
            Refundable: cancelacion.Refundable,
            PenaltyAmount: cancelacion.PenaltyAmount,
            PenaltyFormatted: _priceFormatter.Format(cancelacion.PenaltyAmount, cancelacion.Reservation.Currency),
            PolicyDescription: cancelacion.PolicyDescription,
            RefundStatus: cancelacion.RefundStatus));
    }

    // ── 5. Get ─────────────────────────────────────────────────────
    [HttpGet("{reservationId}")]
    public async Task<IActionResult> Get(string reservationId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reservationId))
        {
            return BadRequest(new { error = "ReservationId es requerido." });
        }

        var reservation = await _booking.GetAsync(reservationId, cancellationToken);
        return reservation is null
            ? NotFound(new { error = $"Reserva '{reservationId}' no encontrada." })
            : Ok(MapReservation(reservation));
    }

    // ── Helpers ────────────────────────────────────────────────────

    // Mapea las ocupaciones del request al record del seam, defendiendo contra
    // nulls del binding JSON (Rooms/ChildAges opcionales en el payload).
    private static IReadOnlyList<RoomOccupancy> MapRooms(IReadOnlyList<RoomOccupancyRequest>? rooms)
    {
        if (rooms is null || rooms.Count == 0)
        {
            return Array.Empty<RoomOccupancy>();
        }

        return rooms
            .Select(r => new RoomOccupancy(
                Adults: r.Adults,
                ChildAges: r.ChildAges ?? Array.Empty<int>()))
            .ToList();
    }

    /// <summary>
    /// El separador del <c>offerId</c>. No es un id nuevo inventado: una oferta de este
    /// dominio ES un tipo de habitación sobre un plan tarifario, y el par se compone al
    /// buscar y se vuelve a partir al apartar. Un id opaco obligaría a guardar un mapa que
    /// nadie vaciaría nunca.
    /// </summary>
    private const string OfferIdSeparator = "::";

    internal static string ComposeOfferId(string roomTypeCode, string ratePlanCode)
        => $"{roomTypeCode}{OfferIdSeparator}{ratePlanCode}";

    /// <summary>
    /// De dónde salen el tipo de habitación y el plan tarifario del <c>hold</c>: de los
    /// campos planos si vienen, del <c>offerId</c> si no, y del objeto <c>offer</c> anidado
    /// como última fuente.
    /// </summary>
    private static (string? RoomTypeCode, string? RatePlanCode) ResolveOffer(HoldRequest request)
    {
        var roomTypeCode = FirstNonBlank(request.RoomTypeCode, request.Offer?.RoomTypeCode);
        var ratePlanCode = FirstNonBlank(request.RatePlanCode, request.Offer?.RatePlanCode);
        if (!string.IsNullOrWhiteSpace(roomTypeCode) && !string.IsNullOrWhiteSpace(ratePlanCode))
        {
            return (roomTypeCode, ratePlanCode);
        }

        var offerId = FirstNonBlank(request.OfferId, request.Offer?.OfferId);
        if (string.IsNullOrWhiteSpace(offerId))
        {
            return (roomTypeCode, ratePlanCode);
        }

        var parts = offerId.Split(OfferIdSeparator, StringSplitOptions.TrimEntries);
        return parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1])
            // Un offerId que no se entiende NO se adivina: se deja como vino y la validación
            // de arriba contesta 400. Partirlo a medias apartaría una habitación distinta de
            // la que se eligió, que es peor que no apartar.
            ? (roomTypeCode, ratePlanCode)
            : (roomTypeCode ?? parts[0], ratePlanCode ?? parts[1]);
    }

    /// <summary>El primero de los dos que traiga algo. Blanco cuenta como ausente.</summary>
    private static string? FirstNonBlank(string? first, string? second)
        => !string.IsNullOrWhiteSpace(first) ? first : second;

    private ReservationResponse MapReservation(Reservation reservation) => new(
        ReservationId: reservation.Id,
        Status: reservation.Status.ToString(),
        RoomTypeCode: reservation.RoomTypeCode,
        RatePlanCode: reservation.RatePlanCode,
        CheckIn: reservation.CheckIn,
        CheckOut: reservation.CheckOut,
        GuestName: reservation.GuestName,
        GuestEmail: reservation.GuestEmail,
        TotalPrice: reservation.TotalPrice,
        TotalPriceFormatted: _priceFormatter.Format(reservation.TotalPrice, reservation.Currency),
        Currency: reservation.Currency,
        PaymentSessionId: reservation.PaymentSessionId);

    // ── Request DTOs (binding del wizard search→hold→pay→cancel) ────

    /// <summary>Ocupación de UNA habitación en el payload (niños por edad).</summary>
    public sealed record RoomOccupancyRequest(int Adults, IReadOnlyList<int>? ChildAges);

    /// <summary>POST /api/booking/search — rango + ocupación por habitación.</summary>
    public sealed record SearchRequest(
        DateOnly CheckIn,
        DateOnly CheckOut,
        IReadOnlyList<RoomOccupancyRequest>? Rooms);

    /// <summary>POST /api/booking/hold — oferta elegida + datos del huésped.</summary>
    /// <remarks>
    /// <para><b>Acepta dos formas del mismo cuerpo, y ninguna es opcional por comodidad.</b>
    /// La plana —<c>roomTypeCode</c>/<c>ratePlanCode</c>/<c>guestName</c>/<c>guestEmail</c>/
    /// <c>totalPrice</c>— es la de este borde. La anidada —<c>offerId</c> + <c>offer</c> +
    /// <c>guest</c>— es la que manda <c>&lt;synergos-booking-wizard&gt;</c>, que reenvía la
    /// oferta tal como se la dio <c>search</c>.</para>
    ///
    /// <para>Antes sólo existía la plana, con los campos NO nulables: el cuerpo del asistente
    /// no ligaba ni uno —System.Text.Json descarta en silencio lo que no mapea— y
    /// <c>hold</c> contestaba <c>400</c> <b>siempre</b>. No se veía porque el cliente
    /// devuelve <c>null</c> al fallar y el asistente lo lee como «la habitación ya no está»:
    /// un borde roto y un hotel lleno se ven exactamente igual.</para>
    /// </remarks>
    public sealed record HoldRequest(
        DateOnly CheckIn,
        DateOnly CheckOut,
        IReadOnlyList<RoomOccupancyRequest>? Rooms,
        string? RoomTypeCode = null,
        string? RatePlanCode = null,
        string? GuestName = null,
        string? GuestEmail = null,
        decimal TotalPrice = 0m,
        string? Currency = null,
        string? OfferId = null,
        HoldOfferRequest? Offer = null,
        HoldGuestRequest? Guest = null);

    /// <summary>La oferta reenviada tal como salió de <c>search</c>.</summary>
    public sealed record HoldOfferRequest(
        string? OfferId = null,
        string? RoomTypeCode = null,
        string? RatePlanCode = null,
        decimal TotalPrice = 0m,
        string? Currency = null);

    /// <summary>El huésped, anidado como lo manda el asistente.</summary>
    public sealed record HoldGuestRequest(string? Name = null, string? Email = null);

    /// <summary>POST /api/booking/pay — la reserva a cobrar.</summary>
    public sealed record PayRequest(string ReservationId);

    /// <summary>POST /api/booking/cancel — la reserva a liberar + motivo.</summary>
    public sealed record CancelRequest(string ReservationId, string? Reason);

    // ── Response DTOs (JSON estable para la UI) ─────────────────────

    /// <summary>Una oferta enriquecida con precio es-CO + texto de política.</summary>
    /// <param name="OfferId">
    /// Lo que identifica la oferta para la UI, que la lee como <c>offerId</c> (?? <c>id</c> ??
    /// <c>rateKey</c>). <b>No estaba, y sin ella el asistente descartaba TODAS las ofertas</b>:
    /// su normalizador devuelve <c>null</c> cuando no encuentra ninguna de las tres, así que
    /// contra este borde la búsqueda salía vacía siempre — y el cliente de reservas no degrada
    /// a mock a propósito, así que lo que se veía era un hotel sin habitaciones.
    /// </param>
    /// <param name="Board">
    /// El régimen, con la clave que lee la UI. <see cref="BoardBasis"/> se conserva.
    /// </param>
    public sealed record RoomOfferResponse(
        string OfferId,
        string RoomTypeCode,
        string RoomTypeName,
        string RatePlanCode,
        string BoardBasis,
        string Board,
        decimal TotalPrice,
        string TotalPriceFormatted,
        string Currency,
        bool Refundable,
        string CancellationPolicy,
        int? MinStayNights,
        int RoomsLeft);

    public sealed record SearchResponse(
        DateOnly CheckIn,
        DateOnly CheckOut,
        IReadOnlyList<RoomOfferResponse> Offers);

    public sealed record ReservationResponse(
        string ReservationId,
        string Status,
        string RoomTypeCode,
        string RatePlanCode,
        DateOnly CheckIn,
        DateOnly CheckOut,
        string GuestName,
        string GuestEmail,
        decimal TotalPrice,
        string TotalPriceFormatted,
        string Currency,
        string? PaymentSessionId);

    /// <param name="TotalPrice">
    /// El total de la reserva, con las claves que el comprobante lee (<c>totalPrice</c> ·
    /// <c>totalPriceFormatted</c> · <c>currency</c> · <c>checkIn</c> · <c>checkOut</c>).
    /// Faltaban las cinco, así que el voucher se rellenaba con lo que la UI traía de
    /// <c>search</c>: mientras coincidan no se nota, y el día que no coincidan el huésped
    /// se lleva impreso un número que el motor nunca cobró.
    /// </param>
    public sealed record PayResponse(
        string ReservationId,
        string Status,
        string PaymentStatus,
        string? PaymentSessionId,
        decimal AmountCaptured,
        string AmountFormatted,
        string? FailureReason,
        decimal TotalPrice = 0m,
        string TotalPriceFormatted = "",
        string Currency = "",
        DateOnly CheckIn = default,
        DateOnly CheckOut = default);

    /// <param name="RefundStatus">Qué pasó con la devolución del dinero.
    ///   <c>null</c> cuando no había nada que devolver (reserva no cobrada, o
    ///   penalidad igual al total). Se expone a propósito: antes esta respuesta
    ///   informaba un monto reembolsable que nadie devolvía, y el huésped no
    ///   tenía forma de notar la diferencia.</param>
    public sealed record CancelResponse(
        string ReservationId,
        string Status,
        bool Refundable,
        decimal PenaltyAmount,
        string PenaltyFormatted,
        string PolicyDescription,
        string? RefundStatus = null);
}
