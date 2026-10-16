using Synergos.Api.Booking.Domain;

namespace Synergos.Api.Booking.Contracts;

// ─────────────────────────────────────────────────────────────────────────────
// Lo que cruza el cable. SEPARADO de Domain/ a propósito (doc 08 §4.1): es lo que
// permite cambiar el modelo interno sin romper a los clientes. Fusionarlos es
// cómodo el primer mes y carísimo el segundo, porque cada renombre interno pasa a
// ser un cambio de contrato.
//
// Nullable en todo lo que llega: si un campo obligatorio fuera no-nullable, el
// binder de ASP.NET devolvería un 400 genérico ANTES de que la API pueda explicar
// qué falta — y el llamador se queda sin saber cuál de siete campos era.
// ─────────────────────────────────────────────────────────────────────────────

// En lo que llega en el CUERPO de una petición, todo anulable lleva `= null` (el molde F2 de la ADR
// 0140, núcleo N1): no cambia cómo se liga —si falta el campo, System.Text.Json ya ponía null—, cambia
// lo que se publica. Sin él, ASP.NET marca `required` todo parámetro posicional y el contrato exigiría
// campos que el dominio no exige (lo vigila el suelo del contrato).

/// <summary>Una franja de apertura tal como llega.</summary>
public sealed record OpeningRuleDto(string? Day = null, string? From = null, string? To = null);

/// <summary>Registrar un recurso reservable.</summary>
public sealed record CreateResourceRequest(
    string? SubjectKind = null,
    string? SubjectId = null,
    int? Capacity = null,
    string? TimeZoneId = null,
    IReadOnlyList<OpeningRuleDto>? Opening = null,
    int? CancellationNoticeMinutes = null,
    int? SlotMinutes = null);

/// <summary>Tomar un bloqueo temporal sobre una ventana.</summary>
public sealed record CreateHoldRequest(
    string? ResourceId = null,
    DateTimeOffset? Start = null,
    DateTimeOffset? End = null,
    string? HeldForKind = null,
    string? HeldForId = null,
    int? TtlMinutes = null);

/// <summary>Cómo sale un recurso.</summary>
/// <remarks><c>SlotMinutes</c> nulo: el recurso no se reserva por franjas.</remarks>
public sealed record ResourceResponse(
    string Id,
    string SubjectKind,
    string SubjectId,
    int Capacity,
    string TimeZoneId,
    IReadOnlyList<OpeningRuleDto> Opening,
    int CancellationNoticeMinutes,
    int? SlotMinutes)
{
    public static ResourceResponse From(Resource r) => new(
        r.Id, r.Subject.Kind, r.Subject.Id, r.Capacity, r.TimeZoneId,
        r.Opening.Select(o => new OpeningRuleDto(o.Day.ToString(), o.From.ToString("HH:mm"), o.To.ToString("HH:mm"))).ToList(),
        (int)r.Policy.MinNoticeBeforeStart.TotalMinutes,
        r.SlotMinutes);
}

/// <summary>Cómo sale un hold.</summary>
public sealed record HoldResponse(
    string Id,
    string ResourceId,
    DateTimeOffset Start,
    DateTimeOffset End,
    string HeldForKind,
    string HeldForId,
    DateTimeOffset ExpiresAt,
    bool Released,
    string? ReservationId)
{
    public static HoldResponse From(Hold h) => new(
        h.Id, h.ResourceId, h.Window.Start, h.Window.End,
        h.HeldFor.Kind, h.HeldFor.Id, h.ExpiresAt, h.Released, h.ConfirmedReservationId);
}

/// <summary>Cómo sale una reserva.</summary>
public sealed record ReservationResponse(
    string Id,
    string ResourceId,
    string HoldId,
    DateTimeOffset Start,
    DateTimeOffset End,
    string ForKind,
    string ForId,
    DateTimeOffset ConfirmedAt,
    string Status,
    DateTimeOffset? CancelledAt)
{
    public static ReservationResponse From(Reservation r) => new(
        r.Id, r.ResourceId, r.HoldId, r.Window.Start, r.Window.End,
        r.For.Kind, r.For.Id, r.ConfirmedAt, r.Status.ToString(), r.CancelledAt);
}

/// <summary>
/// Si una ventana concreta se puede tomar.
/// </summary>
/// <remarks>
/// Lleva <see cref="ReasonCode"/> además de <see cref="Available"/> porque "no" tiene causas
/// distintas —fuera de horario, sin cupo, en el pasado— y sin el código el cliente solo puede
/// mostrar "no disponible" a un usuario que no sabe qué cambiar.
/// </remarks>
public sealed record AvailabilityResponse(
    bool Available,
    int Taken,
    int Capacity,
    string? ReasonCode,
    string? Reason);

/// <summary>Una franja y cuánto cupo le queda.</summary>
public sealed record SlotResponse(DateTimeOffset Start, DateTimeOffset End, int Free);

/// <summary>
/// Las franjas de un recurso en un rango, también las llenas (<c>free</c> en cero): la pantalla decide si
/// las enseña.
/// </summary>
public sealed record SlotListResponse(string ResourceId, int SlotMinutes, int Capacity, IReadOnlyList<SlotResponse> Items)
{
    public static SlotListResponse From(SlotList l) => new(
        l.ResourceId, l.SlotMinutes, l.Capacity,
        l.Items.Select(s => new SlotResponse(s.Window.Start, s.Window.End, s.Free)).ToList());
}

/// <summary>Una porción de una lista, con su total.</summary>
public sealed record PageResponse<T>(IReadOnlyList<T> Items, int Total, int Offset, bool HasMore);
