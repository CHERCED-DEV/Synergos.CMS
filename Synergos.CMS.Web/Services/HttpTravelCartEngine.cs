using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// El motor del carrito de viaje que compra de verdad — contra <c>Synergos.Bff.Viajes</c> (HU #40).
/// </summary>
/// <remarks>
/// <para><b>Contra el ORQUESTADOR, no contra las capacidades</b>, y acá el argumento es más
/// fuerte que en la vía hotel: un carrito son varios apartados heterogéneos sobre varios recursos
/// con varias ventanas, y el fallo puede llegar <i>después</i> de haber confirmado los primeros.
/// Llamando a <c>Api.Booking</c> y <c>Api.Payments</c> por separado, el CMS estaría
/// reimplementando la máquina de sagas sin tener dónde anotar una compensación pendiente.</para>
///
/// <para><b>Pide confirmación PARCIAL, y ésa es la decisión de este motor.</b> Quien compró un
/// vuelo, un hotel y un auto no pierde el vuelo porque el auto se agotó — es la misma política
/// que ya tenía <see cref="Synergos.CMS.Application.Services.Impl.InProcessTravelCartEngine"/>,
/// y mantenerla es lo que hace que cambiar de motor no cambie lo que le pasa a quien compra. El
/// modo todo-o-nada existe y es el default del orquestador: no se usa acá a propósito.</para>
///
/// <para><b>Y por eso este motor ORDENA la devolución de lo no cumplido.</b> El orquestador no
/// puede calcularla: cotiza el viaje entero de una vez, así que no sabe cuánto vale el ítem
/// caído. El CMS sí —tiene el precio de cada línea— y por eso la manda ya calculada, igual que
/// manda la penalidad de cancelar. Sin ese paso, cablear el carrito habría sido un retroceso
/// frente al motor en proceso: el ítem caído se soltaba y la plata se quedaba acá.</para>
///
/// <para><b>El producto viaja como el <c>OfferId</c> tal cual</b>, sin prefijo de tipo. No es
/// comodidad: la vía hotel ya usa <c>tipo/tarifa</c> como producto, que es exactamente el
/// <c>OfferId</c> de una línea de hotel del carrito. Prefijarlo acá haría que la misma habitación
/// fuera dos recursos distintos en <c>Api.Booking</c> — y dos pozos para un solo cuarto es
/// sobreventa garantizada.</para>
///
/// <para><b>Consecuencia de DESPLIEGUE</b>, la misma de la vía hotel: cada oferta del catálogo
/// necesita su recurso dado de alta en <c>Api.Booking</c> y su precio en <c>Api.Pricing</c>, o
/// apartar se rechaza con <c>booking.resource_not_found</c> / <c>pricing.price_not_found</c>.
/// Está dicho en <c>.env.example</c>.</para>
/// </remarks>
public sealed class HttpTravelCartEngine : ITravelCartEngine
{
    /// <summary>Cliente nombrado que registra el composer. El mismo de la vía hotel.</summary>
    public const string ClientName = ViajesWire.ClientName;

    private readonly ViajesWire _wire;
    private readonly IOptionsMonitor<ViajesSettings> _settings;
    private readonly ILogger<HttpTravelCartEngine> _log;

    public HttpTravelCartEngine(
        IHttpClientFactory clients,
        IOptionsMonitor<ViajesSettings> settings,
        ILogger<HttpTravelCartEngine> log)
    {
        // Quien está comprando un carrito necesita saber que NO se le cobró: es lo único
        // accionable de un fallo de red, y callarlo lo deja sin saber si repetir.
        _wire = new ViajesWire(clients, log, "No pudimos procesar tu compra. No se te cobró.");
        _settings = settings;
        _log = log;
    }

    // ── Apartar ─────────────────────────────────────────────────────────────

    public async Task<TravelCartHold> HoldAllAsync(
        IReadOnlyList<TravelCartItem> items,
        TravelGuest guest,
        string orderRef,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(guest);

        var s = _settings.CurrentValue;

        using var req = new HttpRequestMessage(HttpMethod.Post, "v1/trips")
        {
            Content = JsonContent.Create(new
            {
                travellerKind = s.TravellerKind,
                travellerId = ViajesWire.TravellerId(guest.Email),
                // Lo que hace de este carrito un carrito y no un paquete: si un ítem no se puede
                // confirmar tras cobrar, se conserva el resto y se marca el caído.
                partialConfirm = true,
                items = items.Select(i => new
                {
                    productRef = i.OfferId,
                    productLabel = i.Label,
                    start = i.Start,
                    end = i.End,
                }).ToList(),
            }),
        };

        // La llave ES el orderRef, y el orquestador la usa como identificador de la saga. Sirve
        // para lo único que hace falta acá: que un reintento tras un timeout no aparte el carrito
        // dos veces. No hace falta derivarla de lo que se compra —como sí hace la vía hotel—
        // porque el orderRef ya es único por intento de compra y lo genera este lado.
        req.Headers.Add("Idempotency-Key", orderRef);

        var viaje = await _wire.SendAsync<TripDto>(req, "apartar tu viaje", cancellationToken).ConfigureAwait(false);

        // El total lo dice el orquestador, que cotizó contra Api.Pricing. Aceptar el del carrito
        // dejaría comprar la suite al precio de la estándar.
        return new TravelCartHold(
            EngineRef: viaje.Id,
            // El orquestador no expone el identificador del pago —es interno de Api.Payments— así
            // que la compra se liga con el de la saga, que es lo que sí identifica esta
            // transacción de punta a punta. Mismo criterio que la vía hotel.
            PaymentSessionId: viaje.Id,
            Total: viaje.Total.Amount,
            Currency: viaje.Total.Currency,
            Items: items
                // Sin identificador de reserva: Api.Booking los genera y el orquestador NO los
                // expone, a propósito. Devolver el de la saga en su lugar sería una mentira con
                // forma de dato, y alguien acabaría cableándolo río arriba.
                .Select(i => new TravelCartHeldItem(i.OfferId, string.Empty))
                .ToList());
    }

    // ── Cobrar y confirmar ──────────────────────────────────────────────────

    public async Task<TravelCartSettlement> SettleAsync(
        string? engineRef,
        string paymentSessionId,
        IReadOnlyList<TravelCartSettledLine> lines,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var viajeId = Viaje(engineRef, paymentSessionId);

        TripDto viaje;
        try
        {
            using var req = new HttpRequestMessage(
                HttpMethod.Post, $"v1/trips/{Uri.EscapeDataString(viajeId)}/confirm");
            viaje = await _wire.SendAsync<TripDto>(req, "confirmar tu viaje", cancellationToken).ConfigureAwait(false);
        }
        catch (ViajesRejectedException ex)
        {
            // El viaje NO salió, y la saga ya deshizo lo suyo: soltó los apartados y devolvió lo
            // que se hubiera movido. Se reporta como «nada cumplido» —que es la verdad— y quien
            // vendió sella su orden como cancelada. Un rechazo de negocio es distinto de una
            // caída: eso lo lanza el cable y NO llega acá, porque de una caída no sabemos si el
            // viaje quedó hecho.
            _log.LogWarning("El viaje {Viaje} no se pudo confirmar ({Code}): {Motivo}",
                viajeId, ex.Code ?? "-", ex.Message);

            return new TravelCartSettlement(
                lines.Select(l => new TravelCartSettledItem(
                    l.OfferId, l.ReservationId, ReservationStatus.Cancelled.ToString())).ToList(),
                lines.Sum(l => l.Price));
        }

        // Un 200 que no sea Completed no es un viaje confirmado. Darlo por bueno sería lo peor
        // que puede pasar acá: el viajero llega y no hay nada reservado.
        if (!string.Equals(viaje.Status, "Completed", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                viaje.LastError ?? $"No pudimos confirmar tu viaje (quedó en {viaje.Status ?? "?"}).");
        }

        var caidos = (viaje.Items ?? Array.Empty<TripItemDto>())
            .Where(i => i.Unfulfilled || !i.Confirmed)
            .Select(i => i.ProductRef)
            .ToHashSet(StringComparer.Ordinal);

        var resultado = new List<TravelCartSettledItem>(lines.Count);
        var noCumplido = 0m;
        foreach (var line in lines)
        {
            var cumplido = !caidos.Contains(line.OfferId);
            if (!cumplido) noCumplido += line.Price;

            resultado.Add(new TravelCartSettledItem(
                line.OfferId,
                line.ReservationId,
                (cumplido ? ReservationStatus.Confirmed : ReservationStatus.Cancelled).ToString()));
        }

        // Y acá se ordena la devolución de lo que no se entregó. Best-effort por la misma razón
        // que en el motor en proceso: lo que SÍ salió ya está confirmado y una devolución caída
        // no puede deshacer un viaje confirmado. Pero se intenta siempre y queda dicho si falla —
        // no intentarlo es el defecto que esto cierra.
        if (noCumplido > 0m)
        {
            await DevolverAsync(viajeId, noCumplido, MonedaDe(lines), cancellationToken).ConfigureAwait(false);
        }

        return new TravelCartSettlement(resultado, noCumplido);
    }

    /// <summary>Ordena devolver lo que no se pudo entregar.</summary>
    /// <remarks>
    /// La llave se deriva del viaje y del monto: repetir la misma orden no devuelve dos veces, y
    /// una orden por otro monto —otro ítem caído en otro intento— sí es otra operación. Es la
    /// misma regla que el ajuste relativo de <c>Api.Inventory</c> (#30): lo relativo exige llave.
    /// </remarks>
    private async Task DevolverAsync(string viajeId, decimal monto, string moneda, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(
                HttpMethod.Post, $"v1/trips/{Uri.EscapeDataString(viajeId)}/refund")
            {
                Content = JsonContent.Create(new
                {
                    amount = new { amount = monto, currency = moneda },
                    reason = "ítems del carrito no cumplidos",
                }),
            };
            req.Headers.Add("Idempotency-Key", $"{viajeId}:refund:{monto}");

            await _wire.SendAsync<TripDto>(req, "devolver lo no cumplido", ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ViajesRejectedException or InvalidOperationException)
        {
            // Queda dicho y no se propaga: el viaje ya está confirmado y sellado de este lado, y
            // tumbarlo por una devolución fallida le quitaría al comprador lo que SÍ recibió.
            _log.LogError(ex, "El viaje {Viaje} no pudo devolver {Monto} {Moneda} de los ítems no cumplidos.",
                viajeId, monto, moneda);
        }
    }

    // ── Soltar ──────────────────────────────────────────────────────────────

    public async Task<TravelCartRelease> ReleaseAsync(
        string? engineRef,
        string paymentSessionId,
        IReadOnlyList<TravelCartSettledLine> lines,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var viajeId = Viaje(engineRef, paymentSessionId);

        // Sin penalidad: la política del carrito multi-producto es devolver todo (MMB v1), y el
        // orquestador retiene lo que se le diga. El día que el carrito tenga política de
        // cancelación, se calcula ACÁ y viaja ya calculada — como en la vía hotel.
        using var req = new HttpRequestMessage(
            HttpMethod.Post, $"v1/trips/{Uri.EscapeDataString(viajeId)}/cancel");

        var viaje = await _wire.SendAsync<TripDto>(req, "cancelar tu viaje", cancellationToken).ConfigureAwait(false);

        // Lo que el orquestador no pudo deshacer sale en su `lastError`: una devolución colgada
        // tiene que verse, no quedarse en su log.
        if (viaje.LastError is { Length: > 0 } malo)
        {
            _log.LogError("El viaje {Viaje} se canceló con algo colgando: {Motivo}", viajeId, malo);
        }

        // Cuánto volvió lo dice el orquestador, que se lo preguntó a Api.Payments. Deducirlo del
        // total sería adivinar: puede haberse devuelto algo antes por un ítem no cumplido.
        return viaje.Refunded is { } d && d.Amount > 0m
            ? new TravelCartRelease(true, d.Amount)
            : new TravelCartRelease(false, 0m);
    }

    // ── Traducciones ────────────────────────────────────────────────────────

    /// <summary>
    /// Con qué identificador se le vuelve a hablar al viaje.
    /// </summary>
    /// <remarks>
    /// El respaldo es para las órdenes que se apartaron ANTES de que el expediente guardara la
    /// referencia del motor: en este motor las dos valen lo mismo, así que una orden vieja se
    /// sigue pudiendo confirmar y cancelar. Sin él, cambiar de versión dejaría carritos a medias
    /// sin forma de cerrarlos.
    /// </remarks>
    private static string Viaje(string? engineRef, string paymentSessionId)
        => string.IsNullOrWhiteSpace(engineRef) ? paymentSessionId : engineRef!;

    /// <summary>
    /// La moneda del carrito. Una sola por carrito, y quien vende ya lo validó.
    /// </summary>
    /// <remarks>
    /// Va en la línea y no en un default: mandar «COP» por costumbre haría que un carrito en
    /// dólares ordenara una devolución en pesos, y el rechazo llegaría de <c>Api.Payments</c> con
    /// la plata ya no devuelta. Vacío no puede llegar —el borde lo exige al armar el carrito—
    /// pero si llegara, es mejor que el orquestador rechace que devolver en la moneda equivocada.
    /// </remarks>
    private static string MonedaDe(IReadOnlyList<TravelCartSettledLine> lines)
        => lines.Count > 0 ? lines[0].Currency : string.Empty;
}
