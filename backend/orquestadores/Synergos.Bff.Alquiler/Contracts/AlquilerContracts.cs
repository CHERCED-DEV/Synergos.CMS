using Synergos.Bff.Alquiler.Domain;

namespace Synergos.Bff.Alquiler.Contracts;

/// <summary>Dinero, como llega y como sale.</summary>
/// <param name="Amount">El monto.</param>
/// <param name="Currency">La moneda.</param>
public sealed record MoneyPayload(decimal? Amount, string? Currency);

/// <summary>Lo que se pide para reservar un alquiler.</summary>
/// <remarks>
/// <b>El total y la garantía llegan CALCULADOS</b>, como la penalidad de Viajes y la devolución
/// parcial de Tienda: el orquestador compone la transacción y no sabe la tarifa de un andamio.
/// </remarks>
/// <param name="EquipmentId">Qué equipo.</param>
/// <param name="Quantity">Cuántas unidades.</param>
/// <param name="Start">Desde cuándo.</param>
/// <param name="End">Hasta cuándo.</param>
/// <param name="RenterKind">El tipo del sujeto que alquila.</param>
/// <param name="RenterId">Quién alquila, como seudónimo.</param>
/// <param name="RentalTotal">Lo que se cobra.</param>
/// <param name="Deposit">Lo que se retiene.</param>
public sealed record ReserveRentalRequest(
    string? EquipmentId,
    int? Quantity,
    DateTimeOffset? Start,
    DateTimeOffset? End,
    string? RenterKind,
    string? RenterId,
    MoneyPayload? RentalTotal,
    MoneyPayload? Deposit);

/// <summary>Lo que se pide para cerrar un alquiler.</summary>
/// <param name="Amount">El daño o la penalidad, ya calculado por quien recibió.</param>
public sealed record SettleRentalRequest(MoneyPayload? Amount);

/// <summary>Un alquiler tal como sale del orquestador.</summary>
/// <param name="RentalId">Su identificador.</param>
/// <param name="EquipmentId">Qué equipo.</param>
/// <param name="Quantity">Cuántas unidades.</param>
/// <param name="Start">Desde cuándo.</param>
/// <param name="End">Hasta cuándo.</param>
/// <param name="RenterId">Quién alquila.</param>
/// <param name="State">En qué punto está, como lo lee el CMS.</param>
/// <param name="RentalTotal">Lo que se cobró.</param>
/// <param name="DepositHeld">Cuánto sigue retenido; cero una vez cerrado.</param>
/// <param name="DamageCharged">Cuánto se cobró de la garantía.</param>
public sealed record RentalResponse(
    string RentalId,
    string EquipmentId,
    int Quantity,
    DateOnly Start,
    DateOnly End,
    string RenterId,
    string State,
    MoneyPayload RentalTotal,
    decimal DepositHeld,
    decimal DamageCharged)
{
    /// <summary>Proyecta una saga a lo que el CMS lee.</summary>
    /// <param name="s">La saga.</param>
    /// <returns>La respuesta.</returns>
    public static RentalResponse From(RentalSaga s)
        => new(
            s.Id,
            s.EquipmentRef,
            s.Quantity,
            DateOnly.FromDateTime(s.Window.Start.UtcDateTime),
            DateOnly.FromDateTime(s.Window.End.UtcDateTime),
            s.Renter.Id,
            Estado(s),
            new MoneyPayload(s.RentalTotal.Amount, s.RentalTotal.Currency),
            // Lo que sigue retenido: la garantía hasta que se cierra, y cero después. Se DERIVA
            // del estado y no se guarda aparte, porque dos verdades sobre la misma plata acaban
            // discrepando y gana la que nadie mira.
            s.Settled ? 0m : s.Deposit.Amount,
            s.DamageCharged.Amount);

    /// <summary>
    /// El estado en el vocabulario del CMS: <c>reserved</c>, <c>returned</c> o <c>cancelled</c>.
    /// </summary>
    /// <remarks>
    /// <b>Se emite siempre</b> y el CMS lo PARSEA en vez de adivinarlo: uno que no se reconozca
    /// deja el alquiler fuera, que es mejor que decir «reservado» de algo que ya se cerró
    /// (<c>an_omitted_key_can_be_an_assertion</c>).
    /// </remarks>
    private static string Estado(RentalSaga s) => s.SettledAs ?? "reserved";
}
