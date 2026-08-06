using Synergos.Core;

namespace Synergos.Api.Orders.Domain;

/// <summary>En qué punto está un pedido.</summary>
/// <remarks>
/// <b>No hay estado "pagado".</b> El cobro es de <c>Api.Payments</c> y se enlaza por referencia:
/// un pedido puede existir sin cobro —un trámite gubernamental se radica y puede pagarse después,
/// o no pagarse nunca— y meter el pago en este ciclo de vida obligaría a Gov a arrastrar
/// semántica de tienda.
/// </remarks>
public enum OrderStatus
{
    /// <summary>Registrado y en pie.</summary>
    Placed,

    /// <summary>Entregado o prestado. Estado final.</summary>
    Fulfilled,

    /// <summary>Anulado. Estado final.</summary>
    Cancelled,
}

/// <summary>Una línea de pedido, con su precio ya congelado.</summary>
/// <param name="Subject">Qué — opaco.</param>
/// <param name="Quantity">Cuántos.</param>
/// <param name="UnitPrice">A cuánto, en el momento de registrar.</param>
/// <remarks>
/// <b>Acá el precio SÍ se congela</b>, al revés que en la canasta: un pedido es un acuerdo sobre
/// un monto. Si el precio se releyera al mostrarlo, la factura cambiaría sola cada vez que el
/// catálogo sube un precio, y nadie podría reclamar lo que acordó.
/// </remarks>
public sealed record OrderLine(Ref Subject, int Quantity, Money UnitPrice)
{
    public Money Subtotal => UnitPrice * Quantity;
}

/// <summary>Un pedido.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Buyer">Quién lo hizo — opaco.</param>
/// <param name="Lines">Qué lleva, con precios congelados.</param>
/// <param name="Total">Lo acordado, incluidos impuestos y descuentos.</param>
/// <param name="Status">En qué punto está.</param>
/// <param name="PlacedAtUtc">Cuándo se registró.</param>
/// <param name="ClosedAtUtc">Cuándo llegó a un estado final.</param>
public sealed record Order(
    string Id,
    Ref Buyer,
    IReadOnlyList<OrderLine> Lines,
    Money Total,
    OrderStatus Status,
    DateTimeOffset PlacedAtUtc,
    DateTimeOffset? ClosedAtUtc = null)
{
    public bool IsClosed => Status != OrderStatus.Placed;
}
