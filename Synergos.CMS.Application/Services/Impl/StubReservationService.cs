using System.Collections.Concurrent;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Default <see cref="IReservationService"/> — servicio de reservas STUB para
/// que el vertical Hoteles corra end-to-end en demo sin un PMS/DB real (mismo
/// patrón stub-first que <c>StubPaymentProvider</c>). Aparta (Hold), confirma
/// y cancela reservas en memoria.
/// </summary>
/// <remarks>
/// Lógica pura en <c>Synergos.CMS.Application</c> — cero dependencia de
/// Umbraco/AspNetCore (ADR 0002). El estado vive en memoria (proceso),
/// suficiente para demo; un adapter real delega el estado al PMS/DB.
/// <see cref="ConfirmAsync"/> es idempotente: confirmar dos veces deja la
/// reserva Confirmed sin doble efecto. El adapter real implementa la misma
/// seam y se registra en su lugar vía el composer sin tocar el motor.
/// </remarks>
public sealed class StubReservationService : IReservationService
{
    /// <summary>
    /// Ventana de hold por defecto: 15 min para completar checkout/pago antes
    /// de que el cupo se libere automáticamente. Aprendizaje de NS.Booking (doc 17).
    /// </summary>
    public static readonly TimeSpan DefaultHoldWindow = TimeSpan.FromMinutes(15);

    private readonly ConcurrentDictionary<string, Reservation> _reservations = new(StringComparer.Ordinal);
    private readonly TimeSpan _holdWindow;
    private readonly Func<DateTimeOffset> _now;

    /// <summary>
    /// Default ctor — hold window de 15 min y reloj real (<see cref="DateTimeOffset.UtcNow"/>).
    /// </summary>
    public StubReservationService()
        : this(DefaultHoldWindow, null)
    {
    }

    /// <summary>
    /// Ctor configurable: <paramref name="holdWindow"/> para ajustar la ventana
    /// del hold (≤ 0 cae al default) y <paramref name="now"/> como time source
    /// inyectable para determinismo en tests (ADR 0002: Application sin Umbraco,
    /// time source simple en vez de un clock framework). Null = reloj real.
    /// </summary>
    public StubReservationService(TimeSpan holdWindow, Func<DateTimeOffset>? now)
    {
        _holdWindow = holdWindow > TimeSpan.Zero ? holdWindow : DefaultHoldWindow;
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    public Task<Reservation> HoldAsync(ReservationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.CheckOut <= request.CheckIn)
        {
            throw new ArgumentException("La fecha de salida debe ser posterior a la de entrada.", nameof(request));
        }
        if (string.IsNullOrWhiteSpace(request.RoomTypeCode) || string.IsNullOrWhiteSpace(request.RatePlanCode))
        {
            throw new ArgumentException("El room type y el rate plan son obligatorios.", nameof(request));
        }
        if (request.TotalPrice <= 0m)
        {
            throw new ArgumentException("El total de la reserva debe ser mayor a cero.", nameof(request));
        }
        if (request.Rooms is null || request.Rooms.Count == 0)
        {
            throw new ArgumentException("Se requiere al menos una habitación con ocupación.", nameof(request));
        }

        var id = $"resv_{Guid.NewGuid():N}";
        var reservation = new Reservation(
            id,
            ReservationStatus.Held,
            request.RoomTypeCode,
            request.RatePlanCode,
            request.CheckIn,
            request.CheckOut,
            request.GuestName,
            request.GuestEmail,
            request.TotalPrice,
            request.Currency,
            PaymentSessionId: null,
            ExpiresAt: _now() + _holdWindow);
        _reservations[id] = reservation;
        return Task.FromResult(reservation);
    }

    public Task<Reservation> HoldItemAsync(TravelItemReservationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.ProductRef))
        {
            throw new ArgumentException("La referencia del producto (offerId) es obligatoria.", nameof(request));
        }
        if (request.TotalPrice <= 0m)
        {
            throw new ArgumentException("El total de la reserva debe ser mayor a cero.", nameof(request));
        }
        if (string.IsNullOrWhiteSpace(request.Currency))
        {
            throw new ArgumentException("La moneda es obligatoria.", nameof(request));
        }

        // Generaliza el hold hotel a un ítem polimórfico: los campos hotel-only
        // (RoomType/RatePlan, CheckIn/CheckOut, ocupación) no aplican a vuelo/auto,
        // así que se dejan como placeholders neutros y la identidad del producto
        // viaja en ProductType/ProductRef/ProductLabel. El resto del ciclo de vida
        // (Confirm/Cancel/Get/ExpireStaleHolds) es común — opera por id, no por forma.
        var id = $"resv_{Guid.NewGuid():N}";
        var today = DateOnly.FromDateTime(_now().UtcDateTime);
        var reservation = new Reservation(
            id,
            ReservationStatus.Held,
            RoomTypeCode: request.ProductRef,
            RatePlanCode: request.ProductType.ToString(),
            CheckIn: today,
            CheckOut: today.AddDays(1),
            GuestName: request.GuestName,
            GuestEmail: request.GuestEmail,
            TotalPrice: request.TotalPrice,
            Currency: request.Currency,
            PaymentSessionId: null,
            ExpiresAt: _now() + _holdWindow,
            ProductType: request.ProductType,
            ProductRef: request.ProductRef,
            ProductLabel: request.ProductLabel);
        _reservations[id] = reservation;
        return Task.FromResult(reservation);
    }

    public Task<Reservation> ConfirmAsync(string reservationId, string paymentSessionId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(reservationId) || !_reservations.TryGetValue(reservationId, out var current))
        {
            throw new ArgumentException("Reserva no encontrada.", nameof(reservationId));
        }
        if (current.Status == ReservationStatus.Cancelled)
        {
            throw new InvalidOperationException("No se puede confirmar una reserva cancelada.");
        }
        if (current.Status == ReservationStatus.Expired)
        {
            throw new InvalidOperationException("No se puede confirmar una reserva con el hold vencido.");
        }

        // Idempotente: si ya está Confirmed, devuelve el mismo estado (sin
        // sobreescribir el PaymentSessionId original ni duplicar efecto).
        if (current.Status == ReservationStatus.Confirmed)
        {
            return Task.FromResult(current);
        }

        // Hold vencido (Held pero now > ExpiresAt): la confirmación llega tarde,
        // el cupo ya no está garantizado. La marca Expired in-line para que un
        // GetAsync posterior lo refleje, y rechaza la confirmación. El scanner
        // de fondo también la habría barrido, pero no dependemos de su timing.
        if (current.ExpiresAt is { } expiresAt && _now() > expiresAt)
        {
            _reservations[reservationId] = current with { Status = ReservationStatus.Expired };
            throw new InvalidOperationException("El hold de la reserva venció antes de confirmar.");
        }

        var confirmed = current with { Status = ReservationStatus.Confirmed, PaymentSessionId = paymentSessionId };
        _reservations[reservationId] = confirmed;
        return Task.FromResult(confirmed);
    }

    public Task<Reservation> CancelAsync(string reservationId, string reason, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(reservationId) || !_reservations.TryGetValue(reservationId, out var current))
        {
            throw new ArgumentException("Reserva no encontrada.", nameof(reservationId));
        }

        // Idempotente: cancelar una reserva ya cancelada deja el mismo estado.
        if (current.Status == ReservationStatus.Cancelled)
        {
            return Task.FromResult(current);
        }

        var cancelled = current with { Status = ReservationStatus.Cancelled };
        _reservations[reservationId] = cancelled;
        return Task.FromResult(cancelled);
    }

    public Task<Reservation?> GetAsync(string reservationId, CancellationToken cancellationToken = default)
        => Task.FromResult(_reservations.TryGetValue(reservationId ?? string.Empty, out var r) ? r : null);

    public Task<int> ExpireStaleHoldsAsync(CancellationToken cancellationToken = default)
    {
        var now = _now();
        var expired = 0;
        foreach (var kvp in _reservations)
        {
            var current = kvp.Value;
            if (current.Status != ReservationStatus.Held
                || current.ExpiresAt is not { } expiresAt
                || now <= expiresAt)
            {
                continue;
            }
            // TryUpdate: solo transiciona si sigue siendo el mismo Held (evita
            // pisar una confirmación/cancelación que entró en paralelo). Idempotente.
            if (_reservations.TryUpdate(kvp.Key, current with { Status = ReservationStatus.Expired }, current))
            {
                expired++;
            }
        }
        return Task.FromResult(expired);
    }
}
