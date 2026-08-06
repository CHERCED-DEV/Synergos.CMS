using Synergos.Bff.Core;
using Synergos.Bff.Salud.Domain;

namespace Synergos.Bff.Salud.Contracts;

// Lo que cruza el cable, separado de Domain/ (doc 08 §4.1). Acá la separación gana algo muy
// concreto: la saga lleva los identificadores internos de cada capacidad —el hold, el pago— y
// los intentos de cada compensación. Nada de eso tiene por qué salir a la UI.

/// <summary>Un monto tal como sale.</summary>
public sealed record MoneyDto(decimal Amount, string Currency);

/// <summary>Agendar una cita.</summary>
public sealed record ScheduleRequest(
    string? PatientKind, string? PatientId,
    string? ProfessionalKind, string? ProfessionalId,
    string? ResourceId,
    string? ServiceKind, string? ServiceId,
    DateTimeOffset? Start, DateTimeOffset? End);

/// <summary>Cómo sale una cita.</summary>
public sealed record AppointmentResponse(
    string Id, string PatientKind, string PatientId, string ProfessionalKind, string ProfessionalId,
    DateTimeOffset Start, DateTimeOffset End, string Status, MoneyDto Total,
    string? ReservationId, int PendingCompensations, string? LastError)
{
    public static AppointmentResponse From(AppointmentSaga s) => new(
        s.Id, s.Patient.Kind, s.Patient.Id, s.Professional.Kind, s.Professional.Id,
        s.Window.Start, s.Window.End, s.Status.ToString(),
        new MoneyDto(s.Total.Amount, s.Total.Currency),
        s.ReservationId, s.Pending().Count, s.LastError);
}

/// <summary>Una compensación pendiente, para quien vigila.</summary>
/// <param name="AppointmentId">La cita a la que pertenece.</param>
/// <param name="Kind">Qué hay que deshacer.</param>
/// <param name="Reason">Por qué.</param>
/// <param name="Attempts">Cuántas veces se intentó.</param>
/// <param name="NextAttemptUtc">Cuándo toca el próximo intento.</param>
/// <param name="LastError">Qué dijo el último fallo.</param>
/// <param name="Stuck">Se rindió: el barrido ya no la toca hasta que una persona pida reintento.</param>
/// <param name="AlertedAtUtc">Cuándo se avisó a la guardia. <b>Nulo con <c>Stuck</c> en cierto
/// significa que se rindió y nadie fue avisado</b> — la fila más urgente de esta lista.</param>
public sealed record PendingCompensationResponse(
    string AppointmentId, string Kind, string Reason, int Attempts,
    DateTimeOffset? NextAttemptUtc, string? LastError, bool Stuck, DateTimeOffset? AlertedAtUtc);

/// <summary>Una porción de una lista, con su total.</summary>
public sealed record PageResponse<T>(IReadOnlyList<T> Items, int Total, int Offset, bool HasMore);
