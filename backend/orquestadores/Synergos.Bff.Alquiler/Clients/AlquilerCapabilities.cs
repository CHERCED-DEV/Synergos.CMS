using Synergos.Bff.Core;
using Synergos.Core;

namespace Synergos.Bff.Alquiler.Clients;

/// <summary>
/// Las DOS capacidades que Alquiler compone: la ventana sobre el equipo y la plata.
/// </summary>
/// <remarks>
/// <para><b>No hay <c>Api.Inventory</c>, y es una decisión medida.</b> Un equipo que VUELVE es
/// una ventana sobre un recurso, no existencia que se consume: <c>Resource.Capacity</c> ya lleva
/// «cuántas unidades hay» —«1 para un consultorio; 40 para un aula»— y ése es el mismo argumento
/// con que el #36 mandó el hotel a <c>Api.Booking</c>. Meter Inventory pondría dos capacidades a
/// contestar la misma pregunta y dejaría dos colgando de un solo interruptor, que es la huella de
/// haber mirado «cuántos pasos compone» (<c>the_switch_count_tells_the_form</c>).</para>
///
/// <para><b>Un hold es UNA unidad de capacidad</b> —<c>CreateHoldRequest</c> no lleva cantidad—
/// así que alquilar N unidades son N apartados, y la saga los suelta todos.</para>
/// </remarks>
public sealed class AlquilerCapabilities : CapabilityClients
{
    /// <summary>La capacidad que lleva la ventana sobre el equipo.</summary>
    public const string Booking = "booking";

    /// <summary>La capacidad que mueve la plata.</summary>
    public const string Payments = "payments";

    /// <summary>Construye los clientes.</summary>
    /// <param name="clients">La fábrica con la llave compartida y la correlación.</param>
    public AlquilerCapabilities(IHttpClientFactory clients) : base(clients) { }

    /// <summary>El recurso de un equipo, que la capacidad generó.</summary>
    /// <param name="subject">El equipo, como <c>Ref</c>.</param>
    /// <param name="ct">Cancelación.</param>
    /// <returns>El recurso, o el rechazo.</returns>
    public Task<Result<ResourceDto>> FindResourceAsync(Ref subject, CancellationToken ct)
        => Get<ResourceDto>(Booking,
            $"v1/resources?subjectKind={Uri.EscapeDataString(subject.Kind)}&subjectId={Uri.EscapeDataString(subject.Id)}",
            ct);

    /// <summary>Aparta UNA unidad de la ventana.</summary>
    /// <param name="resourceId">El recurso.</param>
    /// <param name="window">La ventana del alquiler.</param>
    /// <param name="renter">Quién alquila, como seudónimo.</param>
    /// <param name="key">La llave de idempotencia.</param>
    /// <param name="ct">Cancelación.</param>
    /// <returns>El apartado, o el rechazo.</returns>
    public Task<Result<HoldDto>> HoldAsync(
        string resourceId, TimeWindow window, Ref renter, IdempotencyKey key, CancellationToken ct)
        => Post<HoldDto>(Booking, "v1/holds", new
        {
            resourceId,
            start = window.Start,
            end = window.End,
            heldForKind = renter.Kind,
            heldForId = renter.Id,
        }, key, ct);

    /// <summary>Suelta un apartado.</summary>
    /// <param name="holdId">Cuál.</param>
    /// <param name="ct">Cancelación.</param>
    /// <returns>El apartado suelto, o el rechazo.</returns>
    public Task<Result<HoldDto>> ReleaseHoldAsync(string holdId, CancellationToken ct)
        => Post<HoldDto>(Booking, $"v1/holds/{holdId}/release", null, null, ct);

    /// <summary>Confirma un apartado y lo vuelve reserva.</summary>
    /// <param name="holdId">Cuál.</param>
    /// <param name="key">La llave de idempotencia.</param>
    /// <param name="ct">Cancelación.</param>
    /// <returns>La reserva, o el rechazo.</returns>
    public Task<Result<ReservationDto>> ConfirmHoldAsync(string holdId, IdempotencyKey key, CancellationToken ct)
        => Post<ReservationDto>(Booking, $"v1/holds/{holdId}/confirm", null, key, ct);

    /// <summary>Cancela una reserva ya confirmada.</summary>
    /// <param name="reservationId">Cuál.</param>
    /// <param name="ct">Cancelación.</param>
    /// <returns>La reserva cancelada, o el rechazo.</returns>
    public Task<Result<ReservationDto>> CancelReservationAsync(string reservationId, CancellationToken ct)
        => Post<ReservationDto>(Booking, $"v1/reservations/{reservationId}/cancel", null, null, ct);

    /// <summary>Autoriza un cobro.</summary>
    /// <param name="forWhat">Sobre qué.</param>
    /// <param name="payer">Quién paga.</param>
    /// <param name="amount">Cuánto.</param>
    /// <param name="key">La llave de idempotencia.</param>
    /// <param name="ct">Cancelación.</param>
    /// <returns>El cobro autorizado, o el rechazo.</returns>
    public Task<Result<PaymentDto>> AuthorizeAsync(
        Ref forWhat, Ref payer, Money amount, IdempotencyKey key, CancellationToken ct)
        => Post<PaymentDto>(Payments, "v1/payments", new
        {
            forKind = forWhat.Kind,
            forId = forWhat.Id,
            payerKind = payer.Kind,
            payerId = payer.Id,
            amount = new { amount = amount.Amount, currency = amount.Currency },
        }, key, ct);

    /// <summary>Captura lo autorizado.</summary>
    /// <param name="paymentId">Cuál.</param>
    /// <param name="key">La llave de idempotencia.</param>
    /// <param name="ct">Cancelación.</param>
    /// <returns>El cobro capturado, o el rechazo.</returns>
    public Task<Result<PaymentDto>> CaptureAsync(string paymentId, IdempotencyKey key, CancellationToken ct)
        => Post<PaymentDto>(Payments, $"v1/payments/{paymentId}/capture", null, key, ct);

    /// <summary>Anula una autorización que nunca se capturó.</summary>
    /// <param name="paymentId">Cuál.</param>
    /// <param name="ct">Cancelación.</param>
    /// <returns>El cobro anulado, o el rechazo.</returns>
    public Task<Result<PaymentDto>> VoidAsync(string paymentId, CancellationToken ct)
        => Post<PaymentDto>(Payments, $"v1/payments/{paymentId}/void", null, null, ct);

    /// <summary>Devuelve parte o todo de un cobro capturado.</summary>
    /// <param name="paymentId">Cuál.</param>
    /// <param name="amount">Cuánto.</param>
    /// <param name="reason">Por qué.</param>
    /// <param name="key">La llave de idempotencia.</param>
    /// <param name="ct">Cancelación.</param>
    /// <returns>El cobro con su devolución, o el rechazo.</returns>
    public Task<Result<PaymentDto>> RefundAsync(
        string paymentId, Money amount, string reason, IdempotencyKey key, CancellationToken ct)
        => Post<PaymentDto>(Payments, $"v1/payments/{paymentId}/refund", new
        {
            amount = new { amount = amount.Amount, currency = amount.Currency },
            reason,
        }, key, ct);

    /// <summary>Un cobro tal como está ahora.</summary>
    /// <param name="paymentId">Cuál.</param>
    /// <param name="ct">Cancelación.</param>
    /// <returns>El cobro, o el rechazo.</returns>
    public Task<Result<PaymentDto>> GetPaymentAsync(string paymentId, CancellationToken ct)
        => Get<PaymentDto>(Payments, $"v1/payments/{paymentId}", ct);
}

/// <summary>Dinero, como lo emiten las capacidades.</summary>
/// <param name="Amount">El monto.</param>
/// <param name="Currency">La moneda.</param>
public sealed record MoneyDto(decimal Amount, string Currency);

/// <summary>Un recurso de <c>Api.Booking</c>.</summary>
/// <param name="Id">Su identificador, que generó la capacidad.</param>
/// <param name="SubjectKind">El tipo del sujeto.</param>
/// <param name="SubjectId">El sujeto.</param>
/// <param name="Capacity">Cuántas unidades admite a la vez.</param>
public sealed record ResourceDto(string Id, string SubjectKind, string SubjectId, int Capacity);

/// <summary>Un apartado de <c>Api.Booking</c>.</summary>
/// <param name="Id">Su identificador.</param>
/// <param name="ResourceId">Sobre qué recurso.</param>
/// <param name="ExpiresAt">Cuándo vence solo — diez minutos por defecto, medido.</param>
public sealed record HoldDto(string Id, string ResourceId, DateTimeOffset ExpiresAt);

/// <summary>Una reserva confirmada.</summary>
/// <param name="Id">Su identificador.</param>
/// <param name="Status">En qué estado está.</param>
public sealed record ReservationDto(string Id, string Status);

/// <summary>
/// Un cobro de <c>Api.Payments</c>.
/// </summary>
/// <remarks>
/// <b><c>Refundable</c> se declara y hace falta</b>: al devolver hay que saber cuánto queda por
/// devolver de la garantía capturada, y rellenarlo con cero sería la fabricación del #122 —
/// omitir un campo es inocente hasta que alguien tiene que llenar el hueco.
/// </remarks>
/// <param name="Id">Su identificador.</param>
/// <param name="Status">En qué estado está.</param>
/// <param name="Amount">Por cuánto.</param>
/// <param name="Refundable">Cuánto queda por devolver.</param>
public sealed record PaymentDto(string Id, string Status, MoneyDto Amount, MoneyDto Refundable);
