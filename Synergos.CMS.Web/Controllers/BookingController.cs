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
                RoomTypeCode: offer.RoomTypeCode,
                RoomTypeName: offer.RoomTypeName,
                RatePlanCode: offer.RatePlanCode,
                BoardBasis: offer.BoardBasis,
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
        if (string.IsNullOrWhiteSpace(request.RoomTypeCode) || string.IsNullOrWhiteSpace(request.RatePlanCode))
        {
            return BadRequest(new { error = "RoomTypeCode y RatePlanCode son requeridos." });
        }
        if (string.IsNullOrWhiteSpace(request.GuestName) || string.IsNullOrWhiteSpace(request.GuestEmail))
        {
            return BadRequest(new { error = "GuestName y GuestEmail son requeridos." });
        }
        if (request.TotalPrice <= 0m)
        {
            return BadRequest(new { error = "TotalPrice debe ser mayor que cero." });
        }

        var rooms = MapRooms(request.Rooms);
        if (rooms.Count == 0)
        {
            return BadRequest(new { error = "Debe apartar al menos una habitación." });
        }

        var currency = string.IsNullOrWhiteSpace(request.Currency) ? "COP" : request.Currency.Trim();

        Reservation reservation;
        try
        {
            reservation = await _booking.HoldAsync(
                new ReservationRequest(
                    RoomTypeCode: request.RoomTypeCode.Trim(),
                    RatePlanCode: request.RatePlanCode.Trim(),
                    CheckIn: request.CheckIn,
                    CheckOut: request.CheckOut,
                    Rooms: rooms,
                    GuestName: request.GuestName.Trim(),
                    GuestEmail: request.GuestEmail.Trim(),
                    TotalPrice: request.TotalPrice,
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
            FailureReason: pago.FailureReason);

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
    public sealed record HoldRequest(
        string RoomTypeCode,
        string RatePlanCode,
        DateOnly CheckIn,
        DateOnly CheckOut,
        IReadOnlyList<RoomOccupancyRequest>? Rooms,
        string GuestName,
        string GuestEmail,
        decimal TotalPrice,
        string? Currency);

    /// <summary>POST /api/booking/pay — la reserva a cobrar.</summary>
    public sealed record PayRequest(string ReservationId);

    /// <summary>POST /api/booking/cancel — la reserva a liberar + motivo.</summary>
    public sealed record CancelRequest(string ReservationId, string? Reason);

    // ── Response DTOs (JSON estable para la UI) ─────────────────────

    /// <summary>Una oferta enriquecida con precio es-CO + texto de política.</summary>
    public sealed record RoomOfferResponse(
        string RoomTypeCode,
        string RoomTypeName,
        string RatePlanCode,
        string BoardBasis,
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

    public sealed record PayResponse(
        string ReservationId,
        string Status,
        string PaymentStatus,
        string? PaymentSessionId,
        decimal AmountCaptured,
        string AmountFormatted,
        string? FailureReason);

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
