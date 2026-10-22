using System.Globalization;
using System.Text.Json;

namespace Synergos.CMS.Web.Services.Reservas;

/// <summary>Cómo terminó publicar o retirar una oferta de reserva.</summary>
public enum EstadoDeLaOferta
{
    /// <summary>El orquestador la tomó.</summary>
    Hecha,

    /// <summary>El despliegue no dice dónde vive <c>Bff.Reservas</c>: no se intentó.</summary>
    SinDestino,

    /// <summary>El orquestador la rechazó o no respondió; el motivo va en el resultado.</summary>
    Fallida,
}

/// <summary>
/// Lo que pasó con una oferta: el estado y, si falló, el código y el detalle del orquestador. Publicada, cuántas
/// reservas vigentes dejó fuera el horario o los cupos nuevos (la agenda no las cancela: el editor tiene que saberlo).
/// </summary>
public sealed record ResultadoDeLaOferta(EstadoDeLaOferta Estado, string? Codigo = null, string? Detalle = null, int ReservasFuera = 0)
{
    public static readonly ResultadoDeLaOferta Hecha = new(EstadoDeLaOferta.Hecha);
    public static readonly ResultadoDeLaOferta SinDestino = new(EstadoDeLaOferta.SinDestino);
}

/// <summary>
/// Publica y retira la oferta de un servicio reservable en <c>Synergos.Bff.Reservas</c>
/// (<c>POST v1/ofertas</c> y <c>POST v1/ofertas/{servicio}/retirar</c>, 011-booking-reserva).
/// </summary>
/// <remarks>
/// <para><b>Manda lo que la ficha declara y nada más</b>: la duración, los días con su horario (un bloque por día,
/// de la apertura al cierre), los cupos, el plazo de cancelación y el precio de la ficha con la moneda y la zona
/// del sitio. La agenda y los precios los registra el orquestador; el CMS no habla con las capacidades.</para>
///
/// <para><b>Sin destino no intenta</b>, como la oferta de Eventos: un clon limpio no tiene orquestador. Con destino
/// y el orquestador caído o en desacuerdo, devuelve <see cref="EstadoDeLaOferta.Fallida"/> con su código —quien
/// llama lo dice en el backoffice— y nunca lanza, porque quien llama es el publicar del editor.</para>
/// </remarks>
public sealed class PublicadorDeOfertasDeReservas
{
    /// <summary>Cliente nombrado que registra el composer, hacia <c>Synergos:Reservas:BaseUrl</c>.</summary>
    public const string ClientName = "synergos-bff-reservas-oferta";

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly IHttpClientFactory _clients;
    private readonly ILogger<PublicadorDeOfertasDeReservas> _log;
    private readonly bool _hayDestino;

    /// <param name="clients">La fábrica de clientes; el de <see cref="ClientName"/> lo arma la pieza del árbol.</param>
    /// <param name="log">Dónde queda lo que no se pudo publicar.</param>
    /// <param name="hayDestino">Si el despliegue configuró dónde vive el orquestador.</param>
    public PublicadorDeOfertasDeReservas(IHttpClientFactory clients, ILogger<PublicadorDeOfertasDeReservas> log, bool hayDestino)
    {
        _clients = clients;
        _log = log;
        _hayDestino = hayDestino;
    }

    /// <summary>Publica la oferta de una ficha completa (<see cref="LecturaDeLaFicha.Completa"/>).</summary>
    public Task<ResultadoDeLaOferta> PublicarAsync(LecturaDeLaFicha ficha, string moneda, string zona, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ficha);
        if (!ficha.Completa) throw new ArgumentException("Una ficha incompleta no se publica: se dice en el backoffice.", nameof(ficha));

        return EnviarAsync(ficha.Servicio, "publicar", () => new HttpRequestMessage(HttpMethod.Post, "v1/ofertas")
        {
            Content = JsonContent.Create(Oferta(ficha, moneda, zona)),
        }, cancellationToken, leerFuera: true);
    }

    /// <summary>El campo de la respuesta de <c>PublishServiceOffer</c> con las reservas que quedaron fuera.</summary>
    public const string CampoReservasFuera = "reservasFuera";

    /// <summary>
    /// Retira la oferta del servicio: su precio deja de valer. Repetirlo no hace daño, y retirar la de un servicio
    /// que nunca se ofreció —una ficha de la tienda que no es reservable— es no tener nada que hacer.
    /// </summary>
    public Task<ResultadoDeLaOferta> RetirarAsync(string servicio, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(servicio);
        return EnviarAsync(servicio, "retirar", () => new HttpRequestMessage(
            HttpMethod.Post, $"v1/ofertas/{Uri.EscapeDataString(servicio)}/retirar"), cancellationToken, NoExiste);
    }

    /// <summary>El rechazo del orquestador a retirar la oferta de un servicio que no tiene.</summary>
    public const string NoExiste = "reservas.oferta_no_existe";

    private async Task<ResultadoDeLaOferta> EnviarAsync(
        string servicio, string que, Func<HttpRequestMessage> peticion, CancellationToken cancellationToken,
        string? nadaQueHacer = null, bool leerFuera = false)
    {
        if (!_hayDestino)
        {
            _log.LogDebug("La oferta del servicio {Servicio} no se {Que}: no hay orquestador de Reservas configurado.", servicio, que);
            return ResultadoDeLaOferta.SinDestino;
        }

        using var req = peticion();
        // Una llave por publicación: los reintentos de la cadena repiten ESTA petición, y otro publicar es otra.
        req.Headers.Add("Idempotency-Key", "oferta-" + Guid.NewGuid().ToString("n"));

        try
        {
            using var res = await _clients.CreateClient(ClientName).SendAsync(req, cancellationToken).ConfigureAwait(false);
            if (res.IsSuccessStatusCode)
            {
                var fuera = leerFuera ? await FueraAsync(res, cancellationToken).ConfigureAwait(false) : 0;
                _log.LogInformation("Oferta del servicio {Servicio}: {Que} en el orquestador, hecho ({Fuera} reservas fuera).", servicio, que, fuera);
                return fuera > 0 ? ResultadoDeLaOferta.Hecha with { ReservasFuera = fuera } : ResultadoDeLaOferta.Hecha;
            }

            var rechazo = await RechazoDelArbolDeServicios.LeerAsync(res, Json, cancellationToken).ConfigureAwait(false);
            if (nadaQueHacer is not null && string.Equals(rechazo?.Codigo, nadaQueHacer, StringComparison.Ordinal))
            {
                _log.LogDebug("La oferta del servicio {Servicio} no estaba publicada: nada que {Que}.", servicio, que);
                return ResultadoDeLaOferta.Hecha;
            }
            _log.LogWarning(
                "El orquestador no pudo {Que} la oferta del servicio {Servicio}: {Estado} {Codigo} {Detalle}.",
                que, servicio, (int)res.StatusCode, rechazo?.Codigo, rechazo?.Detalle);
            return new ResultadoDeLaOferta(EstadoDeLaOferta.Fallida, rechazo?.Codigo, rechazo?.Detalle);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            _log.LogWarning(ex, "El orquestador de Reservas no respondió al {Que} la oferta del servicio {Servicio}.", que, servicio);
            return new ResultadoDeLaOferta(EstadoDeLaOferta.Fallida);
        }
    }

    /// <summary>
    /// Cuántas reservas dejó fuera la publicación, de su respuesta. Una respuesta que no lo dice cuenta como ninguna:
    /// la oferta YA se publicó, y lo que falta es sólo el aviso.
    /// </summary>
    private static async Task<int> FueraAsync(HttpResponseMessage res, CancellationToken ct)
    {
        try
        {
            using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(ct).ConfigureAwait(false), cancellationToken: ct)
                .ConfigureAwait(false);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty(CampoReservasFuera, out var fuera) && fuera.TryGetInt32(out var n) && n > 0 ? n : 0;
        }
        catch (JsonException)
        {
            return 0;
        }
    }

    /// <summary>El cuerpo de <c>PublishServiceOffer</c>, con los nombres del contrato del orquestador.</summary>
    private static object Oferta(LecturaDeLaFicha f, string moneda, string zona) => new
    {
        servicio = f.Servicio,
        nombre = f.Nombre,
        duracionMinutos = f.DuracionMinutos,
        horario = f.Dias.Select(d => new
        {
            dia = d.ToString(),
            desde = f.Abre.ToString("HH:mm", CultureInfo.InvariantCulture),
            hasta = f.Cierra.ToString("HH:mm", CultureInfo.InvariantCulture),
        }).ToList(),
        cupos = f.Cupos,
        plazoCancelacionHoras = f.PlazoCancelacionHoras,
        precio = f.Precio,
        moneda,
        zona,
    };
}
