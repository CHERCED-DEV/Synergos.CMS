using Synergos.Core;

namespace Synergos.Api.Booking.Domain;

/// <summary>Una reserva vigente que, con el recurso revisado, ya no cabría.</summary>
/// <param name="Reservation">La reserva, que sigue confirmada: revisar no cancela nada.</param>
/// <param name="Reason">Por qué no cabría, con el código de la regla que la dejaría fuera
/// (<c>booking.outside_opening_hours</c>, <c>booking.bad_slot_length</c>, <c>booking.at_capacity</c>…).</param>
public sealed record OutsideReservation(Reservation Reservation, Rejection Reason);

/// <summary>Cómo quedó un recurso revisado (núcleo, NB-7 de 011 y NN-6 de 005).</summary>
/// <param name="Resource">El recurso como está ahora.</param>
/// <param name="Outside">Las reservas vigentes que con él ya no cabrían: lo que alguien tiene que mirar.</param>
public sealed record Revision(Resource Resource, IReadOnlyList<OutsideReservation> Outside);
