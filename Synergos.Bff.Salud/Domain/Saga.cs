using Synergos.Core;

namespace Synergos.Bff.Salud.Domain;

/// <summary>Qué hay que deshacer, si hay que deshacerlo.</summary>
/// <remarks>
/// <b>Es un dato, no una función.</b> Una compensación tiene que poder ejecutarse <i>después</i>
/// —desde otro proceso, tras un reinicio, horas más tarde— y un <c>Action</c> en memoria no
/// sobrevive a nada de eso. Guardar la intención como dato es lo que permite que un barrido de
/// fondo la reintente.
/// </remarks>
public enum CompensationKind
{
    /// <summary>Soltar un hold de <c>Api.Booking</c>.</summary>
    ReleaseBookingHold,

    /// <summary>Liberar una autorización de <c>Api.Payments</c> que nunca se capturó.</summary>
    VoidPayment,

    /// <summary>Devolver plata ya capturada.</summary>
    RefundPayment,
}

/// <summary>Una compensación pendiente o ya hecha.</summary>
/// <param name="Id">Identificador dentro de la saga.</param>
/// <param name="Kind">Qué deshacer.</param>
/// <param name="TargetId">Sobre qué identificador de la capacidad.</param>
/// <param name="Reason">Por qué. Va al <c>Reason</c> de la devolución, para conciliar.</param>
/// <param name="Attempts">Cuántas veces se intentó.</param>
/// <param name="NextAttemptUtc">Cuándo toca el próximo intento.</param>
/// <param name="LastError">Qué dijo el último fallo.</param>
/// <param name="DoneAtUtc">Cuándo se completó, si se completó.</param>
public sealed record Compensation(
    string Id,
    CompensationKind Kind,
    string TargetId,
    string Reason,
    int Attempts = 0,
    DateTimeOffset? NextAttemptUtc = null,
    string? LastError = null,
    DateTimeOffset? DoneAtUtc = null)
{
    public bool IsPending => DoneAtUtc is null;
}

/// <summary>En qué punto está el flujo.</summary>
public enum SagaStatus
{
    /// <summary>Cupo tomado y cobro autorizado; falta confirmar.</summary>
    AwaitingConfirmation,

    /// <summary>Salió todo. Estado final.</summary>
    Confirmed,

    /// <summary>Algo falló y hay compensaciones por hacer.</summary>
    Compensating,

    /// <summary>Se deshizo todo lo que había que deshacer. Estado final.</summary>
    Compensated,

    /// <summary>Se deshizo lo que se pudo y algo quedó colgado. <b>Necesita una persona.</b></summary>
    CompensationFailed,
}

/// <summary>
/// Un flujo que cruza capacidades, con lo que haya que deshacer si falla.
/// </summary>
/// <param name="Id">Identificador. <b>Semilla de todas las llaves de idempotencia.</b></param>
/// <param name="Patient">Para quién — viaja opaco a las capacidades.</param>
/// <param name="Professional">Con quién.</param>
/// <param name="Window">Cuándo.</param>
/// <param name="Status">En qué punto está.</param>
/// <param name="HoldId">El hold de Booking.</param>
/// <param name="PaymentId">El cobro de Payments, si hubo copago.</param>
/// <param name="ReservationId">La reserva, si se confirmó.</param>
/// <param name="Total">Cuánto se cobra.</param>
/// <param name="Compensations">Lo que hay que deshacer.</param>
/// <param name="LastError">Qué falló, para que el llamador lo lea.</param>
/// <param name="StartedAtUtc">Cuándo arrancó.</param>
/// <remarks>
/// <para><b>El identificador de la saga es la semilla de las llaves de idempotencia</b>, y esa
/// es la decisión que hace recuperable todo lo demás. Con llaves deterministas
/// —<c>{sagaId}:capture</c>, <c>{sagaId}:refund</c>— <b>reintentar un paso ES la recuperación</b>:
/// la capacidad reconoce la llave y devuelve lo que ya hizo, en vez de hacerlo otra vez. Sin
/// eso, tras una caída entre "cobré" y "lo anoté" no habría manera de averiguar si el cobro
/// salió — y las capacidades no exponen "búscame por llave".</para>
/// </remarks>
public sealed record AppointmentSaga(
    string Id,
    Ref Patient,
    Ref Professional,
    TimeWindow Window,
    SagaStatus Status,
    string? HoldId,
    string? PaymentId,
    string? ReservationId,
    Money Total,
    IReadOnlyList<Compensation> Compensations,
    string? LastError,
    DateTimeOffset StartedAtUtc)
{
    public IReadOnlyList<Compensation> Pending => Compensations.Where(c => c.IsPending).ToList();

    /// <summary>La llave de idempotencia de un paso. Determinista: reintentar es recuperar.</summary>
    public IdempotencyKey KeyFor(string step) => IdempotencyKey.From(Id, step);
}
