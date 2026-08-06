using Synergos.Bff.Core;
using Synergos.Core;

namespace Synergos.Bff.Eventos.Domain;

/// <summary>
/// Lo que Eventos sabe deshacer.
/// </summary>
/// <remarks>
/// <b>Cuatro kinds, y ninguno nuevo respecto de Tienda salvo el nombre.</b> Es la señal de que el
/// aforo de un evento es la misma atomicidad que el stock de una tienda —un pozo contable— y no
/// la de una agenda. Si hubiera hecho falta inventar un quinto para «soltar una butaca», sería
/// que la butaca no era stock.
/// </remarks>
public static class EventosCompensations
{
    /// <summary>Soltar un apartado de aforo que aún no se consumió.</summary>
    public const string ReleaseSeatHold = "ReleaseSeatHold";

    /// <summary>
    /// Devolver al aforo unidades <b>ya consumidas</b>.
    /// </summary>
    /// <remarks>
    /// El apartado ya no existe: <c>Api.Inventory</c> rechaza soltarlo con
    /// <c>hold_already_consumed</c> y tiene razón. Deshacer un consumo es sumar al total, y para
    /// eso hacen falta el ítem y la cantidad — que la saga guardó al apartar precisamente porque
    /// después no habría de dónde sacarlos (<c>feedback_compensation_changes_character</c>).
    /// </remarks>
    public const string RestockSeats = "RestockSeats";

    /// <summary>Liberar una autorización que nunca se capturó.</summary>
    public const string VoidPayment = "VoidPayment";

    /// <summary>Devolver plata ya capturada.</summary>
    public const string RefundPayment = "RefundPayment";
}

/// <summary>
/// Un apartado de aforo, con lo que hace falta para deshacerlo.
/// </summary>
/// <param name="HoldId">El apartado en <c>Api.Inventory</c>.</param>
/// <param name="ItemId">El pozo de aforo sobre el que se hizo.</param>
/// <param name="Quantity">Cuántas unidades.</param>
/// <param name="Tier">La localidad, <b>para el llamador</b>. La capacidad no la conoce.</param>
/// <param name="Seat">La butaca, si es nominada. <c>null</c> en cupo general.</param>
/// <remarks>
/// <para><b>Los tres primeros son para deshacer; los dos últimos, para contestar.</b> Ni
/// <c>tier</c> ni <c>seat</c> viajan a <c>Api.Inventory</c>: la capacidad recibe un identificador
/// de ítem y una cantidad, y no sabe —ni debe— que detrás hay una localidad. Viven acá porque
/// quien pregunta por la compra sí necesita saber qué butaca apartó.</para>
/// </remarks>
public sealed record SeatHold(string HoldId, string ItemId, int Quantity, string Tier, string? Seat);

/// <summary>
/// Una compra de entradas, con lo que haya que deshacer si falla.
/// </summary>
/// <param name="Id">Identificador. <b>Semilla de todas las llaves de idempotencia.</b></param>
/// <param name="Buyer">Quién compra — viaja opaco a las capacidades.</param>
/// <param name="EventId">De qué evento. <b>No viaja a ninguna capacidad como tal</b>: se usa para
/// construir el sujeto del pozo de aforo, que es lo que <c>Api.Inventory</c> sí entiende.</param>
/// <param name="Status">En qué punto está.</param>
/// <param name="Holds">Los apartados de aforo, <b>uno por línea</b>.</param>
/// <param name="PaymentId">El cobro de Payments.</param>
/// <param name="Total">Cuánto se cobra.</param>
/// <param name="Compensations">Lo que hay que deshacer.</param>
/// <param name="LastError">Qué falló, para que el llamador lo lea.</param>
/// <param name="StartedAtUtc">Cuándo arrancó.</param>
/// <param name="AlertedAtUtc">Cuándo se avisó a una persona de que algo quedó colgado.</param>
/// <param name="AlertsSent">Cuántos avisos salieron.</param>
/// <remarks>
/// <para><b>Lo que esta saga NO tiene, y es lo que la define:</b> pedido y envío. Una entrada no
/// se despacha —el artefacto es un e-ticket que se emite del lado del contenido— así que
/// <c>Api.Orders</c> y <c>Api.Fulfillment</c> no participan. Contra la compra de Tienda son dos
/// capacidades menos y una compensación menos.</para>
///
/// <para><b>Y lo que sí comparte:</b> el número de compensaciones no es fijo. Una compra de
/// cuatro butacas lleva cuatro apartados más el cobro.</para>
/// </remarks>
public sealed record TicketingSaga(
    string Id,
    Ref Buyer,
    string EventId,
    SagaStatus Status,
    IReadOnlyList<SeatHold> Holds,
    string? PaymentId,
    Money Total,
    IReadOnlyList<Compensation> Compensations,
    string? LastError,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? AlertedAtUtc = null,
    int AlertsSent = 0) : ISaga<TicketingSaga>
{
    public TicketingSaga WithStatus(SagaStatus status) => this with { Status = status };

    public TicketingSaga WithCompensations(IReadOnlyList<Compensation> compensations)
        => this with { Compensations = compensations };

    public TicketingSaga WithAlert(DateTimeOffset? alertedAtUtc, int alertsSent)
        => this with { AlertedAtUtc = alertedAtUtc, AlertsSent = alertsSent };
}

/// <summary>
/// De qué evento y qué localidad sale un pozo de aforo.
/// </summary>
/// <remarks>
/// <para><b>Es la decisión que la HU #35 tenía abierta</b> —butaca nominada frente a cupo
/// general— y se resuelve sin que <c>Api.Inventory</c> se entere de ninguna de las dos:</para>
///
/// <list type="bullet">
///   <item><b>Cupo general</b> → un pozo por <c>evento/localidad</c>, con tantas unidades como
///   aforo. Se apartan <c>n</c>.</item>
///   <item><b>Butaca nominada</b> → un pozo por <c>evento/localidad/butaca</c>, con UNA unidad.
///   Se aparta 1.</item>
/// </list>
///
/// <para><b>La granularidad va en el identificador del sujeto, no en la capacidad.</b> Para
/// <c>Api.Inventory</c> las dos son lo mismo: un pozo del que se apartan unidades. Que una tenga
/// existencia 1 y la otra 400 es un dato del pozo, no una regla que ella tenga que entender —
/// exactamente igual que un SKU con talla frente a uno sin ella.</para>
///
/// <para>Si esto viviera del lado de la capacidad, <c>Api.Inventory</c> tendría que saber qué es
/// una butaca, y dejaría de servir para la tienda al día siguiente (<c>CLAUDE.md</c> §12).</para>
/// </remarks>
public static class AforoSubject
{
    /// <summary>El <c>Kind</c> con el que se nombra un pozo de aforo.</summary>
    public const string Kind = "eventos.aforo";

    /// <summary>El sujeto del pozo: con butaca si la hay, sin ella si es cupo general.</summary>
    public static Ref For(string eventId, string tier, string? seat)
        => Ref.Create(Kind, string.IsNullOrWhiteSpace(seat)
            ? $"{eventId}/{tier}"
            : $"{eventId}/{tier}/{seat}");

    /// <summary>El sujeto con el que <c>Api.Pricing</c> cotiza una localidad.</summary>
    /// <remarks>
    /// <b>Sin la butaca a propósito.</b> Dos butacas de la misma localidad valen lo mismo; meter
    /// el asiento en el sujeto obligaría a cargar un precio por butaca, que es lo que ningún
    /// organizador hace.
    /// </remarks>
    public static Ref PriceOf(string eventId, string tier) => Ref.Create("eventos.localidad", $"{eventId}/{tier}");
}
