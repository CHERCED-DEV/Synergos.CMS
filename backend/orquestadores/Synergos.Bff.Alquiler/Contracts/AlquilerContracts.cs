using Synergos.Bff.Core;
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
/// <param name="DepositHeld">
/// Cuánto sigue retenido: el monto mientras el equipo está fuera, cero una vez cerrado y
/// <b>nulo cuando no se sabe</b> — la compensación quedó colgada y puede que la garantía siga
/// retenida o puede que no. Cero ahí diría «no hay nada retenido», que es una afirmación y no
/// una ausencia (<c>gethashcode_is_not_a_seed</c>, addendum #111).
/// </param>
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
    decimal? DepositHeld,
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
            Retenido(s),
            s.DamageCharged.Amount);

    /// <summary>
    /// El estado en el vocabulario del CMS: <c>reserved</c>, <c>returned</c>, <c>cancelled</c> o
    /// <c>failed</c>.
    /// </summary>
    /// <remarks>
    /// <para><b>Se emite siempre</b> y el CMS lo PARSEA en vez de adivinarlo: uno que no se
    /// reconozca deja el alquiler fuera, que es mejor que decir «reservado» de algo que ya se
    /// cerró (<c>an_omitted_key_can_be_an_assertion</c>).</para>
    ///
    /// <para><b><c>failed</c> existe porque esto decía <c>reserved</c> de un intento
    /// DESHECHO</b>, y lo destapó un proceso vivo y no un test (#147). Con
    /// <c>Api.Payments</c> caída, reservar rechaza con 503, la saga compensa y la ventana
    /// vuelve —medido: <c>taken</c> de vuelta a 0— pero <c>SettledAs</c> sigue nulo, porque
    /// sólo lo escriben devolver y cancelar. Así que esta línea afirmaba que había un equipo
    /// fuera y 400 000 retenidos de un alquiler que no existió, en el <c>&lt;remarks&gt;</c>
    /// que citaba la memoria que lo prohíbe.</para>
    ///
    /// <para><b>Y es alcanzable desde el producto</b>, que es lo que lo hace un defecto y no
    /// una curiosidad: la llave de idempotencia la deriva el cliente del CMS del propio
    /// alquiler, así que el id del intento muerto es justo el que vuelve a pedir quien
    /// reintenta.</para>
    ///
    /// <para><b>Los tests no lo vieron por su REPARTO</b>: los diecisiete miran el ALMACÉN
    /// —¿se soltó el apartado?, ¿se anuló la garantía?— y ninguno mira la PROYECCIÓN que lee
    /// el CMS. Las dos mitades en verde y el hueco justo en medio (addendum #116 de
    /// <c>no_read_without_a_write_path</c>).</para>
    /// </remarks>
    private static string Estado(RentalSaga s)
        => s.SettledAs ?? (s.Status == SagaStatus.Completed ? "reserved" : "failed");

    /// <summary>
    /// Cuánto sigue retenido de la garantía, y <c>null</c> cuando no se sabe.
    /// </summary>
    /// <remarks>
    /// Se DERIVA del estado y no se guarda aparte, porque dos verdades sobre la misma plata
    /// acaban discrepando y gana la que nadie mira. Los tres casos son distintos: con el equipo
    /// fuera está retenida; cerrada o compensada está suelta —compensar ES que la anulación
    /// salió—; y con la compensación colgada <b>nadie lo sabe</b>, así que no se rellena.
    /// </remarks>
    private static decimal? Retenido(RentalSaga s) => s.Status switch
    {
        SagaStatus.CompensationFailed => null,
        SagaStatus.Compensating => null,
        SagaStatus.Compensated => 0m,
        _ => s.Settled ? 0m : s.Deposit.Amount,
    };
}
