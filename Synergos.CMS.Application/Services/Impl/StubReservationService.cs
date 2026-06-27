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
    private readonly ConcurrentDictionary<string, Reservation> _reservations = new(StringComparer.Ordinal);

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
            PaymentSessionId: null);
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

        // Idempotente: si ya está Confirmed, devuelve el mismo estado (sin
        // sobreescribir el PaymentSessionId original ni duplicar efecto).
        if (current.Status == ReservationStatus.Confirmed)
        {
            return Task.FromResult(current);
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
}
