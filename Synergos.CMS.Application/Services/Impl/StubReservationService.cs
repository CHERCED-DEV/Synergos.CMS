using System.Text.Encodings.Web;
using System.Text.Json;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Default <see cref="IReservationService"/> — servicio de reservas STUB para que el
/// vertical Hoteles corra end-to-end en demo sin un PMS/DB real (mismo patrón
/// stub-first que <c>StubPaymentProvider</c>). Aparta (Hold), confirma y cancela
/// reservas.
/// </summary>
/// <remarks>
/// Lógica pura en <c>Synergos.CMS.Application</c> — cero dependencia de
/// Umbraco/AspNetCore (ADR 0002). <b>T3 (doc 25):</b> el estado ya NO vive en un
/// diccionario del proceso sino detrás del seam <see cref="IJsonEntityStore"/> — con
/// un adapter FileSystem el hold SOBREVIVE un reinicio. Esto es necesario para cerrar
/// la brecha de restart end-to-end: <c>ConfirmAsync</c> de una orden confirma sus
/// reservas de stock; si el hold viviera solo en memoria, tras un reinicio la
/// confirmación lanzaría "Reserva no encontrada" ANTES de marcar la orden pagada.
/// <see cref="ConfirmAsync"/> es idempotente. El adapter real (PMS/DB) implementa la
/// misma seam y se registra en su lugar vía el composer sin tocar el motor.
/// </remarks>
public sealed class StubReservationService : IReservationService
{
    /// <summary>
    /// Ventana de hold por defecto: 15 min para completar checkout/pago antes de que
    /// el cupo se libere automáticamente. Aprendizaje de NS.Booking (doc 17).
    /// </summary>
    public static readonly TimeSpan DefaultHoldWindow = TimeSpan.FromMinutes(15);

    private static readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Familia de entidades de este motor en el store genérico (→ App_Data/syn-reservations/).</summary>
    private const string ResourceType = "reservations";

    private readonly IJsonEntityStore _store;
    private readonly TimeSpan _holdWindow;
    private readonly Func<DateTimeOffset> _now;
    // Serializa el read-modify-write de Confirm/Cancel/Expire (single-instance): sin
    // lock{} porque el cuerpo hace await; SemaphoreSlim es el equivalente async.
    private readonly SemaphoreSlim _mutate = new(1, 1);

    /// <summary>
    /// Default ctor — hold window de 15 min, reloj real y backing store en memoria.
    /// </summary>
    public StubReservationService()
        : this(DefaultHoldWindow, null, null)
    {
    }

    /// <summary>
    /// Ctor configurable: <paramref name="holdWindow"/> ajusta la ventana del hold (≤ 0
    /// cae al default), <paramref name="now"/> es el time source inyectable para
    /// determinismo en tests, y <paramref name="store"/> (null → en memoria) es el
    /// backing store — el composer inyecta el durable (FileSystem) en Web.
    /// </summary>
    public StubReservationService(TimeSpan holdWindow, Func<DateTimeOffset>? now, IJsonEntityStore? store = null)
    {
        _holdWindow = holdWindow > TimeSpan.Zero ? holdWindow : DefaultHoldWindow;
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _store = store ?? new InMemoryJsonEntityStore();
    }

    public async Task<Reservation> HoldAsync(ReservationRequest request, CancellationToken cancellationToken = default)
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
        await WriteAsync(reservation, cancellationToken);
        return reservation;
    }

    public async Task<Reservation> HoldItemAsync(TravelItemReservationRequest request, CancellationToken cancellationToken = default)
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
        await WriteAsync(reservation, cancellationToken);
        return reservation;
    }

    public async Task<Reservation> ConfirmAsync(string reservationId, string paymentSessionId, CancellationToken cancellationToken = default)
    {
        await _mutate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var current = await LoadAsync(reservationId, cancellationToken);
            if (current is null)
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
                return current;
            }

            // Hold vencido (Held pero now > ExpiresAt): la confirmación llega tarde. La
            // marca Expired in-line para que un GetAsync posterior lo refleje, y rechaza.
            if (current.ExpiresAt is { } expiresAt && _now() > expiresAt)
            {
                await WriteAsync(current with { Status = ReservationStatus.Expired }, cancellationToken);
                throw new InvalidOperationException("El hold de la reserva venció antes de confirmar.");
            }

            var confirmed = current with { Status = ReservationStatus.Confirmed, PaymentSessionId = paymentSessionId };
            await WriteAsync(confirmed, cancellationToken);
            return confirmed;
        }
        finally
        {
            _mutate.Release();
        }
    }

    public async Task<Reservation> CancelAsync(string reservationId, string reason, CancellationToken cancellationToken = default)
    {
        await _mutate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var current = await LoadAsync(reservationId, cancellationToken);
            if (current is null)
            {
                throw new ArgumentException("Reserva no encontrada.", nameof(reservationId));
            }

            // Idempotente: cancelar una reserva ya cancelada deja el mismo estado.
            if (current.Status == ReservationStatus.Cancelled)
            {
                return current;
            }

            var cancelled = current with { Status = ReservationStatus.Cancelled };
            await WriteAsync(cancelled, cancellationToken);
            return cancelled;
        }
        finally
        {
            _mutate.Release();
        }
    }

    public async Task<Reservation?> GetAsync(string reservationId, CancellationToken cancellationToken = default)
        => await LoadAsync(reservationId, cancellationToken);

    public async Task<int> ExpireStaleHoldsAsync(CancellationToken cancellationToken = default)
    {
        await _mutate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var now = _now();
            var expired = 0;
            foreach (var raw in await _store.ListAsync(ResourceType, cancellationToken).ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var current = Deserialize(raw);
                if (current is null
                    || current.Status != ReservationStatus.Held
                    || current.ExpiresAt is not { } expiresAt
                    || now <= expiresAt)
                {
                    continue;
                }
                await WriteAsync(current with { Status = ReservationStatus.Expired }, cancellationToken);
                expired++;
            }
            return expired;
        }
        finally
        {
            _mutate.Release();
        }
    }

    private async Task<Reservation?> LoadAsync(string? reservationId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(reservationId))
        {
            return null;
        }
        return Deserialize(await _store.ReadAsync(ResourceType, reservationId, cancellationToken).ConfigureAwait(false));
    }

    private static Reservation? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }
        try { return JsonSerializer.Deserialize<Reservation>(json, _json); }
        catch (JsonException) { return null; }   // corrupto → como si no existiera
    }

    private Task WriteAsync(Reservation reservation, CancellationToken cancellationToken)
        => _store.WriteAsync(ResourceType, reservation.Id, JsonSerializer.Serialize(reservation, _json), cancellationToken);
}
