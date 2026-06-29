namespace Synergos.CMS.Interfaces;

/// <summary>
/// Un slot de visita disponible de un listado (la agenda del agente). Es la
/// unidad reservable del flujo "agendar visita": el comprador elige uno y se
/// aparta vía el motor de reservas. <see cref="StartUtc"/> es el instante de
/// inicio; <see cref="Available"/> indica si sigue libre (false = ya apartado/
/// confirmado por otra visita).
/// </summary>
public sealed record VisitSlot(
    string Id,
    DateTimeOffset StartUtc,
    bool Available = true);

/// <summary>
/// Datos de contacto del interesado que agenda la visita (caso degenerado del
/// "huésped" del motor: nombre + email + teléfono, sin pax/ocupación).
/// </summary>
public sealed record VisitContact(
    string Name,
    string Email,
    string? Phone = null);

/// <summary>
/// Resultado de agendar una visita: el id de la cita + su estado. El estado
/// refleja el ciclo del recurso reservable (held mientras se confirma →
/// <c>Confirmed</c> tras apartar+confirmar, SIN pago — la visita es gratis).
/// </summary>
public sealed record VisitResult(string VisitId, string Status);

/// <summary>
/// Servicio de agendamiento de visitas del vertical Propiedades. Es el sub-flujo
/// transaccional de la PDP inmobiliaria (panel sticky del agente, doc
/// propiedades-app-spec §2): <see cref="GetSlotsAsync"/> lista la agenda del
/// agente para un listado; <see cref="BookAsync"/> aparta un slot y confirma la
/// visita.
/// </summary>
/// <remarks>
/// REUSA EL MOTOR (no reinventa): la visita es un RECURSO RESERVABLE POLIMÓRFICO
/// (igual que habitación≈asiento≈médico). <see cref="BookAsync"/> llama
/// <see cref="IReservationService.HoldItemAsync"/> para apartar el slot (con
/// hold-timeout) y luego <see cref="IReservationService.ConfirmAsync"/> para
/// confirmar — pero <strong>SIN paso de pago</strong> (la visita es gratis): se
/// confirma con un PaymentSessionId neutro <c>visit-free</c>, validando la
/// generalidad del flujo <c>seleccionar→[pagar]→confirmar</c> con el paso de pago
/// desactivado (spec §1). El default <c>StubVisitSchedulingService</c>
/// (Application, lógica pura) siembra la agenda en memoria y marca el slot
/// apartado como no disponible; <see cref="BookAsync"/> es idempotente por slot
/// (re-agendar el mismo slot ya confirmado devuelve la misma visita). ADR 0002 +
/// ADR 0075.
/// </remarks>
public interface IVisitSchedulingService
{
    /// <summary>
    /// Devuelve los slots de visita de un listado (la agenda del agente).
    /// Vacío si el listado no existe o no tiene agenda. Los ya apartados vienen
    /// con <see cref="VisitSlot.Available"/> = false.
    /// </summary>
    Task<IReadOnlyList<VisitSlot>> GetSlotsAsync(string listingId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Aparta el slot indicado vía el motor de reservas (HoldItem → Confirm, sin
    /// pago) y devuelve la visita confirmada. Lanza <see cref="ArgumentException"/>
    /// si el listado/slot no existe; <see cref="InvalidOperationException"/> si el
    /// slot ya no está disponible. Idempotente por slot.
    /// </summary>
    Task<VisitResult> BookAsync(string listingId, string slot, VisitContact contact, CancellationToken cancellationToken = default);
}
