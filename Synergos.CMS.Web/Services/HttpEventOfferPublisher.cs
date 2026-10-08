using System.Text.Json;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// El <see cref="IEventOfferPublisher"/> que publica la oferta en <c>Synergos.Bff.Eventos</c>
/// (<c>POST v1/ofertas</c>, ADR 0140 F3).
/// </summary>
/// <remarks>
/// <para><b>Manda lo que el contenido ya decidió y nada más.</b> La ventana de cada localidad
/// (<see cref="EventTier.SaleOpensUtc"/>, <see cref="EventTier.SaleClosesUtc"/>) y el inicio
/// (<see cref="EventSummary.StartUtc"/>) son los que <c>EventContentRules</c> calculó en la zona del
/// sitio: acá no se vuelve a convertir ninguna fecha. Cortar la venta en el inicio lo hace el
/// orquestador, que es quien publica la vigencia del precio.</para>
///
/// <para><b>El aforo es lo que el contenido OFRECE</b>: <see cref="EventTier.Remaining"/>, que en el
/// contenido del editor es el aforo entero y en la demo descuenta lo que la siembra da por vendido.
/// Por la misma razón, de una localidad con mapa salen sus butacas libres —una por pozo de 1— y no
/// las que el mapa marca <c>sold</c>: lo que la ficha pinta como vendido no se vende por el otro
/// camino.</para>
///
/// <para><b>Sin destino no intenta</b>: un clon limpio no tiene orquestador, y salir a buscarlo en
/// cada publicar del editor sería esperar a la red para nada. Con destino y el orquestador caído,
/// deja el motivo en el log y devuelve <see cref="EventOfferOutcome.Failed"/>: nunca lanza, porque
/// quien llama es el publicar del editor.</para>
/// </remarks>
public sealed class HttpEventOfferPublisher : IEventOfferPublisher
{
    /// <summary>Cliente nombrado que registra el composer, hacia el mismo destino que la compra.</summary>
    public const string ClientName = "synergos-bff-eventos-oferta";

    private const string Vendida = "sold";

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly IHttpClientFactory _clients;
    private readonly ILogger<HttpEventOfferPublisher> _log;
    private readonly bool _hayDestino;

    /// <param name="clients">La fábrica de clientes; el de <see cref="ClientName"/> lo arma la pieza del árbol.</param>
    /// <param name="log">Dónde queda lo que no se pudo publicar.</param>
    /// <param name="hayDestino">Si el despliegue configuró dónde vive el orquestador.</param>
    public HttpEventOfferPublisher(IHttpClientFactory clients, ILogger<HttpEventOfferPublisher> log, bool hayDestino)
    {
        _clients = clients;
        _log = log;
        _hayDestino = hayDestino;
    }

    public async Task<EventOfferOutcome> PublishAsync(EventDetail evento, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(evento);

        if (!_hayDestino)
        {
            _log.LogDebug("La oferta de {Evento} no se publica: no hay orquestador de Eventos configurado.", evento.Summary.Id);
            return EventOfferOutcome.NoDestination;
        }

        using var req = new HttpRequestMessage(HttpMethod.Post, "v1/ofertas") { Content = JsonContent.Create(Oferta(evento)) };
        // Una llave por publicación: los reintentos de la cadena repiten ESTA petición, y una
        // publicación nueva —otro publicar, o republicar— es otra llave, que el orquestador espera.
        req.Headers.Add("Idempotency-Key", "oferta-" + Guid.NewGuid().ToString("n"));

        try
        {
            using var res = await _clients.CreateClient(ClientName).SendAsync(req, cancellationToken).ConfigureAwait(false);
            if (res.IsSuccessStatusCode)
            {
                _log.LogInformation("Oferta de {Evento} publicada en el orquestador.", evento.Summary.Id);
                return EventOfferOutcome.Published;
            }

            var rechazo = await RechazoDelArbolDeServicios.LeerAsync(res, Json, cancellationToken).ConfigureAwait(false);
            _log.LogWarning(
                "El orquestador no publicó la oferta de {Evento}: {Estado} {Codigo} {Detalle}. Se repara republicando.",
                evento.Summary.Id, (int)res.StatusCode, rechazo?.Codigo, rechazo?.Detalle);
            return EventOfferOutcome.Failed;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            _log.LogWarning(ex, "El orquestador de Eventos no respondió al publicar la oferta de {Evento}. Se repara republicando.",
                evento.Summary.Id);
            return EventOfferOutcome.Failed;
        }
    }

    /// <summary>El cuerpo de <c>PublishEventOffer</c>, con los nombres del contrato del orquestador.</summary>
    private static object Oferta(EventDetail e) => new
    {
        eventId = e.Summary.Id,
        currency = e.Summary.Currency,
        startsAtUtc = e.Summary.StartUtc,
        tiers = e.Tiers.Select(t =>
        {
            var butacas = Butacas(e.SeatMap, t.Code);
            return new
            {
                code = t.Code,
                price = t.Price,
                // Cero no es «sin tope» en el contenido —EventContentRules pone diez—, pero si llega,
                // el orquestador lo rechazaría como tope inválido: se manda como ausente.
                maxPerOrder = t.MaxPerOrder > 0 ? t.MaxPerOrder : (int?)null,
                capacity = butacas?.Count ?? t.Remaining,
                seats = butacas,
                saleOpensUtc = t.SaleOpensUtc,
                saleClosesUtc = t.SaleClosesUtc,
            };
        }).ToList(),
    };

    /// <summary>Las butacas libres de una localidad con mapa, o nulo si se vende por cantidad.</summary>
    private static List<string>? Butacas(EventSeatMap? mapa, string localidad)
    {
        var zonas = mapa?.Zones.Where(z => string.Equals(z.TierCode, localidad, StringComparison.Ordinal)).ToList();
        if (zonas is not { Count: > 0 }) return null;

        return zonas
            .SelectMany(z => z.Rows)
            .SelectMany(r => r.Seats)
            .Where(s => !string.Equals(s.Status, Vendida, StringComparison.OrdinalIgnoreCase))
            .Select(s => s.Id)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }
}
