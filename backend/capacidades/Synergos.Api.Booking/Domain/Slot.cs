using Synergos.Core;

namespace Synergos.Api.Booking.Domain;

/// <summary>Una franja de un recurso y cuánto cupo le queda.</summary>
/// <param name="Window">La franja: de la duración del recurso, en su grilla.</param>
/// <param name="Free">Cuántas reservas más admite: la capacidad menos lo que ya la ocupa (apartados vivos y
/// reservas vigentes). Cero: llena.</param>
public sealed record Slot(TimeWindow Window, int Free);

/// <summary>Las franjas de un recurso en un rango (núcleo, NB-2 de 011).</summary>
/// <param name="ResourceId">El recurso.</param>
/// <param name="SlotMinutes">Cuánto dura cada franja.</param>
/// <param name="Capacity">Cuántas reservas admite cada una, vacía.</param>
/// <param name="Items">Las franjas, en orden, también las llenas.</param>
public sealed record SlotList(string ResourceId, int SlotMinutes, int Capacity, IReadOnlyList<Slot> Items);
