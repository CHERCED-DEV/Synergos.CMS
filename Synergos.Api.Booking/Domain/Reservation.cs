using Synergos.Core;

namespace Synergos.Api.Booking.Domain;

/// <summary>En qué estado está una reserva.</summary>
public enum ReservationStatus
{
    /// <summary>Vigente y ocupando cupo.</summary>
    Confirmed,

    /// <summary>Cancelada. Deja de ocupar cupo, pero <b>no se borra</b>.</summary>
    Cancelled,
}

/// <summary>
/// Una reserva confirmada sobre una ventana de un recurso.
/// </summary>
/// <param name="Id">Identificador de la reserva.</param>
/// <param name="ResourceId">Sobre qué recurso.</param>
/// <param name="HoldId">De qué hold salió — la trazabilidad del flujo.</param>
/// <param name="Window">Qué ventana ocupa.</param>
/// <param name="For">Para quién — opaco.</param>
/// <param name="ConfirmedAt">Cuándo se confirmó.</param>
/// <param name="Status">Vigente o cancelada.</param>
/// <param name="CancelledAt">Cuándo se canceló, si aplica.</param>
/// <remarks>
/// <b>Cancelar no borra.</b> Una reserva cancelada se conserva con su estado porque "nunca
/// existió" y "existió y se canceló" son cosas distintas para quien reclama, para quien audita y
/// para quien decide si devolver plata. Borrar la fila hace imposible contestar cuál de las dos
/// fue.
/// </remarks>
public sealed record Reservation(
    string Id,
    string ResourceId,
    string HoldId,
    TimeWindow Window,
    Ref For,
    DateTimeOffset ConfirmedAt,
    ReservationStatus Status = ReservationStatus.Confirmed,
    DateTimeOffset? CancelledAt = null)
{
    /// <summary>Si ocupa cupo.</summary>
    public bool IsActive => Status == ReservationStatus.Confirmed;
}
