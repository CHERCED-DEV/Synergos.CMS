using Synergos.Bff.Core;
using Synergos.Bff.Viajes.Domain;

namespace Synergos.Bff.Viajes.Contracts;

// Lo que cruza el cable, separado de Domain/ (doc 08 §4.1). Acá la separación gana algo muy
// concreto: la saga lleva los identificadores internos de cada capacidad —los apartados, las
// reservas, el pago— y los intentos de cada compensación. Nada de eso tiene por qué salir.

/// <summary>Un monto tal como sale.</summary>
public sealed record MoneyDto(decimal Amount, string Currency);

/// <summary>Un ítem pedido: qué producto y qué periodo ocupa.</summary>
/// <remarks>
/// <b>Sin precio, a propósito.</b> Si el total llegara del llamador, cualquiera reservaría la
/// suite al precio de la estándar. Se cotiza contra <c>Api.Pricing</c>.
/// </remarks>
public sealed record TripItemRequest(
    string? ProductRef, string? ProductLabel, DateTimeOffset? Start, DateTimeOffset? End);

/// <summary>Reservar un viaje.</summary>
public sealed record BookTripRequest(
    string? TravellerKind, string? TravellerId, IReadOnlyList<TripItemRequest>? Items);

/// <summary>Un ítem apartado, tal como sale.</summary>
/// <param name="ProductRef">Qué producto.</param>
/// <param name="ProductLabel">Cómo se llama en pantalla.</param>
/// <param name="Start">Desde.</param>
/// <param name="End">Hasta.</param>
/// <param name="Confirmed">Si el apartado ya es una reserva.</param>
/// <remarks>
/// <b>Sin el identificador del apartado, del recurso ni de la reserva.</b> Los tres son internos
/// de <c>Api.Booking</c> y quien pregunta no puede hacer nada con ellos — sacarlos solo invita a
/// que alguien los cablee río arriba, que es el error que costó una vuelta en la HU #25.
/// </remarks>
public sealed record HeldItemResponse(
    string ProductRef, string ProductLabel, DateTimeOffset Start, DateTimeOffset End, bool Confirmed);

/// <summary>Cómo sale un viaje.</summary>
public sealed record TripResponse(
    string Id, string TravellerKind, string TravellerId, string Status, MoneyDto Total,
    IReadOnlyList<HeldItemResponse> Items, int PendingCompensations, string? LastError)
{
    public static TripResponse From(TripSaga s) => new(
        s.Id, s.Traveller.Kind, s.Traveller.Id, s.Status.ToString(),
        new MoneyDto(s.Total.Amount, s.Total.Currency),
        s.Holds.Select(h => new HeldItemResponse(
            h.ProductRef, h.ProductLabel, h.Window.Start, h.Window.End, h.ReservationId is not null)).ToList(),
        s.Pending().Count, s.LastError);
}

/// <summary>Una compensación pendiente, para quien vigila.</summary>
/// <param name="TripId">El viaje al que pertenece.</param>
/// <param name="Kind">Qué hay que deshacer.</param>
/// <param name="Reason">Por qué.</param>
/// <param name="Attempts">Cuántas veces se intentó.</param>
/// <param name="NextAttemptUtc">Cuándo toca el próximo intento.</param>
/// <param name="LastError">Qué dijo el último fallo.</param>
/// <param name="Stuck">Se rindió: el barrido ya no la toca hasta que una persona pida reintento.</param>
/// <param name="AlertedAtUtc">Cuándo se avisó a la guardia. <b>Nulo con <c>Stuck</c> en cierto
/// significa que se rindió y nadie fue avisado</b> — la fila más urgente de esta lista.</param>
public sealed record PendingCompensationResponse(
    string TripId, string Kind, string Reason, int Attempts,
    DateTimeOffset? NextAttemptUtc, string? LastError, bool Stuck, DateTimeOffset? AlertedAtUtc);

/// <summary>Una porción de una lista, con su total.</summary>
public sealed record PageResponse<T>(IReadOnlyList<T> Items, int Total, int Offset, bool HasMore);
