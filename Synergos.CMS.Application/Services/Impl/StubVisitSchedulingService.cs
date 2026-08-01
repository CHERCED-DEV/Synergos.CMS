using System.Text.Encodings.Web;
using System.Text.Json;
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
/// Umbraco/AspNetCore (ADR 0002). La agenda se deriva deterministamente del id del
/// listado (slots calculados, no almacenados); apartar un slot lo marca como no
/// disponible para futuras consultas. <see cref="BookAsync"/> es idempotente por
/// (listado, slot): re-agendar el mismo slot ya confirmado devuelve la misma
/// visita sin segundo hold. El adapter real (agenda del agente en un CRM/DB) se
/// enchufa sin tocar el motor. ADR 0075.
///
/// <para><b>Durabilidad.</b> Los slots apartados viven detrás de
/// <see cref="IJsonEntityStore"/> (ADR 0105) y ya no en un diccionario del
/// proceso: un reinicio liberaba visitas que el comprador ya tenía agendadas y
/// que la reserva subyacente seguía dando por confirmadas. La agenda en sí NO se
/// persiste — es una función pura del id del listado y del reloj.</para>
///
/// <para>La familia de entidades es PROPIA de este seam
/// (<c>realty-visits</c>): compartir espacio con leads o búsquedas guardadas
/// haría que un documento se leyera contra la forma de otro dominio, y una
/// deserialización que "casi" encaja no falla — devuelve otra cosa en silencio.</para>
/// </remarks>
public sealed class StubVisitSchedulingService : IVisitSchedulingService
{
    /// <summary>PaymentSessionId neutro: la visita NO se cobra (spec §1).</summary>
    private const string FreeVisitPaymentSession = "visit-free";

    private const string Cop = "COP";

    /// <summary>Familia de entidades en el store genérico (→ App_Data/syn-realty-visits/).</summary>
    private const string DefaultResourceType = "realty-visits";

    private static readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly IReservationService _reservations;
    private readonly Func<DateTimeOffset> _now;
    private readonly IJsonEntityStore _store;
    private readonly string _resourceType;

    // Serializa el read-modify-write de apartar un slot. No se puede usar lock{}
    // porque el cuerpo hace await; SemaphoreSlim es el equivalente async — mismo
    // criterio que StubOrderTrackingService.
    private readonly SemaphoreSlim _mutate = new(1, 1);

    public StubVisitSchedulingService(IReservationService reservations)
        : this(reservations, null)
    {
    }

    /// <summary>
    /// Ctor configurable con time source inyectable (<paramref name="now"/>) para
    /// determinismo en tests (ADR 0002). Null = reloj real.
    /// </summary>
    public StubVisitSchedulingService(IReservationService reservations, Func<DateTimeOffset>? now)
        : this(reservations, now, null, null)
    {
    }

    /// <summary>
    /// Ctor completo: <paramref name="store"/> es el backing store durable
    /// (FileSystem en Web, InMemory en tests) y <paramref name="storeNamespace"/>
    /// separa las visitas de las demás familias del portal.
    /// </summary>
    /// <param name="reservations">El motor de reservas que aparta el slot.</param>
    /// <param name="now">Reloj inyectable para los tests. Null usa el del sistema.</param>
    /// <param name="store">Backing store durable. Null deja las visitas en memoria.</param>
    /// <param name="storeNamespace">Familia de entidades de ESTA instancia.
    ///   Null usa <c>realty-visits</c>. Cambiarlo re-ubica los datos.</param>
    public StubVisitSchedulingService(
        IReservationService reservations,
        Func<DateTimeOffset>? now,
        IJsonEntityStore? store,
        string? storeNamespace)
    {
        _reservations = reservations ?? throw new ArgumentNullException(nameof(reservations));
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _store = store ?? new InMemoryJsonEntityStore();
        _resourceType = string.IsNullOrWhiteSpace(storeNamespace) ? DefaultResourceType : storeNamespace;
    }

    public async Task<IReadOnlyList<VisitSlot>> GetSlotsAsync(string listingId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(listingId))
        {
            return Array.Empty<VisitSlot>();
        }

        var listing = listingId.Trim();
        var slots = new List<VisitSlot>();
        foreach (var slot in BuildSlots(listing))
        {
            var booked = await LoadAsync(Key(listing, slot.Id), cancellationToken).ConfigureAwait(false);
            slots.Add(booked is null ? slot : slot with { Available = false });
        }
        return slots;
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

        // El apartado completo (leer→reservar→escribir) va serializado: antes la
        // carrera la resolvía un TryAdd, que dejaba al perdedor con un hold
        // desperdiciado en el motor de reservas. El contrato observable es el
        // mismo — la misma visita para ambos —, ahora sin la reserva de más.
        await _mutate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Idempotente: el mismo slot ya confirmado devuelve la misma visita.
            var existing = await LoadAsync(key, cancellationToken).ConfigureAwait(false);
            if (existing is not null)
            {
                return existing;
            }

            // El slot debe existir en la agenda derivada del listado.
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

            await _store.WriteAsync(_resourceType, key, JsonSerializer.Serialize(result, _json), cancellationToken)
                .ConfigureAwait(false);
            return result;
        }
        finally
        {
            _mutate.Release();
        }
    }

    /// <summary>
    /// Lee una visita apartada. Un documento corrupto se trata como "slot libre"
    /// en vez de reventar: una visita ilegible no puede tumbar la agenda entera
    /// del listado ni impedir que otro comprador agende.
    /// </summary>
    private async Task<VisitResult?> LoadAsync(string key, CancellationToken cancellationToken)
    {
        var json = await _store.ReadAsync(_resourceType, key, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }
        try
        {
            var visit = JsonSerializer.Deserialize<VisitResult>(json, _json);
            return visit is null || string.IsNullOrWhiteSpace(visit.VisitId) ? null : visit;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // Agenda derivada determinista: 6 slots a partir del día siguiente, a las
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

    // La clave del store reproduce la del diccionario que había antes
    // ("(listado)/(slot)"), en minúsculas porque aquel comparaba con
    // OrdinalIgnoreCase y un sistema de archivos POSIX no. El store sanea la
    // barra; no hay path traversal posible.
    private static string Key(string listingId, string slotId)
        => $"{listingId}/{slotId}".ToLowerInvariant();
}
