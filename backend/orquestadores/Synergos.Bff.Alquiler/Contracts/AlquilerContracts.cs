using Synergos.Bff.Core;
using Synergos.Bff.Alquiler.Domain;

namespace Synergos.Bff.Alquiler.Contracts;

/// <summary>Dinero, como llega y como sale.</summary>
/// <param name="Amount">El monto.</param>
/// <param name="Currency">La moneda.</param>
public sealed record MoneyPayload(decimal? Amount = null, string? Currency = null);

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
    string? EquipmentId = null,
    int? Quantity = null,
    DateTimeOffset? Start = null,
    DateTimeOffset? End = null,
    string? RenterKind = null,
    string? RenterId = null,
    MoneyPayload? RentalTotal = null,
    MoneyPayload? Deposit = null);

/// <summary>Un tramo de la tarifa, como llega del CMS.</summary>
/// <param name="MinDays">Desde cuántos días aplica (dos o más).</param>
/// <param name="PerDay">Cuánto vale el día en ese tramo.</param>
public sealed record RateTierPayload(int? MinDays = null, decimal? PerDay = null);

/// <summary>La oferta de un equipo que publica el CMS al publicar su ficha (línea 005, T008).</summary>
/// <param name="EquipmentId">El slug del equipo.</param>
/// <param name="Currency">ISO 4217 de todos los importes.</param>
/// <param name="DailyRate">La tarifa base del día.</param>
/// <param name="Rates">Los tramos por duración.</param>
/// <param name="DepositPerUnit">La garantía de UNA unidad; cero es «sin garantía».</param>
/// <param name="MinDays">El alquiler más corto.</param>
/// <param name="MaxDays">El alquiler más largo.</param>
/// <param name="Units">Cuántas unidades hay.</param>
public sealed record PublishEquipmentOfferRequest(
    string? EquipmentId = null,
    string? Currency = null,
    decimal? DailyRate = null,
    IReadOnlyList<RateTierPayload>? Rates = null,
    decimal? DepositPerUnit = null,
    int? MinDays = null,
    int? MaxDays = null,
    int? Units = null);

/// <summary>Un tramo publicado.</summary>
/// <param name="MinDays">Desde cuántos días aplica.</param>
/// <param name="PerDay">Cuánto vale el día.</param>
public sealed record RateTierResponse(int MinDays, decimal PerDay);

/// <summary>Lo que quedó publicado de un equipo.</summary>
/// <param name="EquipmentId">El slug del equipo.</param>
/// <param name="Currency">La moneda.</param>
/// <param name="DailyRate">La tarifa base.</param>
/// <param name="Rates">Los tramos.</param>
/// <param name="DepositPerUnit">La garantía por unidad.</param>
/// <param name="MinDays">El alquiler más corto.</param>
/// <param name="MaxDays">El más largo.</param>
/// <param name="Units">Las unidades.</param>
/// <param name="Retired">Si la oferta se retiró: el equipo ya no se ofrece por la puerta.</param>
/// <param name="OutsideReservations">
/// Las reservas vigentes que bajar las unidades dejó por encima de los cupos (NN-6): siguen confirmadas; decidir qué hacer
/// con ellas es del personal.
/// </param>
public sealed record EquipmentOfferResponse(
    string EquipmentId, string Currency, decimal DailyRate, IReadOnlyList<RateTierResponse> Rates,
    decimal DepositPerUnit, int MinDays, int MaxDays, int Units, bool Retired, IReadOnlyList<string> OutsideReservations)
{
    /// <summary>Proyecta lo registrado.</summary>
    /// <param name="o">La oferta registrada.</param>
    /// <returns>La respuesta.</returns>
    public static EquipmentOfferResponse From(OfertaDeAlquilerRegistrada o)
        => new(o.Equipo, o.Moneda, o.ValorDia, o.Tramos.Select(t => new RateTierResponse(t.DesdeDias, t.ValorDia)).ToList(),
            o.GarantiaUnidad, o.MinDias, o.MaxDias, o.Unidades, o.Retirada, o.ReservasFuera ?? []);
}

/// <summary>Lo que se pide para cerrar un alquiler.</summary>
/// <param name="Amount">El daño o la penalidad, ya calculado por quien recibió.</param>
public sealed record SettleRentalRequest(MoneyPayload? Amount = null);

/// <summary>Lo que se pide para cancelar un alquiler confirmado (línea 005, T019).</summary>
/// <param name="Amount">La penalidad que retiene el personal; la del inquilino se ignora.</param>
/// <param name="Actor">Quién cancela: <c>renter</c> (por defecto) o <c>staff</c>.</param>
public sealed record CancelRentalRequest(MoneyPayload? Amount = null, string? Actor = null);

/// <summary>Confirmar la devolución de un alquiler de la puerta (línea 005, T017): el daño y quién confirma.</summary>
/// <param name="Damage">Lo que se cobra de la garantía; cero la anula entera. Obligatorio: no se supone cero.</param>
/// <param name="StaffId">El seudónimo del personal que recibió el equipo; su clase la pone el orquestador.</param>
public sealed record ConfirmRentalReturnRequest(MoneyPayload? Damage = null, string? StaffId = null);

/// <summary>Un alquiler de la puerta con lo que pasó después del cierre (la liquidación, data-model §2).</summary>
/// <param name="RentalId">El alquiler: la saga de <c>alquiler.renta</c>.</param>
/// <param name="State">En el vocabulario del CMS: <c>reserved</c>, <c>declared</c>, <c>returned</c> o <c>cancelled</c>.</param>
/// <param name="Deposit">La garantía retenida al reservar.</param>
/// <param name="DepositHeld">Cuánto sigue retenido: la garantía hasta que el personal confirme, cero después.</param>
/// <param name="DamageCharged">Lo que se cobró de la garantía; nulo mientras no se confirma.</param>
/// <param name="DepositReleased">Lo que se le soltó al inquilino; nulo es «no consta».</param>
/// <param name="DeclaredAtUtc">Cuándo declaró el inquilino; nulo si no declaró.</param>
/// <param name="ConfirmedAtUtc">Cuándo confirmó el personal.</param>
/// <param name="CancelledAtUtc">Cuándo se canceló (T020).</param>
/// <param name="RentalRefunded">Lo cobrado que se devolvió al cancelar: el total entero.</param>
public sealed record RentalSettlementResponse(
    string RentalId,
    string State,
    MoneyPayload Deposit,
    decimal DepositHeld,
    decimal? DamageCharged,
    decimal? DepositReleased,
    DateTimeOffset? DeclaredAtUtc,
    DateTimeOffset? ConfirmedAtUtc,
    DateTimeOffset? CancelledAtUtc,
    decimal? RentalRefunded)
{
    /// <summary>Proyecta la vista de la liquidación a lo que el CMS lee.</summary>
    /// <param name="v">La vista.</param>
    /// <returns>La respuesta.</returns>
    /// <remarks>Lo cobrado de la garantía es el daño al devolver o la penalidad al cancelar: nunca los dos.</remarks>
    public static RentalSettlementResponse From(VistaDeLiquidacion v)
        => new(
            v.SagaId,
            v.Estado,
            new MoneyPayload(v.Garantia.Amount, v.Garantia.Currency),
            v.GarantiaRetenida.Amount,
            (v.Registro?.Danio ?? v.Registro?.Penalidad)?.Amount,
            v.Registro?.GarantiaLiberada?.Amount,
            v.Registro?.DeclaradaUtc,
            v.Registro?.ConfirmadaUtc,
            v.Registro?.CanceladaUtc,
            v.Registro?.AlquilerDevuelto?.Amount);
}

/// <summary>Cancelar un alquiler confirmado de la puerta (línea 005, T020).</summary>
/// <param name="Actor">Quién cancela: <c>renter</c> (el dueño, por <c>X-Synergos-Sujeto</c>) o <c>staff</c>.</param>
/// <param name="Penalty">Lo que retiene el personal; la del inquilino se ignora.</param>
/// <param name="StaffId">El seudónimo del personal, si cancela él; su clase la pone el orquestador.</param>
public sealed record CancelDoorRentalRequest(string? Actor = null, MoneyPayload? Penalty = null, string? StaffId = null);

/// <summary>Un alquiler tal como sale del orquestador.</summary>
/// <param name="RentalId">Su identificador.</param>
/// <param name="EquipmentId">Qué equipo.</param>
/// <param name="Quantity">Cuántas unidades.</param>
/// <param name="Start">Desde cuándo.</param>
/// <param name="End">Hasta cuándo.</param>
/// <param name="RenterId">Quién alquila.</param>
/// <param name="State">En qué punto está, como lo lee el CMS.</param>
/// <param name="RentalTotal">Lo que se cobró.</param>
/// <param name="Deposit">
/// La garantía que se retuvo al reservar. Distinta de <paramref name="DepositHeld"/>, que es lo que
/// SIGUE retenido: cerrado el alquiler aquélla no cambia y ésta vale cero, y sin la primera el CMS
/// tendría que adivinar con el catálogo de hoy cuánto se retuvo en su día (#204).
/// </param>
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
    MoneyPayload Deposit,
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
            new MoneyPayload(s.Deposit.Amount, s.Deposit.Currency),
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
