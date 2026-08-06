using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// La forma SERIALIZADA de una orden (el superset que persiste el motor tras T1).
/// Es el antiguo <c>OrderState</c>/<c>LineState</c> del <see cref="StubShopOrderService"/>
/// promovido a top-level para round-trip limpio con System.Text.Json (records
/// posicionales → deserializan por ctor). Guarda MÁS que el <see cref="ShopOrder"/>
/// público: <see cref="PersistedOrderLine.ReservationId"/> y
/// <see cref="PaymentSessionId"/> son necesarios para que <c>ConfirmAsync</c>
/// sobreviva un reinicio; <see cref="OwnerMemberKey"/> es el campo de T2 (aditivo
/// desde ya — null en órdenes de invitado).
/// </summary>
internal sealed record PersistedOrder(
    string OrderRef,
    string OrderNumber,
    string PaymentSessionId,
    string CustomerName,
    string CustomerEmail,
    decimal Total,
    string Currency,
    IReadOnlyList<PersistedOrderLine> Lines,
    DateTimeOffset CreatedAt,
    OrderStatus Status = OrderStatus.Pending,
    Guid? OwnerMemberKey = null);

internal sealed record PersistedOrderLine(
    string ProductId,
    string? VariantId,
    string ProductName,
    int Quantity,
    decimal UnitPrice,
    decimal LineTotal,
    string Currency,
    string ReservationId);
