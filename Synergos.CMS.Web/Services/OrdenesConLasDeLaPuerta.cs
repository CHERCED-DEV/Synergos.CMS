using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// El motor de órdenes de la tienda (el que diga <c>Synergos:Tienda:Mode</c>) MÁS las órdenes de las compras que
/// salieron por la puerta (004-tienda-compra, US 6): lo que leen «Mis compras», el seguimiento, la devolución y las
/// reseñas de comprador verificado.
/// </summary>
/// <remarks>
/// <para><b>Por qué un decorador y no un cambio en cada motor.</b> Con el motor en proceso, las órdenes de la puerta
/// ya están en su almacén (el libro escribe en el mismo); con el orquestador (<c>Mode=Bff</c>), el historial salía
/// vacío. Sumarlas acá vale para los dos, sin tocar a ninguno, y sin duplicar: una orden se cuenta una vez.</para>
///
/// <para><b>Lo que no es de las órdenes de la puerta va al motor tal cual</b>: abrir y confirmar por la ruta directa
/// no cambian. Una orden de la puerta no se confirma por el motor (la cierra la puerta), ni se reembolsa por él: su
/// plata la tiene el orquestador, y la operación que la devuelve sobre la saga genérica es del núcleo (N-6); hasta
/// entonces se dice que no se puede.</para>
/// </remarks>
public sealed class OrdenesConLasDeLaPuerta : IShopOrderService
{
    private readonly IShopOrderService _motor;
    private readonly LibroDeComprasDeLaPuerta _libro;
    private readonly RegistroDeComprasDeLaTienda _registro;
    private readonly ILogger<OrdenesConLasDeLaPuerta> _log;

    public OrdenesConLasDeLaPuerta(
        IShopOrderService motor, LibroDeComprasDeLaPuerta libro, RegistroDeComprasDeLaTienda registro, ILogger<OrdenesConLasDeLaPuerta> log)
    {
        _motor = motor;
        _libro = libro;
        _registro = registro;
        _log = log;
    }

    public Task<ShopCheckoutResult> CheckoutAsync(IReadOnlyList<ShopCartItem> items, ShopCustomer customer, CancellationToken cancellationToken = default)
        => _motor.CheckoutAsync(items, customer, cancellationToken);

    /// <summary>Confirma por el motor una orden de la ruta directa; la de la puerta se cierra por la puerta.</summary>
    /// <remarks>
    /// Con el motor en proceso, él mismo lo rechaza; con el orquestador de motor no sabe que la orden es de la
    /// puerta y le pediría confirmar una compra que no es suya. Se decide acá, por la orden, para los dos.
    /// </remarks>
    public async Task<ShopConfirmationResult> ConfirmAsync(string orderRef, ShopShippingAddress? shipTo = null, CancellationToken cancellationToken = default)
    {
        if (await _libro.LeerAsync(orderRef, cancellationToken).ConfigureAwait(false) is not null)
        {
            throw new InvalidOperationException("Esta compra salió por la puerta: se confirma por ella, no por la ruta directa.");
        }
        return await _motor.ConfirmAsync(orderRef, shipTo, cancellationToken).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<ShopOrder>> GetOrdersAsync(string customerEmail, CancellationToken cancellationToken = default)
        => _motor.GetOrdersAsync(customerEmail, cancellationToken);

    /// <summary>Las del motor y las de la puerta del miembro, una vez cada una, las más recientes primero.</summary>
    /// <remarks>Antes de listar se reconcilian sus compras de la puerta pendientes, con el techo del registro.</remarks>
    public async Task<IReadOnlyList<ShopOrder>> GetOrdersByMemberAsync(Guid memberKey, CancellationToken cancellationToken = default)
    {
        try
        {
            await _registro.ReconciliarAsync(memberKey, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Degradar por ausencia: sin reconciliar se listan como estén; la próxima lectura lo intenta otra vez.
            _log.LogWarning(ex, "No se pudieron reconciliar las compras de la puerta de un miembro; se listan como están.");
        }

        var delaPuerta = await _libro.DelMiembroAsync(memberKey, cancellationToken).ConfigureAwait(false);
        var delMotor = await _motor.GetOrdersByMemberAsync(memberKey, cancellationToken).ConfigureAwait(false);
        var vistas = delaPuerta.Select(o => o.OrderRef).ToHashSet(StringComparer.Ordinal);
        return delaPuerta.Concat(delMotor.Where(o => !vistas.Contains(o.OrderRef)))
            .OrderByDescending(o => o.CreatedAt)
            .ToList();
    }

    /// <summary>La de la puerta si lo es (sabe que lo es); si no, la del motor.</summary>
    public async Task<ShopOrder?> GetOrderAsync(string orderRef, CancellationToken cancellationToken = default)
        => await _libro.LeerAsync(orderRef, cancellationToken).ConfigureAwait(false)
           ?? await _motor.GetOrderAsync(orderRef, cancellationToken).ConfigureAwait(false);

    /// <summary>Reembolsa por el motor una orden de la ruta directa; la de la puerta, todavía no (N-6).</summary>
    public async Task<decimal> RefundAsync(string orderRef, decimal amount, string currency, string reason, CancellationToken cancellationToken = default)
    {
        if (await _libro.LeerAsync(orderRef, cancellationToken).ConfigureAwait(false) is not null)
        {
            throw new InvalidOperationException(
                "Esta compra salió por la puerta y su plata la tiene el orquestador: el reembolso por ese camino todavía no existe. " +
                "La devolución queda recibida y se reembolsa cuando esté.");
        }
        return await _motor.RefundAsync(orderRef, amount, currency, reason, cancellationToken).ConfigureAwait(false);
    }
}
