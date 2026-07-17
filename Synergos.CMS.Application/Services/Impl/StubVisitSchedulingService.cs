using System.Collections.Concurrent;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Default <see cref="IVisitSchedulingService"/> — agendamiento de visitas STUB
/// del portal inmobiliario (doc propiedades-app-spec §2), el sub-flujo
/// transaccional de la PDP. REUSA EL MOTOR: la visita es un RECURSO RESERVABLE
/// POLIMÓRFICO (igual que habitación/asiento/médico), apartada vía
/// <see cref="IReservationService.HoldItemAsync"/> + confirmada con
/// <see cref="IReservationService.ConfirmAsync"/> — pero <strong>SIN pago</strong>
/// (la visita es gratis), validando el flujo <c>seleccionar→[pagar]→confirmar</c>
/// con el paso de pago desactivado.
/// </summary>
/// <remarks>
/// Lógica pura en <c>Synergos.CMS.Application</c> — cero dependencia de
/// Umbraco/AspNetCore (ADR 0002). La agenda se siembra en memoria (slots por
/// listado, derivados deterministamente del id); apartar un slot lo marca como no
/// disponible para futuras consultas. <see cref="BookAsync"/> es idempotente por
/// (listado, slot): re-agendar el mismo slot ya confirmado devuelve la misma
/// visita sin segundo hold. El adapter real (agenda del agente en un CRM/DB) se
/// enchufa sin tocar el motor. ADR 0075.
/// </remarks>
public sealed class StubVisitSchedulingService : IVisitSchedulingService
{
    /// <summary>PaymentSessionId neutro: la visita NO se cobra (spec §1).</summary>
    private const string FreeVisitPaymentSession = "visit-free";

    private const string Cop = "COP";

    private readonly IReservationService _reservations;
    private readonly Func<DateTimeOffset> _now;

    // Slots ya apartados: clave "(listingId)/(slotId)" → visita confirmada
    // (idempotencia + marca de disponibilidad para GetSlotsAsync).
    private readonly ConcurrentDictionary<string, VisitResult> _booked = new(StringComparer.OrdinalIgnoreCase);

    public StubVisitSchedulingService(IReservationService reservations)
        : this(reservations, null)
    {
    }

    /// <summary>
    /// Ctor configurable con time source inyectable (<paramref name="now"/>) para
    /// determinismo en tests (ADR 0002). Null = reloj real.
    /// </summary>
    public StubVisitSchedulingService(IReservationService reservations, Func<DateTimeOffset>? now)
    {
        _reservations = reservations ?? throw new ArgumentNullException(nameof(reservations));
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    public Task<IReadOnlyList<VisitSlot>> GetSlotsAsync(string listingId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(listingId))
        {
            return Task.FromResult<IReadOnlyList<VisitSlot>>(Array.Empty<VisitSlot>());
        }

        var slots = BuildSlots(listingId.Trim())
            .Select(s => _booked.ContainsKey(Key(listingId.Trim(), s.Id))
                ? s with { Available = false }
                : s)
            .ToList();
        return Task.FromResult<IReadOnlyList<VisitSlot>>(slots);
    }

    public async Task<VisitResult> BookAsync(string listingId, string slot, VisitContact contact, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(listingId))
        {
            throw new ArgumentException("El listado es obligatorio.", nameof(listingId));
        }
        if (string.IsNullOrWhiteSpace(slot))
        {
            throw new ArgumentException("El slot de visita es obligatorio.", nameof(slot));
        }
        if (contact is null || string.IsNullOrWhiteSpace(contact.Name) || string.IsNullOrWhiteSpace(contact.Email))
        {
            throw new ArgumentException("El nombre y el email del interesado son obligatorios.", nameof(contact));
        }

        var listing = listingId.Trim();
        var slotId = slot.Trim();
        var key = Key(listing, slotId);

        // Idempotente: el mismo slot ya confirmado devuelve la misma visita.
        if (_booked.TryGetValue(key, out var existing))
        {
            return existing;
        }

        // El slot debe existir en la agenda sembrada del listado.
        var match = BuildSlots(listing).FirstOrDefault(s =>
            string.Equals(s.Id, slotId, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException(
                $"El slot '{slotId}' no existe para el listado '{listing}'.", nameof(slot));

        // 1) Apartar el slot como un recurso reservable (hold-timeout incluido).
        //    Visita gratis → total simbólico 1 (HoldItemAsync exige total > 0);
        //    no se cobra: el confirm usa el PaymentSessionId neutro "visit-free".
        var reservation = await _reservations.HoldItemAsync(
            new TravelItemReservationRequest(
                ProductType: TravelProductType.Hotel,
                ProductRef: $"{listing}/visit/{slotId}",
                ProductLabel: $"Visita a propiedad {listing} — {match.StartUtc:yyyy-MM-dd HH:mm}",
                GuestName: contact.Name.Trim(),
                GuestEmail: contact.Email.Trim(),
                TotalPrice: 1m,
                Currency: Cop),
            cancellationToken);

        // 2) Confirmar SIN pago (visita gratis) — sesión neutra "visit-free".
        var confirmed = await _reservations.ConfirmAsync(reservation.Id, FreeVisitPaymentSession, cancellationToken);

        var result = new VisitResult(
            VisitId: "visit_" + confirmed.Id.Replace("resv_", string.Empty, StringComparison.Ordinal),
            Status: confirmed.Status.ToString());

        // TryAdd: si dos requests apartan el mismo slot en carrera, el primero
        // gana y el segundo devuelve su visita (idempotencia efectiva por slot).
        if (!_booked.TryAdd(key, result))
        {
            return _booked[key];
        }
        return result;
    }

    // Agenda sembrada determinista: 6 slots a partir del día siguiente, a las
    // 09:00 y 11:00 de los próximos 3 días. Derivada del id del listado para que
    // GetSlots/Book sean coherentes entre llamadas.
    private IEnumerable<VisitSlot> BuildSlots(string listingId)
    {
        var baseDay = _now().UtcDateTime.Date.AddDays(1);
        var hours = new[] { 9, 11 };
        for (var day = 0; day < 3; day++)
        {
            foreach (var hour in hours)
            {
                var start = new DateTimeOffset(baseDay.AddDays(day).AddHours(hour), TimeSpan.Zero);
                var id = $"{listingId}-{start:yyyyMMddHHmm}";
                yield return new VisitSlot(id, start);
            }
        }
    }

    private static string Key(string listingId, string slotId) => $"{listingId}/{slotId}";
}
