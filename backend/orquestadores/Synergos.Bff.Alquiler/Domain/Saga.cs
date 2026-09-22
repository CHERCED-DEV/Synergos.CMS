using Synergos.Bff.Core;
using Synergos.Core;

namespace Synergos.Bff.Alquiler.Domain;

/// <summary>Lo que hay que deshacer, por nombre.</summary>
public static class AlquilerCompensations
{
    /// <summary>Soltar un apartado que todavía no es reserva.</summary>
    public const string ReleaseBookingHold = "ReleaseBookingHold";

    /// <summary>Cancelar una reserva ya confirmada.</summary>
    public const string CancelReservation = "CancelReservation";

    /// <summary>Anular una autorización que nunca se capturó — el caso normal de la garantía.</summary>
    public const string VoidPayment = "VoidPayment";

    /// <summary>Devolver lo que ya se capturó.</summary>
    public const string RefundPayment = "RefundPayment";
}

/// <summary>Una unidad apartada, con su reserva cuando se confirma.</summary>
/// <param name="HoldId">El apartado.</param>
/// <param name="ResourceId">Sobre qué recurso.</param>
/// <param name="ReservationId">La reserva, una vez confirmada.</param>
public sealed record UnitHold(string HoldId, string ResourceId, string? ReservationId = null);

/// <summary>
/// Un alquiler visto por el orquestador: la ventana apartada y las DOS plata.
/// </summary>
/// <remarks>
/// <para><b>Dos cobros con vidas distintas, y ésa es la forma que este vertical estrena.</b>
/// <see cref="RentalPaymentId"/> se autoriza y se CAPTURA al reservar: es lo que se cobra.
/// <see cref="DepositPaymentId"/> se autoriza y <b>se queda sin capturar</b> mientras el equipo
/// está fuera; al devolver se anula —el caso normal— o se captura lo que el daño valga. Los
/// cuatro flujos anteriores del repo autorizan para capturar, y ninguno tenía este estado.</para>
///
/// <para><b>La saga se CIERRA al reservar</b> y el retorno es una operación aparte sobre una saga
/// ya liquidada. No es cosmético: un alquiler dura días, y una saga que siguiera <c>Running</c>
/// hasta la devolución la daría por muerta <c>Sweep:AbandonAfterMinutes</c> — soltando la ventana
/// y anulando la garantía de un alquiler vivo. Es <c>close_doors_last</c> leído al derecho: se
/// cierra en cuanto no queda nada detrás que pueda fallar.</para>
///
/// <para><b>No guarda nada con pinta de credencial</b>, que es donde esto se revierte en silencio
/// (<c>an_orchestrator_cites_a_record_it_does_not_relay_a_credential</c>): quien alquila viaja
/// como seudónimo y nada más.</para>
/// </remarks>
/// <param name="Id">El identificador de la saga, que es el del alquiler.</param>
/// <param name="Renter">Quién alquila, como seudónimo.</param>
/// <param name="Status">En qué punto está la saga.</param>
/// <param name="EquipmentRef">Qué equipo.</param>
/// <param name="Quantity">Cuántas unidades.</param>
/// <param name="Window">La ventana del alquiler.</param>
/// <param name="Holds">Las unidades apartadas, una por cada.</param>
/// <param name="RentalPaymentId">El cobro del alquiler, capturado al reservar.</param>
/// <param name="DepositPaymentId">La garantía, autorizada y sin capturar.</param>
/// <param name="RentalTotal">Lo que se cobra.</param>
/// <param name="Deposit">Lo que se retiene.</param>
/// <param name="DamageCharged">Lo que se cobró de la garantía al devolver.</param>
/// <param name="Settled">Si el alquiler ya se cerró.</param>
/// <param name="SettledAs">
/// CÓMO se cerró: <c>returned</c> o <c>cancelled</c>. Es un campo y no una derivación, y eso
/// costó verlo: cancelar también cancela las reservas, así que deducirlo de «¿tiene reservas?»
/// habría contestado «devuelto» de todo alquiler cancelado — una fabricación que sale de datos
/// reales y por eso no se ve rara (<c>a_fabrication_can_be_a_derivation</c>).
/// </param>
/// <param name="Compensations">Lo que queda por deshacer.</param>
/// <param name="LastError">El último rechazo, para que alguien lo lea.</param>
/// <param name="StartedAtUtc">Cuándo empezó.</param>
/// <param name="AlertedAtUtc">Cuándo se avisó de una compensación colgada.</param>
/// <param name="AlertsSent">Cuántos avisos se mandaron.</param>
public sealed record RentalSaga(
    string Id,
    Ref Renter,
    SagaStatus Status,
    string EquipmentRef,
    int Quantity,
    TimeWindow Window,
    IReadOnlyList<UnitHold> Holds,
    string? RentalPaymentId,
    string? DepositPaymentId,
    Money RentalTotal,
    Money Deposit,
    Money DamageCharged,
    bool Settled,
    string? SettledAs,
    IReadOnlyList<Compensation> Compensations,
    string? LastError,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? AlertedAtUtc = null,
    int AlertsSent = 0) : ISaga<RentalSaga>
{
    /// <inheritdoc />
    public RentalSaga WithStatus(SagaStatus status) => this with { Status = status };

    /// <inheritdoc />
    public RentalSaga WithCompensations(IReadOnlyList<Compensation> compensations)
        => this with { Compensations = compensations };

    /// <inheritdoc />
    public RentalSaga WithAlert(DateTimeOffset? alertedAtUtc, int alertsSent)
        => this with { AlertedAtUtc = alertedAtUtc, AlertsSent = alertsSent };
}

/// <summary>El vocabulario con que este orquestador nombra lo que alquila.</summary>
/// <remarks>
/// Es <c>Ref(Kind, Id)</c> y la capacidad lo guarda y lo devuelve sin ramificar sobre él
/// (§0.B.13): <c>Api.Booking</c> no sabe que el recurso es un andamio, y el día que lo supiera
/// dejaría de servirle al siguiente dominio.
/// </remarks>
public static class RentalSubject
{
    /// <summary>El <c>Kind</c> de un equipo.</summary>
    public const string Kind = "alquiler.equipo";

    /// <summary>El equipo, como <c>Ref</c>.</summary>
    /// <param name="equipmentRef">Su slug.</param>
    /// <returns>El <c>Ref</c>.</returns>
    public static Ref For(string equipmentRef) => Ref.Create(Kind, equipmentRef);

    /// <summary>El alquiler, como sujeto de un cobro.</summary>
    /// <param name="sagaId">El identificador del alquiler.</param>
    /// <returns>El <c>Ref</c>.</returns>
    public static Ref RentalOf(string sagaId) => Ref.Create("alquiler.alquiler", sagaId);

    /// <summary>La garantía, como sujeto de su propia retención.</summary>
    /// <param name="sagaId">El identificador del alquiler.</param>
    /// <returns>El <c>Ref</c>.</returns>
    public static Ref DepositOf(string sagaId) => Ref.Create("alquiler.garantia", sagaId);
}
