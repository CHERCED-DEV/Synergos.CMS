using System.Text.Json;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services.Gobierno;

/// <summary>Cómo terminó publicar o retirar la oferta de un trámite.</summary>
public enum EstadoDeLaOfertaDeTramite
{
    /// <summary>El orquestador la tomó (o no había nada que retirar).</summary>
    Hecha,

    /// <summary>El despliegue no dice dónde vive <c>Bff.Gobierno</c>: no se intentó.</summary>
    SinDestino,

    /// <summary>El trámite no tiene clave del nodo (la siembra): no hay con qué ofrecerlo.</summary>
    SinClave,

    /// <summary>El orquestador la rechazó o no respondió; el motivo queda en el log.</summary>
    Fallida,
}

/// <summary>Publica y retira la oferta de un trámite en <c>Synergos.Bff.Gobierno</c>.</summary>
public interface IPublicadorDeOfertasDeGobierno
{
    /// <summary>Publica la oferta del trámite tal como lo sirve el catálogo: su clave del nodo y su tasa.</summary>
    Task<EstadoDeLaOfertaDeTramite> PublicarAsync(TramiteDetail tramite, CancellationToken cancellationToken = default);

    /// <summary>Retira la oferta del trámite: su tasa deja de valer. Retirar la que no está no hace nada.</summary>
    Task<EstadoDeLaOfertaDeTramite> RetirarAsync(string clave, CancellationToken cancellationToken = default);
}

/// <summary>
/// Publica y retira la oferta de un trámite en <c>Synergos.Bff.Gobierno</c> (<c>POST v1/ofertas</c> y
/// <c>POST v1/ofertas/{tramite}/retirar</c>, 009-gobierno-tramite T034). El molde de
/// <c>PublicadorDeOfertasDeCursos</c>.
/// </summary>
/// <remarks>
/// <para><b>Manda lo que el catálogo sirve y nada más</b>: la clave del nodo —la identidad estable del trámite, que el
/// editor no cambia como cambia el slug o el nombre— y la tasa que dejan las reglas del catálogo
/// (<see cref="Catalog.TramiteContentRules.TryResolveFee"/>): marcado gratuito y en cero, cero; con tasa, la tasa —también
/// si además está marcado gratuito, que el catálogo registra como error—; sin marcar y en cero, el catálogo no lo sirve y
/// no hay oferta. Un cero se publica explícito: es un trámite gratuito, no uno sin oferta.</para>
///
/// <para><b>Sin destino no intenta</b>: un clon limpio no tiene orquestador. Con destino y el orquestador caído o en
/// desacuerdo, devuelve <see cref="EstadoDeLaOfertaDeTramite.Fallida"/> y nunca lanza, porque quien llama es el publicar
/// del editor.</para>
/// </remarks>
public sealed class PublicadorDeOfertasDeGobierno : IPublicadorDeOfertasDeGobierno
{
    /// <summary>Cliente nombrado que registra el composer, hacia <c>Synergos:Gobierno:BaseUrl</c>.</summary>
    public const string ClientName = "synergos-bff-gobierno-oferta";

    /// <summary>El rechazo del orquestador a retirar la oferta de un trámite que no tiene.</summary>
    public const string NoExiste = "gobierno.oferta_no_existe";

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly IHttpClientFactory _clients;
    private readonly ILogger<PublicadorDeOfertasDeGobierno> _log;
    private readonly bool _hayDestino;

    /// <param name="hayDestino">Si el despliegue configuró dónde vive el orquestador.</param>
    public PublicadorDeOfertasDeGobierno(IHttpClientFactory clients, ILogger<PublicadorDeOfertasDeGobierno> log, bool hayDestino)
    {
        _clients = clients;
        _log = log;
        _hayDestino = hayDestino;
    }

    /// <inheritdoc />
    public Task<EstadoDeLaOfertaDeTramite> PublicarAsync(TramiteDetail tramite, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tramite);
        var s = tramite.Summary;
        if (string.IsNullOrWhiteSpace(s.Clave))
        {
            _log.LogWarning("El trámite {Tramite} no tiene clave del nodo: no se publica su oferta.", s.Id);
            return Task.FromResult(EstadoDeLaOfertaDeTramite.SinClave);
        }
        return EnviarAsync(s.Clave, "publicar", () => new HttpRequestMessage(HttpMethod.Post, "v1/ofertas")
        {
            Content = JsonContent.Create(new { tramite = s.Clave, tasa = Math.Max(0m, s.FeeMinor), moneda = s.Currency }),
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<EstadoDeLaOfertaDeTramite> RetirarAsync(string clave, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clave);
        return EnviarAsync(clave, "retirar", () => new HttpRequestMessage(
            HttpMethod.Post, $"v1/ofertas/{Uri.EscapeDataString(clave)}/retirar"), cancellationToken, NoExiste);
    }

    private async Task<EstadoDeLaOfertaDeTramite> EnviarAsync(
        string clave, string que, Func<HttpRequestMessage> peticion, CancellationToken cancellationToken, string? nadaQueHacer = null)
    {
        if (!_hayDestino)
        {
            _log.LogDebug("La oferta del trámite {Tramite} no se {Que}: no hay orquestador de Gobierno configurado.", clave, que);
            return EstadoDeLaOfertaDeTramite.SinDestino;
        }

        using var req = peticion();
        // Una llave por publicación: los reintentos de la cadena repiten ESTA petición, y otro publicar es otra.
        req.Headers.Add("Idempotency-Key", "oferta-" + Guid.NewGuid().ToString("n"));

        try
        {
            using var res = await _clients.CreateClient(ClientName).SendAsync(req, cancellationToken).ConfigureAwait(false);
            if (res.IsSuccessStatusCode)
            {
                _log.LogInformation("Oferta del trámite {Tramite}: {Que} en el orquestador, hecho.", clave, que);
                return EstadoDeLaOfertaDeTramite.Hecha;
            }

            var rechazo = await RechazoDelArbolDeServicios.LeerAsync(res, Json, cancellationToken).ConfigureAwait(false);
            if (nadaQueHacer is not null && string.Equals(rechazo?.Codigo, nadaQueHacer, StringComparison.Ordinal))
            {
                _log.LogDebug("La oferta del trámite {Tramite} no estaba publicada: nada que {Que}.", clave, que);
                return EstadoDeLaOfertaDeTramite.Hecha;
            }
            _log.LogWarning(
                "El orquestador no pudo {Que} la oferta del trámite {Tramite}: {Estado} {Codigo} {Detalle}.",
                que, clave, (int)res.StatusCode, rechazo?.Codigo, rechazo?.Detalle);
            return EstadoDeLaOfertaDeTramite.Fallida;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            _log.LogWarning(ex, "El orquestador de Gobierno no respondió al {Que} la oferta del trámite {Tramite}.", que, clave);
            return EstadoDeLaOfertaDeTramite.Fallida;
        }
    }
}
