using System.Text.Encodings.Web;
using System.Text.Json;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>Una compra de la tienda hecha por la puerta, como la contestó el orquestador a su comprador.</summary>
/// <param name="Id">El id de la saga: es la referencia de la orden y su «sesión de pago».</param>
/// <param name="Numero">El número que dice el aviso (los últimos 8 alfanuméricos del id).</param>
/// <param name="Miembro">El miembro que la abrió: el dueño.</param>
/// <param name="Lineas">Lo que se compró, con el precio que cotizó el servidor.</param>
/// <param name="Total">Lo que se autorizó o cobró.</param>
/// <param name="Moneda">La moneda del total.</param>
public sealed record CompraDeLaPuerta(
    string Id, string Numero, Guid Miembro, string? Nombre, string? Correo,
    IReadOnlyList<LineaDeLaCompra> Lineas, decimal Total, string Moneda);

/// <summary>Una línea de una compra de la puerta.</summary>
public sealed record LineaDeLaCompra(string ProductoId, string? VarianteId, string Nombre, int Cantidad, decimal PrecioUnitario);

/// <summary>
/// Las órdenes de las compras que salieron por la puerta (004-tienda-compra, decisión 004-P2): la compra la
/// abre, la cobra y la despacha el orquestador; lo que «Mis compras», el seguimiento, la devolución y las
/// reseñas leen sigue siendo la orden del CMS, y esto la anota.
/// </summary>
/// <remarks>
/// <para><b>Escribe en el MISMO almacén de órdenes</b> (<c>orders</c>) que el motor en proceso, con
/// <see cref="PersistedOrder.ViaGate"/>: así lo que ya lee órdenes las ve sin cambiar, y el motor en proceso
/// sabe que no las confirma ni las reembolsa por su proveedor. Ningún almacén nuevo (ADR 0105).</para>
///
/// <para><b>Una compra, una orden</b>, bajo un cerrojo por compra: anotar dos veces deja la misma, y
/// confirmar dos veces no vuelve a avanzar el seguimiento ni a registrar la venta. <b>Siempre de su dueño</b>:
/// una orden de la puerta de otro miembro no se toca.</para>
///
/// <para><b>No avisa</b>: el aviso de la compra es un paso del flujo (<c>notifications.avisar</c>); avisar
/// también desde acá mandaría dos correos.</para>
/// </remarks>
public sealed class LibroDeComprasDeLaPuerta
{
    /// <summary>La familia del almacén: la misma de <see cref="StubShopOrderService"/>.</summary>
    public const string ResourceType = "orders";

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly SemaphoreSlim[] Cerrojos = Enumerable.Range(0, 32).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    private readonly IJsonEntityStore _store;
    private readonly IOrderTrackingService? _tracking;
    private readonly ICheckoutRecorder? _ventas;
    private readonly Func<DateTimeOffset> _ahora;

    public LibroDeComprasDeLaPuerta(
        IJsonEntityStore store, IOrderTrackingService? tracking = null, ICheckoutRecorder? ventas = null, Func<DateTimeOffset>? ahora = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _tracking = tracking;
        _ventas = ventas;
        _ahora = ahora ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Anota la compra como PENDIENTE (abierta, todavía sin cobrar). Si ya tiene orden, la devuelve como está
    /// —una pagada sigue pagada—; si la orden con ese id no es una compra de la puerta de este miembro, nulo.
    /// </summary>
    public async Task<ShopOrder?> AnotarPendienteAsync(CompraDeLaPuerta compra, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(compra);
        var cerrojo = CerrojoDe(compra.Id);
        await cerrojo.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var previa = await LoadAsync(compra.Id, ct).ConfigureAwait(false);
            if (previa is not null) return DeLaPuertaYDe(previa, compra.Miembro) ? StubShopOrderService.ToOrder(previa) : null;

            var orden = Nueva(compra, OrderStatus.Pending);
            await GuardarAsync(orden, ct).ConfigureAwait(false);
            return StubShopOrderService.ToOrder(orden);
        }
        finally
        {
            cerrojo.Release();
        }
    }

    /// <summary>
    /// La compra se completó: su orden pasa a pagada UNA vez (si no estaba anotada, se anota pagada), avanza el
    /// seguimiento a «pagado» y registra la venta. Nulo si la orden con ese id no es de este miembro por la puerta.
    /// </summary>
    public async Task<ShopOrder?> ConfirmarAsync(CompraDeLaPuerta compra, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(compra);
        PersistedOrder pagada;
        var cerrojo = CerrojoDe(compra.Id);
        await cerrojo.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var previa = await LoadAsync(compra.Id, ct).ConfigureAwait(false);
            if (previa is not null && !DeLaPuertaYDe(previa, compra.Miembro)) return null;
            if (previa is { Status: OrderStatus.Paid }) return StubShopOrderService.ToOrder(previa);

            pagada = (previa ?? Nueva(compra, OrderStatus.Pending)) with { Status = OrderStatus.Paid };
            await GuardarAsync(pagada, ct).ConfigureAwait(false);
        }
        finally
        {
            cerrojo.Release();
        }

        // Lo que sigue a la transición, una sola vez, y después de persistir: un seguimiento o un panel de
        // ventas caídos no convierten una compra cobrada en un error (como en el motor en proceso).
        if (_tracking is not null)
        {
            await BestEffort.RunAsync(() => _tracking.AdvanceAsync(
                pagada.OrderRef, StubOrderTrackingService.StagePaid, $"Pago capturado — orden {pagada.OrderNumber}.", ct), ct)
                .ConfigureAwait(false);
        }
        if (_ventas is not null)
        {
            await BestEffort.RunAsync(() =>
            {
                _ventas.Record(new CheckoutCompleted(
                    OrderId: pagada.OrderRef,
                    LineItems: pagada.Lines.Select(l => new CheckoutLineItem(Sku: l.VariantId ?? l.ProductId, Quantity: l.Quantity, UnitPrice: l.UnitPrice)).ToList(),
                    Subtotal: pagada.Total,
                    Currency: pagada.Currency,
                    OccurredUtc: _ahora().UtcDateTime));
                return Task.CompletedTask;
            }, ct).ConfigureAwait(false);
        }
        return StubShopOrderService.ToOrder(pagada);
    }

    /// <summary>
    /// La compra se deshizo o no existe para su dueño: su orden PENDIENTE queda cancelada y deja de mirarse.
    /// Una pagada no se toca (devolver es otra cosa).
    /// </summary>
    public async Task<bool> DescartarAsync(string id, Guid miembro, CancellationToken ct = default)
    {
        var cerrojo = CerrojoDe(id);
        await cerrojo.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var previa = await LoadAsync(id, ct).ConfigureAwait(false);
            if (previa is not { Status: OrderStatus.Pending } || !DeLaPuertaYDe(previa, miembro)) return false;
            await GuardarAsync(previa with { Status = OrderStatus.Cancelled }, ct).ConfigureAwait(false);
            return true;
        }
        finally
        {
            cerrojo.Release();
        }
    }

    /// <summary>La orden de una compra de la puerta, o nulo si no hay o es de la ruta directa.</summary>
    public async Task<ShopOrder?> LeerAsync(string? id, CancellationToken ct = default)
    {
        var orden = await LoadAsync(id, ct).ConfigureAwait(false);
        return orden is { ViaGate: true } ? StubShopOrderService.ToOrder(orden) : null;
    }

    /// <summary>Las órdenes de la puerta de un miembro, las más recientes primero.</summary>
    public async Task<IReadOnlyList<ShopOrder>> DelMiembroAsync(Guid miembro, CancellationToken ct = default)
    {
        if (miembro == Guid.Empty) return [];
        var todas = new List<ShopOrder>();
        foreach (var json in await _store.ListAsync(ResourceType, ct).ConfigureAwait(false))
        {
            if (string.IsNullOrWhiteSpace(json)) continue;
            PersistedOrder? orden;
            try { orden = JsonSerializer.Deserialize<PersistedOrder>(json, Json); }
            catch (JsonException) { continue; }
            if (orden is not null && DeLaPuertaYDe(orden, miembro)) todas.Add(StubShopOrderService.ToOrder(orden));
        }
        return todas.OrderByDescending(o => o.CreatedAt).ToList();
    }

    /// <summary>
    /// Las órdenes de la puerta todavía PENDIENTES de todos los miembros, las más recientes primero: las que la
    /// conciliación de la tienda entera mira (013-seller-operaciones, T008). Cada una con su dueño.
    /// </summary>
    public async Task<IReadOnlyList<ShopOrder>> PendientesDeLaTiendaAsync(CancellationToken ct = default)
    {
        var pendientes = new List<ShopOrder>();
        foreach (var json in await _store.ListAsync(ResourceType, ct).ConfigureAwait(false))
        {
            if (string.IsNullOrWhiteSpace(json)) continue;
            PersistedOrder? orden;
            try { orden = JsonSerializer.Deserialize<PersistedOrder>(json, Json); }
            catch (JsonException) { continue; }
            if (orden is { ViaGate: true, Status: OrderStatus.Pending, OwnerMemberKey: not null })
            {
                pendientes.Add(StubShopOrderService.ToOrder(orden));
            }
        }
        return pendientes.OrderByDescending(o => o.CreatedAt).ToList();
    }

    private PersistedOrder Nueva(CompraDeLaPuerta compra, OrderStatus estado) => new(
        OrderRef: compra.Id,
        OrderNumber: compra.Numero,
        PaymentSessionId: compra.Id,
        CustomerName: compra.Nombre?.Trim() ?? string.Empty,
        CustomerEmail: compra.Correo?.Trim() ?? string.Empty,
        Total: compra.Total,
        Currency: compra.Moneda,
        Lines: compra.Lineas.Select(l => new PersistedOrderLine(
            l.ProductoId, l.VarianteId, l.Nombre, l.Cantidad, l.PrecioUnitario, l.PrecioUnitario * l.Cantidad, compra.Moneda,
            ReservationId: string.Empty)).ToList(),
        CreatedAt: _ahora(),
        Status: estado,
        OwnerMemberKey: compra.Miembro,
        ShipTo: null,
        ViaGate: true);

    private static bool DeLaPuertaYDe(PersistedOrder orden, Guid miembro) => orden.ViaGate && orden.OwnerMemberKey == miembro;

    private Task GuardarAsync(PersistedOrder orden, CancellationToken ct)
        => _store.WriteAsync(ResourceType, orden.OrderRef, JsonSerializer.Serialize(orden, Json), ct);

    private async Task<PersistedOrder?> LoadAsync(string? id, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        var json = await _store.ReadAsync(ResourceType, id.Trim(), ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<PersistedOrder>(json, Json); }
        catch (JsonException) { return null; }
    }

    private static SemaphoreSlim CerrojoDe(string id)
        => Cerrojos[(int)((uint)StringComparer.Ordinal.GetHashCode(id) % (uint)Cerrojos.Length)];
}
