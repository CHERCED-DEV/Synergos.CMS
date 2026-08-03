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

/// <summary>Una franja de apertura tal como llega.</summary>
public sealed record OpeningRuleDto(string? Day, string? From, string? To);

/// <summary>Registrar un recurso reservable.</summary>
public sealed record CreateResourceRequest(
    string? SubjectKind,
    string? SubjectId,
    int? Capacity,
    string? TimeZoneId,
    IReadOnlyList<OpeningRuleDto>? Opening,
    int? CancellationNoticeMinutes);

/// <summary>Tomar un bloqueo temporal sobre una ventana.</summary>
public sealed record CreateHoldRequest(
    string? ResourceId,
    DateTimeOffset? Start,
    DateTimeOffset? End,
    string? HeldForKind,
    string? HeldForId,
    int? TtlMinutes);

/// <summary>Cómo sale un recurso.</summary>
public sealed record ResourceResponse(
    string Id,
    string SubjectKind,
    string SubjectId,
    int Capacity,
    string TimeZoneId,
    IReadOnlyList<OpeningRuleDto> Opening,
    int CancellationNoticeMinutes)
{
    public static ResourceResponse From(Resource r) => new(
        r.Id, r.Subject.Kind, r.Subject.Id, r.Capacity, r.TimeZoneId,
        r.Opening.Select(o => new OpeningRuleDto(o.Day.ToString(), o.From.ToString("HH:mm"), o.To.ToString("HH:mm"))).ToList(),
        (int)r.Policy.MinNoticeBeforeStart.TotalMinutes);
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

/// <summary>Una porción de una lista, con su total.</summary>
public sealed record PageResponse<T>(IReadOnlyList<T> Items, int Total, int Offset, bool HasMore);
