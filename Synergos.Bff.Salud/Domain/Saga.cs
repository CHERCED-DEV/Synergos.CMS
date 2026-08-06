using Synergos.Bff.Core;
using Synergos.Core;

namespace Synergos.Bff.Salud.Domain;

/// <summary>
/// Lo que Salud sabe deshacer.
/// </summary>
/// <remarks>
/// <b>Constantes y no un enum, porque el <c>Kind</c> de una compensación es un <c>string</c> en
/// <see cref="Compensation"/>.</b> Y eso es deliberado: un enum en <c>Bff.Core</c> tendría que
/// enumerar lo que los ocho dominios saben deshacer, y el sitio compartido pasaría a tocarse cada
/// vez que nace un dominio.
/// </remarks>
public static class SaludCompensations
{
    /// <summary>Soltar un hold de <c>Api.Booking</c>.</summary>
    public const string ReleaseBookingHold = "ReleaseBookingHold";

    /// <summary>Liberar una autorización de <c>Api.Payments</c> que nunca se capturó.</summary>
    public const string VoidPayment = "VoidPayment";

    /// <summary>Devolver plata ya capturada.</summary>
    public const string RefundPayment = "RefundPayment";
}

/// <summary>
/// Una cita que cruza capacidades, con lo que haya que deshacer si falla.
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
/// <param name="AlertedAtUtc">Cuándo se avisó a una persona de que algo quedó colgado.</param>
/// <param name="AlertsSent">Cuántos avisos salieron.</param>
/// <remarks>
/// <b>Lo que este record aporta y <c>Bff.Core</c> no puede aportar</b> son los seis campos del
/// medio: el paciente, el profesional, la ventana y los identificadores de cada capacidad. El
/// motor de sagas no los ve ni los necesita — solo pide <see cref="ISaga{TSelf}"/>, que son seis
/// campos y tres mutadores. Si algún día hiciera falta subir uno de estos a la interfaz, es señal
/// de que se está metiendo negocio en la capa compartida.
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
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? AlertedAtUtc = null,
    int AlertsSent = 0) : ISaga<AppointmentSaga>
{
    public AppointmentSaga WithStatus(SagaStatus status) => this with { Status = status };

    public AppointmentSaga WithCompensations(IReadOnlyList<Compensation> compensations)
        => this with { Compensations = compensations };

    public AppointmentSaga WithAlert(DateTimeOffset? alertedAtUtc, int alertsSent)
        => this with { AlertedAtUtc = alertedAtUtc, AlertsSent = alertsSent };
}
