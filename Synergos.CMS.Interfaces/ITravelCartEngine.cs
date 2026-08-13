namespace Synergos.CMS.Interfaces;

/// <summary>Un ítem del carrito ya apartado: qué se apartó y con qué referencia.</summary>
/// <param name="OfferId">La oferta del catálogo.</param>
/// <param name="ReservationId">
/// Con qué se vuelve a hablar de este apartado. <b>Opaca: la interpreta sólo el motor que la
/// emitió</b> — el motor en proceso pone el id de su reserva; un orquestador puede poner lo que
/// necesite para reconocerlo, o nada si no expone identificadores internos.
/// </param>
public sealed record TravelCartHeldItem(string OfferId, string ReservationId);

/// <summary>Lo que deja apartar un carrito entero.</summary>
/// <param name="EngineRef">
/// Con qué se vuelve a hablar del CARRITO. El motor en proceso no necesita ninguna —le basta la
/// referencia de orden del CMS— y un orquestador pone la de su saga.
/// </param>
/// <param name="PaymentSessionId">La sesión de pago única por el total.</param>
/// <param name="Total">Cuánto se cobra.</param>
/// <param name="Currency">En qué moneda.</param>
/// <param name="Items">Cada ítem, en el mismo orden en que llegó.</param>
/// <remarks>
/// <b>El total lo devuelve el MOTOR, no lo impone quien vende.</b> El motor en proceso suma lo
/// que traía el carrito; un orquestador cotiza contra su capacidad de precios. Si el CMS
/// impusiera el suyo, cualquiera reservaría la suite al precio de la estándar — es la misma
/// razón por la que la vía hotel dejó de mandar su total (HU #36).
/// </remarks>
public sealed record TravelCartHold(
    string? EngineRef,
    string PaymentSessionId,
    decimal Total,
    string Currency,
    IReadOnlyList<TravelCartHeldItem> Items);

/// <summary>Cómo quedó un ítem al liquidar el carrito.</summary>
/// <param name="OfferId">La oferta.</param>
/// <param name="ReservationId">La referencia, que puede haber cambiado al confirmarse.</param>
/// <param name="Status">
/// <c>Confirmed</c> si se honró; cualquier otra cosa significa que no, y entonces su precio entra
/// en <see cref="TravelCartSettlement.UnfulfilledAmount"/>.
/// </param>
public sealed record TravelCartSettledItem(string OfferId, string ReservationId, string Status);

/// <summary>
/// El resultado de cobrar y confirmar un carrito: <b>qué se honró y qué no</b>.
/// </summary>
/// <param name="Items">Cada ítem con su desenlace.</param>
/// <param name="UnfulfilledAmount">Cuánto suman los ítems que no se pudieron honrar.</param>
/// <remarks>
/// <para><b>Por ítem y no un booleano, y ésa es la decisión que hizo falta tomar antes</b>
/// (HU #40). Un carrito multi-producto puede quedar a medias de forma legítima: quien compró un
/// vuelo, un hotel y un auto no pierde el vuelo porque el auto se agotó. Una firma de todo-o-nada
/// habría cerrado esa puerta desde el tipo.</para>
///
/// <para><b>Y por eso el monto no cumplido lo calcula el MOTOR.</b> El que corre en proceso sabe
/// el precio de cada línea y lo suma; un orquestador que cotice el viaje entero puede no saberlo,
/// y en ese caso devuelve cero y dice qué ítems cayeron — la devolución la ordena entonces quien
/// vendió, que es quien sí sabe cuánto vale cada parte.</para>
/// </remarks>
public sealed record TravelCartSettlement(
    IReadOnlyList<TravelCartSettledItem> Items,
    decimal UnfulfilledAmount);

/// <summary>Lo que devolvió cancelar un carrito.</summary>
/// <param name="Refunded">Si se devolvió algo.</param>
/// <param name="Amount">Cuánto.</param>
public sealed record TravelCartRelease(bool Refunded, decimal Amount);

/// <summary>
/// El MOTOR de un carrito de viaje: apartar, cobrar-y-confirmar, y soltar.
/// </summary>
/// <remarks>
/// <para><b>Es lo único que cambia entre comprar en proceso y comprar contra un orquestador.</b>
/// El expediente de la compra —el huésped, el código de confirmación, la etapa del timeline, el
/// rastro de la cancelación— se queda del lado del CMS, porque un orquestador no guarda nada de
/// eso a propósito. Sin esta seam habría que reimplementar todo aquello dos veces, y la segunda
/// copia divergiría de la primera en la tercera ola.</para>
///
/// <para><b>Misma forma que <see cref="IHotelBookingService"/></b> (HU #36 rebanada 2a), y por la
/// misma razón: allí el cableado también obligó a sacar el motor de un controller antes de que
/// existiera un segundo motor que enchufar.</para>
///
/// <para><b>Lo que este seam NO decide</b> es qué pasa cuando un ítem no se puede honrar: sólo
/// obliga a <i>reportarlo por ítem</i>. Quién conserva qué es política del motor, y la de cada
/// uno está escrita en su implementación.</para>
/// </remarks>
public interface ITravelCartEngine
{
    /// <summary>
    /// Aparta cada ítem y abre UNA sesión de pago por el total.
    /// </summary>
    /// <remarks>
    /// Si algo falla a mitad, es el motor quien decide si suelta lo apartado — el CMS todavía no
    /// ha escrito nada, así que no hay expediente que deshacer.
    /// </remarks>
    Task<TravelCartHold> HoldAllAsync(
        IReadOnlyList<TravelCartItem> items,
        TravelGuest guest,
        string orderRef,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Captura el pago y confirma lo apartado, devolviendo qué se honró y qué no.
    /// </summary>
    /// <param name="engineRef">Lo que devolvió <see cref="HoldAllAsync"/>, si devolvió algo.</param>
    /// <param name="paymentSessionId">La sesión de pago del carrito.</param>
    /// <param name="lines">Cada ítem apartado, con su precio para poder cuantificar lo no honrado.</param>
    /// <param name="cancellationToken">Corta la operación.</param>
    /// <remarks>
    /// <b>Lanza si NADA se pudo honrar.</b> Un carrito donde no sobrevivió ni un ítem no es un
    /// viaje parcial: es un viaje que no existe, y quien llama tiene que enterarse.
    /// </remarks>
    Task<TravelCartSettlement> SettleAsync(
        string? engineRef,
        string paymentSessionId,
        IReadOnlyList<TravelCartSettledLine> lines,
        CancellationToken cancellationToken = default);

    /// <summary>Suelta lo apartado y devuelve lo que corresponda.</summary>
    Task<TravelCartRelease> ReleaseAsync(
        string? engineRef,
        string paymentSessionId,
        IReadOnlyList<TravelCartSettledLine> lines,
        string reason,
        CancellationToken cancellationToken = default);
}

/// <summary>Una línea tal como la guarda el CMS, para que el motor sepa qué liquidar.</summary>
/// <param name="OfferId">La oferta.</param>
/// <param name="ReservationId">La referencia opaca que devolvió el apartado.</param>
/// <param name="Price">Cuánto vale esta línea según el CMS.</param>
public sealed record TravelCartSettledLine(string OfferId, string ReservationId, decimal Price);
