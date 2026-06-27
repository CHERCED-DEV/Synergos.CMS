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
    private readonly IReservationService _reservations;
    private readonly IPaymentProvider _payments;
    private readonly ICancellationPolicyEvaluator _cancellationPolicy;
    private readonly IPriceFormatter _priceFormatter;

    public BookingController(
        IRoomAvailabilityProvider availability,
        IReservationService reservations,
        IPaymentProvider payments,
        ICancellationPolicyEvaluator cancellationPolicy,
        IPriceFormatter priceFormatter)
    {
        _availability = availability;
        _reservations = reservations;
        _payments = payments;
        _cancellationPolicy = cancellationPolicy;
        _priceFormatter = priceFormatter;
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
            reservation = await _reservations.HoldAsync(
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

        var reservation = await _reservations.GetAsync(request.ReservationId, cancellationToken);
        if (reservation is null)
        {
            return NotFound(new { error = $"Reserva '{request.ReservationId}' no encontrada." });
        }

        // Idempotencia: si ya está confirmada, devolver el estado actual sin
        // volver a cobrar.
        if (reservation.Status == ReservationStatus.Confirmed)
        {
            return Ok(MapReservation(reservation));
        }
        if (reservation.Status == ReservationStatus.Cancelled)
        {
            return BadRequest(new PayResponse(
                ReservationId: reservation.Id,
                Status: reservation.Status.ToString(),
                PaymentStatus: PaymentStatus.Cancelled.ToString(),
                PaymentSessionId: null,
                AmountCaptured: 0m,
                AmountFormatted: _priceFormatter.Format(0m, reservation.Currency),
                FailureReason: "La reserva está cancelada; no se puede cobrar."));
        }

        var session = await _payments.CreateSessionAsync(
            new PaymentSessionRequest(
                OrderReference: reservation.Id,
                Amount: reservation.TotalPrice,
                Currency: reservation.Currency,
                Items: new[]
                {
                    new PaymentLineItem(
                        Sku: $"{reservation.RoomTypeCode}/{reservation.RatePlanCode}",
                        Description: $"Reserva {reservation.RoomTypeCode} ({reservation.CheckIn:yyyy-MM-dd} → {reservation.CheckOut:yyyy-MM-dd})",
                        UnitPrice: reservation.TotalPrice,
                        Quantity: 1),
                },
                CustomerEmail: reservation.GuestEmail,
                ReturnUrl: null,
                Metadata: null),
            cancellationToken);

        var capture = await _payments.CaptureAsync(session.SessionId, cancellationToken);

        if (capture.Status != PaymentStatus.Captured)
        {
            // El cobro no se completó (RequiresAction / Failed / etc.). No se
            // confirma la reserva; el cliente reintenta o sigue el redirect.
            var failureReason = capture.FailureReason
                ?? (session.RedirectUrl is not null
                    ? "El pago requiere una acción adicional del cliente."
                    : null);
            return Ok(new PayResponse(
                ReservationId: reservation.Id,
                Status: reservation.Status.ToString(),
                PaymentStatus: capture.Status.ToString(),
                PaymentSessionId: session.SessionId,
                AmountCaptured: capture.AmountCaptured,
                AmountFormatted: _priceFormatter.Format(capture.AmountCaptured, reservation.Currency),
                FailureReason: failureReason));
        }

        var confirmed = await _reservations.ConfirmAsync(reservation.Id, session.SessionId, cancellationToken);

        return Ok(new PayResponse(
            ReservationId: confirmed.Id,
            Status: confirmed.Status.ToString(),
            PaymentStatus: capture.Status.ToString(),
            PaymentSessionId: confirmed.PaymentSessionId ?? session.SessionId,
            AmountCaptured: capture.AmountCaptured,
            AmountFormatted: _priceFormatter.Format(capture.AmountCaptured, confirmed.Currency),
            FailureReason: null));
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

        var reservation = await _reservations.GetAsync(request.ReservationId, cancellationToken);
        if (reservation is null)
        {
            return NotFound(new { error = $"Reserva '{request.ReservationId}' no encontrada." });
        }

        var reason = string.IsNullOrWhiteSpace(request.Reason) ? "guest-requested" : request.Reason.Trim();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var outcome = _cancellationPolicy.Evaluate(reservation.RatePlanCode, reservation.CheckIn, today);

        var cancelled = await _reservations.CancelAsync(reservation.Id, reason, cancellationToken);

        return Ok(new CancelResponse(
            ReservationId: cancelled.Id,
            Status: cancelled.Status.ToString(),
            Refundable: outcome.Refundable,
            PenaltyAmount: outcome.PenaltyAmount,
            PenaltyFormatted: _priceFormatter.Format(outcome.PenaltyAmount, cancelled.Currency),
            PolicyDescription: outcome.Description));
    }

    // ── 5. Get ─────────────────────────────────────────────────────
    [HttpGet("{reservationId}")]
    public async Task<IActionResult> Get(string reservationId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reservationId))
        {
            return BadRequest(new { error = "ReservationId es requerido." });
        }

        var reservation = await _reservations.GetAsync(reservationId, cancellationToken);
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

    public sealed record CancelResponse(
        string ReservationId,
        string Status,
        bool Refundable,
        decimal PenaltyAmount,
        string PenaltyFormatted,
        string PolicyDescription);
}
