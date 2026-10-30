using System.Text.Encodings.Web;
using System.Text.Json;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Las órdenes de la tienda entera, de los dos caminos: la directa del motor en proceso
/// (<see cref="StubShopOrderService"/>) y la puerta (<see cref="LibroDeComprasDeLaPuerta"/>). Así las lee el personal
/// (013-seller-operaciones, R7).
/// </summary>
/// <remarks>
/// <para><b>Lee el MISMO almacén de órdenes</b> (<c>orders</c>) que escriben los dos: ningún almacén nuevo
/// (ADR 0105). <see cref="IShopOrderService"/> sólo lista por comprador, porque es «Mis compras», y el personal
/// necesita la tienda entera.</para>
///
/// <para><b>Sólo lee.</b> Una orden ilegible se salta, como la salta el motor en proceso.</para>
/// </remarks>
public sealed class OrdenesDeLaTienda
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly IJsonEntityStore _store;

    public OrdenesDeLaTienda(IJsonEntityStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <summary>Todas las órdenes del almacén, la más reciente primero (a igual fecha, por referencia).</summary>
    public async Task<IReadOnlyList<ShopOrder>> TodasAsync(CancellationToken ct = default)
    {
        var todas = new List<ShopOrder>();
        foreach (var json in await _store.ListAsync(LibroDeComprasDeLaPuerta.ResourceType, ct).ConfigureAwait(false))
        {
            if (string.IsNullOrWhiteSpace(json)) continue;
            PersistedOrder? orden;
            try { orden = JsonSerializer.Deserialize<PersistedOrder>(json, Json); }
            catch (JsonException) { continue; }
            if (orden is not null) todas.Add(StubShopOrderService.ToOrder(orden));
        }
        return todas
            .OrderByDescending(o => o.CreatedAt)
            .ThenBy(o => o.OrderRef, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>La orden <paramref name="orderRef"/>, de cualquiera de los dos caminos, o nula si no hay.</summary>
    public async Task<ShopOrder?> UnaAsync(string? orderRef, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(orderRef)) return null;
        var json = await _store.ReadAsync(LibroDeComprasDeLaPuerta.ResourceType, orderRef.Trim(), ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<PersistedOrder>(json, Json) is { } orden ? StubShopOrderService.ToOrder(orden) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
