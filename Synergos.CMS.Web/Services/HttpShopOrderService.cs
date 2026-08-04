using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// El <see cref="IShopOrderService"/> que compra de verdad — contra <c>Synergos.Bff.Tienda</c>
/// (HU #24).
/// </summary>
/// <remarks>
/// <para><b>Contra el ORQUESTADOR, no contra las capacidades.</b> Es la decisión de la HU y la
/// que no había que equivocar. Un checkout son tres pasos que pueden fallar a la mitad: si el
/// cobro falla hay que soltar el stock apartado. Llamando a <c>Api.Inventory</c>,
/// <c>Api.Payments</c> y <c>Api.Orders</c> por separado, el CMS estaría reimplementando la
/// máquina de sagas de <c>Bff.Core</c> — y peor, porque <b>no tiene dónde anotar una
/// compensación pendiente</b>. Hay gate: <c>ShopWiringTests</c>.</para>
///
/// <para><b>Comprar es crear una canasta y comprarla</b>, y eso es dos capacidades, no una.
/// <c>POST /v1/purchases</c> recibe un <c>cartId</c> a propósito: la canasta es la única fuente
/// de qué se está comprando, porque copiar las líneas en la petición dejaría que el cliente
/// pidiera algo distinto de lo que tiene en pantalla. Así que acá se abre la canasta en
/// <c>Api.Cart</c>, se ponen las líneas y recién entonces se compra. <b>No es reimplementar la
/// saga</b>: nada de eso hay que deshacerlo si falla —una canasta abierta y nunca comprada vence
/// sola— y por eso puede vivir del lado del CMS sin necesitar un orquestador.</para>
///
/// <para><b>El peor resultado posible es un cobro sin pedido</b>, así que un timeout NO se
/// resuelve mostrando un error y ya. La llave de idempotencia se deriva del carrito y del
/// comprador ANTES de la primera llamada, y es el identificador de la saga: si la petición se
/// pierde en el aire, reintentar con la misma llave devuelve la compra que ya existía en vez de
/// crear una segunda. Es lo que hace que «no pudimos procesar tu compra, no se te cobró» pueda
/// ser CIERTO.</para>
///
/// <para><b>Con el orquestador apagado, la tienda sigue sirviendo.</b> Las lecturas degradan a
/// vacío o nulo —el catálogo, las fichas y el historial no dependen de esto— y solo las
/// escrituras fallan, con el motivo puesto. Un BFF caído no puede apagar una vitrina.</para>
/// </remarks>
public sealed class HttpShopOrderService : IShopOrderService
{
    /// <summary>Cabecera de la llave compartida. La misma que exige toda capacidad.</summary>
    public const string ApiKeyHeader = "X-Synergos-Key";

    /// <summary>Clientes nombrados que registra el composer.</summary>
    public const string BffClientName = "synergos-bff-tienda";

    /// <summary>Ídem para la canasta.</summary>
    public const string CartClientName = "synergos-api-cart";

    /// <summary>El <c>Kind</c> con el que esta tienda nombra a su comprador.</summary>
    internal const string BuyerKind = "tienda.comprador";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IHttpClientFactory _clients;
    private readonly IOptionsMonitor<TiendaSettings> _settings;
    private readonly ILogger<HttpShopOrderService> _log;

    public HttpShopOrderService(
        IHttpClientFactory clients,
        IOptionsMonitor<TiendaSettings> settings,
        ILogger<HttpShopOrderService> log)
    {
        _clients = clients;
        _settings = settings;
        _log = log;
    }

    // ── Comprar ─────────────────────────────────────────────────────────────

    public async Task<ShopCheckoutResult> CheckoutAsync(
        IReadOnlyList<ShopCartItem> items,
        ShopCustomer customer,
        CancellationToken cancellationToken = default)
    {
        if (items is null || items.Count == 0) throw new ArgumentException("El carrito requiere al menos un producto.", nameof(items));
        if (customer is null) throw new ArgumentException("Hace falta el comprador.", nameof(customer));

        var buyerId = BuyerId(customer);

        // La llave ANTES de la primera llamada, y derivada de lo que se compra — no del reloj ni
        // de un Guid nuevo. Con un identificador fresco por intento, un reintento tras un
        // timeout crearía una SEGUNDA compra sobre el mismo carrito: exactamente el cobro doble
        // que esta HU tiene que hacer imposible.
        var key = IdempotencyKeyFor(buyerId, items);

        var cartId = await AbrirCanastaAsync(buyerId, items, key, cancellationToken).ConfigureAwait(false);

        var compra = await ComprarAsync(cartId, key, cancellationToken).ConfigureAwait(false);

        return new ShopCheckoutResult(
            OrderRef: compra.Id,
            // El BFF NO expone el identificador del pago, y hace bien: los identificadores
            // internos de cada capacidad no tienen por qué salir a la UI. Además acá no hay
            // sesión de PSP a la que redirigir — la autorización ocurre servidor adentro, dentro
            // de la compra. Se devuelve el id de la compra, que es lo que la UI necesita para
            // llamar a Confirm.
            PaymentSessionId: compra.Id,
            Amount: compra.Total.Amount,
            Currency: compra.Total.Currency);
    }

    private async Task<string> AbrirCanastaAsync(
        string buyerId, IReadOnlyList<ShopCartItem> items, string key, CancellationToken ct)
    {
        var cart = _clients.CreateClient(CartClientName);

        // Misma llave que la compra: si esto se reintenta, Api.Cart devuelve la canasta que ya
        // había en vez de abrir una segunda con las mismas líneas.
        using var abrir = new HttpRequestMessage(HttpMethod.Post, "v1/carts")
        {
            Content = JsonContent.Create(new { ownerKind = BuyerKind, ownerId = buyerId }),
        };
        abrir.Headers.Add("Idempotency-Key", key);

        var creada = await EnviarAsync<CartDto>(cart, abrir, "abrir la canasta", ct).ConfigureAwait(false);

        foreach (var item in items)
        {
            if (item.Quantity <= 0) throw new ArgumentException($"La cantidad de {item.ProductId} tiene que ser mayor que cero.", nameof(items));

            // La variante viaja DENTRO del subjectId y no en un campo aparte: para Api.Cart una
            // línea es un Ref opaco y una cantidad, y darle un campo «variante» sería meterle un
            // sustantivo de tienda a una capacidad que sirve a veinte dominios.
            using var linea = new HttpRequestMessage(HttpMethod.Post, $"v1/carts/{creada.Id}/lines")
            {
                Content = JsonContent.Create(new
                {
                    subjectKind = "tienda.producto",
                    subjectId = SubjectId(item),
                    quantity = item.Quantity,
                }),
            };
            await EnviarAsync<CartDto>(cart, linea, $"agregar {item.ProductId} a la canasta", ct).ConfigureAwait(false);
        }

        return creada.Id;
    }

    private async Task<PurchaseDto> ComprarAsync(string cartId, string key, CancellationToken ct)
    {
        var bff = _clients.CreateClient(BffClientName);

        using var req = new HttpRequestMessage(HttpMethod.Post, "v1/purchases")
        {
            Content = JsonContent.Create(new { cartId }),
        };
        req.Headers.Add("Idempotency-Key", key);

        try
        {
            return await EnviarAsync<PurchaseDto>(bff, req, "comprar", ct).ConfigureAwait(false);
        }
        catch (InvalidOperationException) when (!ct.IsCancellationRequested)
        {
            // Un timeout no dice «no se cobró»: dice «no sé». Y no saberlo es justamente lo que
            // no podemos permitirnos, así que se PREGUNTA. La llave es el identificador de la
            // saga, o sea que la compra —si llegó a existir— se puede consultar por ella.
            _log.LogWarning("La compra {Key} no respondió; se consulta si llegó a existir.", key);

            var existente = await BuscarCompraAsync(key, ct).ConfigureAwait(false);
            if (existente is not null)
            {
                _log.LogWarning("La compra {Key} SÍ existía: se sigue con ella en vez de crear otra.", key);
                return existente;
            }
            throw;
        }
    }

    // ── Confirmar ───────────────────────────────────────────────────────────

    public async Task<ShopConfirmationResult> ConfirmAsync(
        string orderRef, ShopShippingAddress? shipTo = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(orderRef)) throw new ArgumentException("Hace falta la referencia de la orden.", nameof(orderRef));

        // Confirmar acá CAPTURA y DESPACHA, así que la dirección no es opcional por más que la
        // interfaz la deje pasar. Se comprueba antes de llamar: descubrir que falta después de
        // mover plata costaría una devolución por un campo de formulario.
        if (shipTo is null || string.IsNullOrWhiteSpace(shipTo.Line1) || string.IsNullOrWhiteSpace(shipTo.City))
        {
            throw new ArgumentException(
                "Hace falta una dirección de entrega con línea y ciudad para confirmar la compra.", nameof(shipTo));
        }

        var s = _settings.CurrentValue;

        using var req = new HttpRequestMessage(HttpMethod.Post, $"v1/purchases/{Uri.EscapeDataString(orderRef)}/confirm")
        {
            Content = JsonContent.Create(new
            {
                to = new
                {
                    line1 = shipTo.Line1,
                    line2 = shipTo.Line2,
                    city = shipTo.City,
                    region = shipTo.Region,
                    postalCode = shipTo.PostalCode,
                    country = shipTo.Country,
                    contact = shipTo.Contact,
                },
                carrier = string.IsNullOrWhiteSpace(s.Carrier) ? "default" : s.Carrier,
            }),
        };

        var compra = await EnviarAsync<PurchaseDto>(
            _clients.CreateClient(BffClientName), req, "confirmar la compra", cancellationToken).ConfigureAwait(false);

        return new ShopConfirmationResult(
            OrderRef: compra.Id,
            OrderNumber: compra.OrderId ?? compra.Id,
            Status: Estado(compra.Status),
            // Las líneas NO salen del BFF: su respuesta lleva el total y el estado, no el detalle
            // —la canasta es de Api.Cart—. Devolver vacío es más honesto que inventar: la UI
            // pinta el resumen con lo que ya tenía en pantalla.
            Lines: Array.Empty<ShopOrderLine>(),
            Total: compra.Total.Amount,
            Currency: compra.Total.Currency);
    }

    // ── Leer ────────────────────────────────────────────────────────────────

    public async Task<ShopOrder?> GetOrderAsync(string orderRef, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(orderRef)) return null;

        using var req = new HttpRequestMessage(HttpMethod.Get, $"v1/purchases/{Uri.EscapeDataString(orderRef)}");
        var compra = await LeerAsync<PurchaseDto>(
            _clients.CreateClient(BffClientName), req, cancellationToken).ConfigureAwait(false);

        return compra is null ? null : AOrden(compra);
    }

    /// <summary>
    /// «Mis compras» por comprador. <b>Devuelve vacío, y es una decisión, no un pendiente.</b>
    /// </summary>
    /// <remarks>
    /// <para><c>Bff.Tienda</c> resuelve por id y <b>no lista por comprador</b> — es el hueco que
    /// la propia HU #24 señala. Se decidió NO abrir la flecha del CMS a <c>Api.Orders</c> para
    /// taparlo: sería el primer sitio donde el CMS habla con una capacidad de Tienda saltándose
    /// al orquestador, y el gate que impide exactamente eso existe por una razón que no cambia
    /// porque el caso sea de lectura.</para>
    ///
    /// <para><b>Vacío degrada; una lista equivocada miente.</b> Con el modo <c>Bff</c> encendido,
    /// «mis compras» sale vacío hasta que <c>Bff.Tienda</c> tenga por dónde listarlas. Está
    /// anotado en el ticket.</para>
    /// </remarks>
    public Task<IReadOnlyList<ShopOrder>> GetOrdersByMemberAsync(Guid memberKey, CancellationToken cancellationToken = default)
    {
        _log.LogDebug("Bff.Tienda no lista compras por comprador todavía; el historial sale vacío.");
        return Task.FromResult<IReadOnlyList<ShopOrder>>(Array.Empty<ShopOrder>());
    }

    /// <inheritdoc cref="GetOrdersByMemberAsync" />
    public Task<IReadOnlyList<ShopOrder>> GetOrdersAsync(string customerEmail, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<ShopOrder>>(Array.Empty<ShopOrder>());

    private async Task<PurchaseDto?> BuscarCompraAsync(string sagaId, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"v1/purchases/{Uri.EscapeDataString(sagaId)}");
        return await LeerAsync<PurchaseDto>(_clients.CreateClient(BffClientName), req, ct).ConfigureAwait(false);
    }

    // ── El cable ────────────────────────────────────────────────────────────

    /// <summary>Una escritura: o sale bien, o se traduce el motivo. Nunca «éxito» a secas.</summary>
    private async Task<T> EnviarAsync<T>(HttpClient http, HttpRequestMessage req, string queHacia, CancellationToken ct)
    {
        HttpResponseMessage res;
        try
        {
            res = await http.SendAsync(req, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;   // el comprador cerró la pestaña; no es un fallo del servicio
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            // NO se traga la excepción. Un fallo de red que devolviera «compra exitosa» es el
            // defecto que esta HU nombra como el peor posible.
            _log.LogError(ex, "No se pudo {Que}: el orquestador de Tienda no respondió.", queHacia);
            throw new InvalidOperationException(
                "No pudimos procesar tu compra. No se te cobró.", ex);
        }

        using (res)
        {
            if (res.IsSuccessStatusCode)
            {
                var cuerpo = await res.Content.ReadFromJsonAsync<T>(Json, ct).ConfigureAwait(false);
                return cuerpo ?? throw new InvalidOperationException($"No pudimos {queHacia}: la respuesta vino vacía.");
            }

            var problema = await LeerProblemaAsync(res, ct).ConfigureAwait(false);

            if (res.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                // Es un defecto de DESPLIEGUE, no del visitante: la llave compartida está mal o
                // no está. Se grita en el log y afuera sale un error genérico — el comprador no
                // puede hacer nada con «401», y el detalle no es suyo.
                _log.LogError(
                    "Tienda respondió {Status} al {Que}: la llave compartida es inválida o falta. "
                    + "Revisar Synergos:Tienda:ApiKey.", (int)res.StatusCode, queHacia);
                throw new InvalidOperationException("No pudimos procesar tu compra. No se te cobró.");
            }

            _log.LogWarning("Tienda rechazó {Que} con {Status} ({Code}): {Detalle}",
                queHacia, (int)res.StatusCode, problema.Code ?? "-", problema.Detail ?? "-");

            // El motivo del rechazo SÍ es del comprador: «se agotó mientras comprabas» es
            // accionable y «error» no lo es. Va como ArgumentException porque es lo que el
            // controller traduce a 400 con el mensaje visible.
            throw new ArgumentException(
                string.IsNullOrWhiteSpace(problema.Detail) ? $"No pudimos {queHacia}." : problema.Detail!);
        }
    }

    /// <summary>Una lectura: si no está o no responde, es null. Nunca revienta la página.</summary>
    private async Task<T?> LeerAsync<T>(HttpClient http, HttpRequestMessage req, CancellationToken ct)
        where T : class
    {
        try
        {
            using var res = await http.SendAsync(req, ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
            {
                if (res.StatusCode != HttpStatusCode.NotFound)
                {
                    _log.LogWarning("Tienda respondió {Status} a {Url}.", (int)res.StatusCode, req.RequestUri);
                }
                return null;
            }
            return await res.Content.ReadFromJsonAsync<T>(Json, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Tienda no respondió a {Url}; se sirve sin ese dato.", req.RequestUri);
            return null;
        }
    }

    private static async Task<ProblemDto> LeerProblemaAsync(HttpResponseMessage res, CancellationToken ct)
    {
        try
        {
            return await res.Content.ReadFromJsonAsync<ProblemDto>(Json, ct).ConfigureAwait(false) ?? new ProblemDto();
        }
        catch (Exception ex) when (ex is JsonException or HttpRequestException or NotSupportedException)
        {
            return new ProblemDto();
        }
    }

    // ── Traducciones ────────────────────────────────────────────────────────

    /// <summary>Quién compra, en el vocabulario del árbol de servicios.</summary>
    /// <remarks>
    /// El <c>memberKey</c> cuando hay sesión —es la identidad de confianza-servidor— y el correo
    /// normalizado en el checkout de invitado. Nunca el nombre: dos personas se llaman igual.
    /// </remarks>
    internal static string BuyerId(ShopCustomer customer)
        => customer.MemberKey is Guid k && k != Guid.Empty
            ? k.ToString("n")
            : (customer.Email ?? string.Empty).Trim().ToLowerInvariant();

    internal static string SubjectId(ShopCartItem item)
        => string.IsNullOrWhiteSpace(item.VariantId) ? item.ProductId : $"{item.ProductId}:{item.VariantId}";

    /// <summary>
    /// La llave de idempotencia: <b>determinista sobre lo que se compra</b>, no sobre cuándo.
    /// </summary>
    /// <remarks>
    /// Es lo que hace que un reintento tras un timeout no cree una segunda compra. Entran el
    /// comprador y las líneas ORDENADAS —así reordenar la canasta en pantalla no cambia la
    /// llave— y sale un hash estable. Que dos compras idénticas del mismo comprador compartan
    /// llave es intencional: sin un carrito real de por medio, es indistinguible de un reintento,
    /// y ante la duda se prefiere no cobrar dos veces.
    /// </remarks>
    internal static string IdempotencyKeyFor(string buyerId, IReadOnlyList<ShopCartItem> items)
    {
        var partes = items
            .Select(i => $"{SubjectId(i)}x{i.Quantity}")
            .OrderBy(x => x, StringComparer.Ordinal);

        var semilla = $"{buyerId}|{string.Join('|', partes)}";
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(semilla));
        return "shop-" + Convert.ToHexString(hash)[..32].ToLowerInvariant();
    }

    private static string Estado(string? sagaStatus) => sagaStatus switch
    {
        "Completed" => "Paid",
        "Compensated" or "Failed" => "Cancelled",
        _ => "Pending",
    };

    private static ShopOrder AOrden(PurchaseDto c) => new(
        OrderRef: c.Id,
        OrderNumber: c.OrderId ?? c.Id,
        Status: Estado(c.Status) switch
        {
            "Paid" => OrderStatus.Paid,
            "Cancelled" => OrderStatus.Cancelled,
            _ => OrderStatus.Pending,
        },
        CustomerName: string.Empty,
        CustomerEmail: c.BuyerId ?? string.Empty,
        Lines: Array.Empty<ShopOrderLine>(),
        Total: c.Total.Amount,
        Currency: c.Total.Currency,
        PaymentSessionId: c.Id,
        CreatedAt: DateTimeOffset.MinValue,
        OwnerMemberKey: null);

    // Los DTO viven acá y NO en Synergos.CMS.Interfaces: son la forma del contrato HTTP con otro
    // servicio, no vocabulario del dominio del CMS.

    private sealed record CartDto(string Id);

    private sealed record MoneyDto(decimal Amount, string Currency);

    private sealed record PurchaseDto(
        string Id, string? BuyerKind, string? BuyerId, string? CartId, string? Status,
        MoneyDto Total, string? OrderId, string? ShipmentId, int HeldLines,
        int PendingCompensations, string? LastError);

    private sealed record ProblemDto
    {
        public string? Title { get; init; }
        public string? Detail { get; init; }
        public string? Code { get; init; }
    }
}
