using Synergos.Bff.Core;
using Synergos.Core;

namespace Synergos.Bff.Tienda.Domain;

/// <summary>
/// Lo que Tienda sabe deshacer.
/// </summary>
/// <remarks>
/// <b>Cuatro kinds contra los tres de Salud, y ninguno compartido salvo los del pago.</b> Es la
/// prueba de que el <c>Kind</c> tenía que ser un <c>string</c>: un enum en <c>Bff.Core</c> habría
/// tenido que crecer al nacer este dominio, y volvería a crecer con Eventos, Viajes y los demás.
/// </remarks>
public static class TiendaCompensations
{
    /// <summary>Soltar un apartado de existencias de <c>Api.Inventory</c> que aún no se consumió.</summary>
    public const string ReleaseStockHold = "ReleaseStockHold";

    /// <summary>
    /// Devolver al inventario existencias <b>ya consumidas</b>.
    /// </summary>
    /// <remarks>
    /// Es a <see cref="ReleaseStockHold"/> lo que <see cref="RefundPayment"/> es a
    /// <see cref="VoidPayment"/>: el paso que la vuelve necesaria es irreversible, y seguir
    /// intentando la barata falla siempre. <c>Api.Inventory</c> lo dice con todas las letras —
    /// <c>hold_already_consumed: devolver existencias es un ajuste, no una liberación</c>.
    /// </remarks>
    public const string RestockItem = "RestockItem";

    /// <summary>Anular un pedido registrado en <c>Api.Orders</c>.</summary>
    public const string CancelOrder = "CancelOrder";

    /// <summary>Liberar una autorización de <c>Api.Payments</c> que nunca se capturó.</summary>
    public const string VoidPayment = "VoidPayment";

    /// <summary>Devolver plata ya capturada.</summary>
    public const string RefundPayment = "RefundPayment";
}

/// <summary>Un apartado de existencias, con lo que hace falta para deshacerlo.</summary>
/// <param name="HoldId">El apartado en <c>Api.Inventory</c>.</param>
/// <param name="ItemId">El ítem de existencias sobre el que se hizo.</param>
/// <param name="Quantity">Cuántas unidades.</param>
/// <remarks>
/// <b>Se guardan los tres y no solo el identificador del apartado.</b> Mientras el apartado no se
/// consume basta con soltarlo; una vez consumido hay que <i>devolver</i> las unidades al ítem, y
/// para eso hacen falta el ítem y la cantidad. Descubrir eso después de consumir sería tarde: la
/// respuesta de <c>Api.Inventory</c> ya no dice cuántas eran.
/// </remarks>
public sealed record StockHold(string HoldId, string ItemId, int Quantity);

/// <summary>
/// Una compra que cruza seis capacidades, con lo que haya que deshacer si falla.
/// </summary>
/// <param name="Id">Identificador. <b>Semilla de todas las llaves de idempotencia.</b></param>
/// <param name="Buyer">Quién compra — viaja opaco a las capacidades.</param>
/// <param name="CartId">La canasta de la que salió.</param>
/// <param name="Status">En qué punto está.</param>
/// <param name="Holds">Los apartados de existencias, <b>uno por línea</b>.</param>
/// <param name="OrderId">El pedido de Orders.</param>
/// <param name="PaymentId">El cobro de Payments.</param>
/// <param name="ShipmentId">El envío, si se despachó.</param>
/// <param name="Total">Cuánto se cobra.</param>
/// <param name="Compensations">Lo que hay que deshacer.</param>
/// <param name="LastError">Qué falló, para que el llamador lo lea.</param>
/// <param name="StartedAtUtc">Cuándo arrancó.</param>
/// <param name="AlertedAtUtc">Cuándo se avisó a una persona de que algo quedó colgado.</param>
/// <param name="AlertsSent">Cuántos avisos salieron.</param>
/// <remarks>
/// <b>Lo que esta saga estresa y la de Salud no:</b> el número de compensaciones no es fijo. Una
/// cita tiene como mucho dos —el cupo y el cobro—; una compra de siete líneas tiene siete
/// apartados de existencias más el pedido más el cobro. Que el motor no sepa cuántas hay ni de
/// qué tipo es exactamente lo que había que comprobar al promoverlo.
/// </remarks>
public sealed record PurchaseSaga(
    string Id,
    Ref Buyer,
    string CartId,
    SagaStatus Status,
    IReadOnlyList<StockHold> Holds,
    string? OrderId,
    string? PaymentId,
    string? ShipmentId,
    Money Total,
    IReadOnlyList<Compensation> Compensations,
    string? LastError,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? AlertedAtUtc = null,
    int AlertsSent = 0,
    Money? Refunded = null) : ISaga<PurchaseSaga>
{
    public PurchaseSaga WithStatus(SagaStatus status) => this with { Status = status };

    public PurchaseSaga WithCompensations(IReadOnlyList<Compensation> compensations)
        => this with { Compensations = compensations };

    public PurchaseSaga WithAlert(DateTimeOffset? alertedAtUtc, int alertsSent)
        => this with { AlertedAtUtc = alertedAtUtc, AlertsSent = alertsSent };
}
