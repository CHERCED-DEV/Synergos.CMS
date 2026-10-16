using System.Text.Json;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// El <see cref="ITiendaOfferPublisher"/> que publica la oferta en <c>Synergos.Bff.Tienda</c>
/// (<c>POST v1/ofertas</c>, 004-tienda-compra R-9). El molde de <see cref="HttpEventOfferPublisher"/>.
/// </summary>
/// <remarks>
/// <para><b>Manda lo que el catálogo sirve y nada más</b>: el precio y las existencias de cada variante de la
/// ficha, o del producto si no tiene variantes. Una variante en otra moneda que la del producto no cabe en
/// una oferta de una sola moneda: no se manda, así que deja de venderse por el orquestador, y el log lo dice.
/// Unas existencias negativas se mandan como cero: es «no hay», no un error del editor que tumbe la oferta
/// entera.</para>
///
/// <para><b>Sin destino no intenta</b>: un clon limpio no tiene orquestador. Con destino y el orquestador
/// caído, deja el motivo en el log y devuelve <see cref="TiendaOfferOutcome.Failed"/>: nunca lanza, porque
/// quien llama es el publicar del editor.</para>
/// </remarks>
public sealed class HttpTiendaOfferPublisher : ITiendaOfferPublisher
{
    /// <summary>Cliente nombrado que registra el composer, hacia el mismo destino que la compra.</summary>
    public const string ClientName = "synergos-bff-tienda-oferta";

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly IHttpClientFactory _clients;
    private readonly ILogger<HttpTiendaOfferPublisher> _log;
    private readonly bool _hayDestino;

    /// <param name="clients">La fábrica de clientes; el de <see cref="ClientName"/> lo arma la pieza del árbol.</param>
    /// <param name="log">Dónde queda lo que no se pudo publicar.</param>
    /// <param name="hayDestino">Si el despliegue configuró dónde vive el orquestador de la tienda.</param>
    public HttpTiendaOfferPublisher(IHttpClientFactory clients, ILogger<HttpTiendaOfferPublisher> log, bool hayDestino)
    {
        _clients = clients;
        _log = log;
        _hayDestino = hayDestino;
    }

    public Task<TiendaOfferOutcome> PublishAsync(ProductDetail producto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(producto);

        var variantes = Variantes(producto);

        // Sin nada que vender en su moneda, lo que se hubiera publicado antes se retira entero (el
        // orquestador exige al menos una variante en una oferta).
        if (variantes.Count == 0) return RetireAsync(producto.Product.Id, cancellationToken);

        return EnviarAsync(producto.Product.Id, "publicar", () => new HttpRequestMessage(HttpMethod.Post, "v1/ofertas")
        {
            Content = JsonContent.Create(new { productId = producto.Product.Id, currency = producto.Product.Currency, variants = variantes }),
        }, cancellationToken);
    }

    public Task<TiendaOfferOutcome> RetireAsync(string productId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productId);
        return EnviarAsync(productId, "retirar", () => new HttpRequestMessage(
            HttpMethod.Post, $"v1/ofertas/{Uri.EscapeDataString(productId)}/retirar"), cancellationToken);
    }

    private async Task<TiendaOfferOutcome> EnviarAsync(
        string producto, string que, Func<HttpRequestMessage> peticion, CancellationToken cancellationToken)
    {
        if (!_hayDestino)
        {
            _log.LogDebug("La oferta de {Producto} no se {Que}: no hay orquestador de la tienda configurado.", producto, que);
            return TiendaOfferOutcome.NoDestination;
        }

        using var req = peticion();
        // Una llave por publicación: los reintentos de la cadena repiten ESTA petición, y una publicación
        // nueva —otro publicar, o republicar— es otra llave, que el orquestador espera.
        req.Headers.Add("Idempotency-Key", "oferta-" + Guid.NewGuid().ToString("n"));

        try
        {
            using var res = await _clients.CreateClient(ClientName).SendAsync(req, cancellationToken).ConfigureAwait(false);
            if (res.IsSuccessStatusCode)
            {
                _log.LogInformation("Oferta de {Producto}: {Que} en el orquestador, hecho.", producto, que);
                return TiendaOfferOutcome.Published;
            }

            var rechazo = await RechazoDelArbolDeServicios.LeerAsync(res, Json, cancellationToken).ConfigureAwait(false);
            _log.LogWarning(
                "El orquestador no pudo {Que} la oferta de {Producto}: {Estado} {Codigo} {Detalle}. Se repara republicando.",
                que, producto, (int)res.StatusCode, rechazo?.Codigo, rechazo?.Detalle);
            return TiendaOfferOutcome.Failed;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            _log.LogWarning(ex, "El orquestador de la tienda no respondió al {Que} la oferta de {Producto}. Se repara republicando.",
                que, producto);
            return TiendaOfferOutcome.Failed;
        }
    }

    /// <summary>Las variantes del cuerpo de <c>PublishProductOffer</c>, con los nombres de su contrato.</summary>
    private List<object> Variantes(ProductDetail p)
    {
        if (p.Variants.Count == 0)
        {
            return [new { variantId = (string?)null, price = p.Product.Price, stock = Math.Max(0, p.Product.Stock) }];
        }

        var enSuMoneda = new List<object>(p.Variants.Count);
        foreach (var v in p.Variants)
        {
            if (!string.Equals(v.Currency, p.Product.Currency, StringComparison.OrdinalIgnoreCase))
            {
                _log.LogWarning(
                    "La variante {Variante} de {Producto} está en {Moneda} y el producto en {MonedaDelProducto}: no entra en su oferta y deja de venderse por el orquestador.",
                    v.VariantId, p.Product.Id, v.Currency, p.Product.Currency);
                continue;
            }
            enSuMoneda.Add(new { variantId = (string?)v.VariantId, price = v.Price, stock = Math.Max(0, v.Stock) });
        }
        return enSuMoneda;
    }
}
