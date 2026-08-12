using Synergos.Bff.Core;
using Synergos.Core;

namespace Synergos.Bff.Viajes.Domain;

/// <summary>
/// Lo que Viajes sabe deshacer.
/// </summary>
/// <remarks>
/// <para><b>Cuatro kinds, y son DOS pares.</b> Cada par es la misma intención antes y después de
/// que algo se vuelva irreversible: soltar un apartado frente a cancelar una reserva ya hecha;
/// liberar una autorización frente a devolver plata ya capturada
/// (<c>feedback_compensation_changes_character</c>).</para>
///
/// <para><b>Y acá el par del cupo importa de verdad, al revés que en Salud.</b> Allá confirmar el
/// cupo es el último paso, así que nunca hay una reserva confirmada que haya que deshacer. Un
/// viaje son varios ítems: al confirmar el tercero, los dos primeros ya son reservas — y soltar
/// un apartado que ya se convirtió en reserva lo rechaza <c>Api.Booking</c>, con razón. Si no se
/// reescribiera, la compensación fallaría para siempre por una razón que no tiene nada que ver
/// con el mundo real.</para>
/// </remarks>
public static class ViajesCompensations
{
    /// <summary>Soltar un apartado de <c>Api.Booking</c> que aún no se confirmó.</summary>
    public const string ReleaseBookingHold = "ReleaseBookingHold";

    /// <summary>
    /// Cancelar una reserva <b>ya confirmada</b>.
    /// </summary>
    /// <remarks>
    /// El apartado ya no existe: al confirmarlo, <c>Api.Booking</c> lo convirtió en una reserva y
    /// rechaza soltarlo. Deshacerlo es cancelar la reserva, y para eso hace falta el
    /// identificador de la reserva — que la saga guarda en el instante en que se confirma,
    /// precisamente porque después no habría de dónde sacarlo.
    /// </remarks>
    public const string CancelReservation = "CancelReservation";

    /// <summary>Liberar una autorización que nunca se capturó.</summary>
    public const string VoidPayment = "VoidPayment";

    /// <summary>Devolver plata ya capturada.</summary>
    public const string RefundPayment = "RefundPayment";
}

/// <summary>
/// Un ítem del viaje apartado, con lo que hace falta para deshacerlo y para contestar.
/// </summary>
/// <param name="HoldId">El apartado en <c>Api.Booking</c>.</param>
/// <param name="ResourceId">Sobre qué recurso se hizo.</param>
/// <param name="Window">Qué periodo ocupa.</param>
/// <param name="ProductRef">Qué producto es, <b>para el llamador</b>. La capacidad no lo interpreta.</param>
/// <param name="ProductLabel">Cómo se llama en pantalla.</param>
/// <param name="ReservationId">La reserva, cuando el apartado ya se confirmó. Nulo antes.</param>
/// <remarks>
/// <para><b>Los tres primeros son para deshacer; los dos del medio, para contestar.</b> Ni
/// <c>productRef</c> ni <c>productLabel</c> viajan a <c>Api.Booking</c>: la capacidad recibe un
/// recurso y una ventana, y no sabe —ni debe— que detrás hay una habitación doble con desayuno.
/// Viven acá porque quien pregunta por el viaje sí necesita saber qué reservó.</para>
///
/// <para><b><see cref="ReservationId"/> se llena al confirmar</b>, y es lo que permite que la
/// compensación cambie de carácter sin perder a qué se refiere.</para>
/// </remarks>
/// <param name="Unfulfilled">
/// El ítem no se pudo confirmar y se soltó, en un viaje con confirmación PARCIAL.
/// </param>
/// <remarks>
/// <b>«No confirmado» y «no cumplido» no son lo mismo.</b> Un apartado sin
/// <see cref="ReservationId"/> puede estar simplemente pendiente; uno con
/// <see cref="Unfulfilled"/> ya se intentó, falló y se soltó. Sin distinguirlos, un reintento
/// volvería a probar algo que ya se decidió que no iba.
/// </remarks>
public sealed record ItemHold(
    string HoldId,
    string ResourceId,
    TimeWindow Window,
    string ProductRef,
    string ProductLabel,
    string? ReservationId = null,
    bool Unfulfilled = false);

/// <summary>
/// Un viaje que cruza capacidades, con lo que haya que deshacer si falla.
/// </summary>
/// <param name="Id">Identificador. <b>Semilla de todas las llaves de idempotencia.</b></param>
/// <param name="Traveller">Quién viaja — viaja opaco a las capacidades.</param>
/// <param name="Status">En qué punto está.</param>
/// <param name="Holds">Los apartados, <b>uno por ítem</b>.</param>
/// <param name="PaymentId">El cobro de Payments.</param>
/// <param name="Total">Cuánto se cobra.</param>
/// <param name="Retained">
/// Cuánto NO se devuelve si el viaje se cancela — la penalidad de la política comercial.
/// </param>
/// <param name="Compensations">Lo que hay que deshacer.</param>
/// <param name="LastError">Qué falló, para que el llamador lo lea.</param>
/// <param name="PartialConfirm">
/// Si un ítem que no se pueda confirmar tumba el viaje entero (<c>false</c>, el default) o sólo
/// se marca como no cumplido y el resto sigue en pie (<c>true</c>).
/// </param>
/// <param name="StartedAtUtc">Cuándo arrancó.</param>
/// <param name="AlertedAtUtc">Cuándo se avisó a una persona de que algo quedó colgado.</param>
/// <param name="AlertsSent">Cuántos avisos salieron.</param>
/// <remarks>
/// <para><b>Lo que esta saga tiene de propio frente a las otras tres</b> es que sus pasos
/// reversibles son <i>varios y heterogéneos</i>: un vuelo, dos noches de hotel y un auto son
/// cuatro apartados sobre cuatro recursos distintos, con cuatro ventanas distintas, y el fallo
/// puede llegar en cualquiera. Es la razón por la que #36 dejó de ser un cableado y pasó a ser un
/// orquestador.</para>
///
/// <para><b>Por qué la penalidad vive acá y no en <c>Api.Booking</c>.</b> Esa capacidad lo dice
/// de frente: «Booking dice si la cancelación está en plazo; qué se devuelve y a quién lo decide
/// <c>Api.Payments</c> y lo ORDENA el BFF. Meter el monto acá ataría la capacidad a una política
/// comercial que cambia por negocio». Este es el BFF, así que acá se ordena — y lo que llega es
/// un monto ya calculado por quien conoce la tarifa, no una regla que este servicio interprete.
/// </para>
///
/// <para><b>Y lo que NO tiene:</b> ni <c>RoomTypeCode</c>, ni <c>RatePlanCode</c>, ni
/// <c>GuestName</c>. Esos sustantivos son de viajes y ninguna capacidad puede guardarlos; se
/// quedan del lado del CMS, que es quien los une con lo que esto devuelve. El
/// <see cref="ItemHold.ProductRef"/> es lo único que cruza, y cruza opaco.</para>
/// </remarks>
public sealed record TripSaga(
    string Id,
    Ref Traveller,
    SagaStatus Status,
    IReadOnlyList<ItemHold> Holds,
    string? PaymentId,
    Money Total,
    Money Retained,
    IReadOnlyList<Compensation> Compensations,
    string? LastError,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? AlertedAtUtc = null,
    int AlertsSent = 0,
    bool PartialConfirm = false) : ISaga<TripSaga>
{
    public TripSaga WithStatus(SagaStatus status) => this with { Status = status };

    public TripSaga WithCompensations(IReadOnlyList<Compensation> compensations)
        => this with { Compensations = compensations };

    public TripSaga WithAlert(DateTimeOffset? alertedAtUtc, int alertsSent)
        => this with { AlertedAtUtc = alertedAtUtc, AlertsSent = alertsSent };
}

/// <summary>
/// De qué producto de viaje sale un recurso reservable, y con qué se cotiza.
/// </summary>
/// <remarks>
/// <para><b>Ésta es la decisión que la HU #36 tenía abierta</b> —«no todo va a
/// <c>Api.Booking</c>; un asiento de vuelo se parece más a un pozo contable»— y la respuesta,
/// mirando el código y no la intuición, es que <b>los cuatro van a <c>Api.Booking</c></b>.</para>
///
/// <para><b>Por qué.</b> `Api.Booking` no reserva cosas únicas: su <c>Resource</c> lleva
/// <c>Capacity</c> —«1 para un consultorio; 40 para un aula»— así que el aspecto de pozo ya está
/// dentro. Y su regla de «horario vacío = siempre abierto» se tomó nombrando este caso: «una
/// noche de hotel va de las 15:00 de un día a las 11:00 del siguiente — cruza la medianoche y no
/// encaja en ninguna franja diaria». La capacidad se diseñó para esto.</para>
///
/// <para><b>El vuelo se consideró para <c>Api.Inventory</c> y se descartó</b>, con el argumento a
/// la vista para quien quiera revisarlo: modelado como recurso, un vuelo tiene capacidad igual a
/// sus asientos y una ventana igual a su duración, y como <i>todos</i> los apartados usan la
/// misma ventana, la comprobación de solape cuenta exactamente los asientos vendidos. Funciona.
/// El olor es que esa ventana no varía nunca — se carga una dimensión de tiempo ceremonial. Se
/// aceptó igual porque partirlo en dos capacidades duplicaría los kinds de compensación y
/// obligaría a la saga a recordar <i>contra cuál</i> se apartó cada ítem, a cambio de nada.</para>
///
/// <para><b>Lo que dispara revisarlo</b>, escrito para que no haya que reconstruir el
/// razonamiento: que un vuelo necesite sobreventa por clase tarifaria. Eso es comportamiento de
/// pozo contable y <c>Api.Booking</c> no lo sabe expresar — su capacidad es un techo duro.</para>
/// </remarks>
public static class TravelSubject
{
    /// <summary>El <c>Kind</c> con el que se nombra un producto reservable de viaje.</summary>
    public const string Kind = "viajes.producto";

    /// <summary>El sujeto del recurso, tal como lo registró quien lo dio de alta.</summary>
    public static Ref For(string productRef) => Ref.Create(Kind, productRef);

    /// <summary>El sujeto con el que <c>Api.Pricing</c> cotiza un producto.</summary>
    /// <remarks>
    /// <b>El mismo identificador, otro <c>Kind</c>.</b> Cotizar y apartar son dos preguntas
    /// distintas sobre la misma cosa, y cada capacidad es dueña de su propio vocabulario: mezclar
    /// los <c>Kind</c> ataría el catálogo de precios al registro de recursos, que es justo lo que
    /// hace que dos capacidades dejen de poder desplegarse por separado.
    /// </remarks>
    public static Ref PriceOf(string productRef) => Ref.Create("viajes.tarifa", productRef);
}
