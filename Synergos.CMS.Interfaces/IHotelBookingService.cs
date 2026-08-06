namespace Synergos.CMS.Interfaces;

/// <summary>
/// Cómo terminó un intento de cobro sobre una reserva.
/// </summary>
/// <param name="Reservation">La reserva, con su estado tras el intento.</param>
/// <param name="PaymentStatus">Qué dijo el medio de pago.</param>
/// <param name="PaymentSessionId">La sesión abierta, o <c>null</c> si no se llegó a abrir.</param>
/// <param name="AmountCaptured">Cuánto se capturó de verdad.</param>
/// <param name="FailureReason">Por qué no salió, para que el huésped pueda hacer algo.</param>
/// <param name="Outcome">Cómo tiene que contestarle el borde a quien preguntó.</param>
public sealed record HotelPaymentResult(
    Reservation Reservation,
    PaymentStatus PaymentStatus,
    string? PaymentSessionId,
    decimal AmountCaptured,
    string? FailureReason,
    HotelPaymentOutcome Outcome);

/// <summary>
/// Los cuatro finales posibles de un cobro, <b>sin hablar de HTTP</b>.
/// </summary>
/// <remarks>
/// <para>Hace falta porque los tres se ven distintos desde afuera y confundirlos ya costó caro
/// una vez: un hold vencido NO es una petición mal formada, es un conflicto con el estado del
/// recurso, y un cobro que pide una acción adicional del cliente (PSE, 3DS, Nequi) tampoco es un
/// fallo — es un paso más. Quien traduce esto a códigos de estado es el borde; acá solo se dice
/// cuál de los cuatro ocurrió.</para>
///
/// <para><b>Y por eso es un enum y no un <c>bool</c>:</b> «salió / no salió» perdía justo la
/// distinción que el defecto enseñó.</para>
/// </remarks>
public enum HotelPaymentOutcome
{
    /// <summary>Capturado ahora, y reserva confirmada.</summary>
    Confirmed,

    /// <summary>
    /// Ya estaba confirmada: <b>esta llamada no movió nada</b>.
    /// </summary>
    /// <remarks>
    /// <b>Es distinto de <see cref="Confirmed"/> y hay que poder distinguirlo</b>, aunque el
    /// resultado se parezca. El borde de hoy responde a este caso con la forma de una RESERVA y
    /// no con la de un cobro —una verruga de contrato que la UI ya consume, documentada en sus
    /// tests—, y colapsar los dos casos la cambiaría en silencio. Arreglarla es un cambio de API
    /// con su propio ticket, no un efecto colateral de mover código de sitio.
    /// </remarks>
    AlreadyConfirmed,

    /// <summary>No se cobró, y la razón es del cliente o del riel. Se puede reintentar.</summary>
    NotCaptured,

    /// <summary>La reserva no admite cobro: cancelada, o con el apartado vencido.</summary>
    Conflict,
}

/// <summary>Cómo terminó una cancelación, con lo que la política decidió.</summary>
/// <param name="Reservation">La reserva ya cancelada.</param>
/// <param name="Refundable">Si la política admite devolución.</param>
/// <param name="PenaltyAmount">Cuánto se queda el hotel.</param>
/// <param name="PolicyDescription">La política, en palabras, para mostrarla.</param>
/// <param name="RefundStatus">
/// Qué dijo el medio de pago, o <c>null</c> si <b>esta pasada</b> no movió dinero.
/// </param>
/// <remarks>
/// <b><see cref="RefundStatus"/> nulo no significa «no se devolvió nada»</b>, significa «acá no
/// se devolvió nada». Afirmar un reembolso que no ocurrió en esta llamada es la clase de dato con
/// cara de verdad que ya costó una vez en este flujo.
/// </remarks>
public sealed record HotelCancellationResult(
    Reservation Reservation,
    bool Refundable,
    decimal PenaltyAmount,
    string PolicyDescription,
    string? RefundStatus);

/// <summary>
/// El flujo transaccional de una reserva de hotel: apartar → cobrar → confirmar, o cancelar.
/// </summary>
/// <remarks>
/// <para><b>Existe porque esto vivía dentro de un controller de ASP.NET</b>, y ahí no se podía
/// probar sin levantar el pipeline ni cambiar por dónde se reserva sin reescribir el borde. Son
/// unas doscientas líneas que deciden en qué orden se abre la caja, y que llevan dentro dos
/// defectos ya corregidos —el apartado vencido y la doble devolución— que ningún test cubría
/// porque no había dónde ponerlos.</para>
///
/// <para><b>Lo que queda del otro lado de la costura</b> es lo que sí es del borde: validar la
/// petición, formatear precios en es-CO y elegir el código de estado. Nada de eso decide si se
/// cobra.</para>
///
/// <para><b>Y lo que la costura hace posible</b> (HU #36): que la misma reserva se pueda llevar
/// contra <c>Synergos.Bff.Viajes</c> sin tocar el borde. Apartar, cobrar y confirmar es
/// exactamente una saga con compensación, y el CMS no tiene dónde anotar una compensación
/// pendiente — hoy, si la confirmación falla tras capturar, no hay nada que devuelva la plata.
/// </para>
/// </remarks>
public interface IHotelBookingService
{
    /// <summary>Aparta el cupo. Lanza <see cref="ArgumentException"/> si la petición no es válida.</summary>
    Task<Reservation> HoldAsync(ReservationRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cobra la reserva y la confirma si el cobro sale.
    /// </summary>
    /// <remarks>
    /// <b>El cupo se comprueba ANTES de abrir la caja.</b> Cobrar y después descubrir que el
    /// apartado venció es el orden equivocado: deja al huésped cobrado, sin reserva y sin nada
    /// que compense. Devuelve <c>null</c> si la reserva no existe.
    /// </remarks>
    Task<HotelPaymentResult?> PayAsync(string reservationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancela la reserva, aplica la política y devuelve lo que corresponda.
    /// </summary>
    /// <remarks>
    /// <b>Idempotente</b>: cancelar lo ya cancelado no devuelve dos veces. Devuelve <c>null</c>
    /// si la reserva no existe.
    /// </remarks>
    Task<HotelCancellationResult?> CancelAsync(
        string reservationId, string? reason, CancellationToken cancellationToken = default);

    /// <summary>La reserva por su identificador, o <c>null</c>.</summary>
    Task<Reservation?> GetAsync(string reservationId, CancellationToken cancellationToken = default);
}
