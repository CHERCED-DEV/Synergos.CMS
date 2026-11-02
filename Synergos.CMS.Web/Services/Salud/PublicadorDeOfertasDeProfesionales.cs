using System.Globalization;
using System.Text.Json;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services.Salud;

/// <summary>Cómo terminó publicar o retirar la oferta de un profesional.</summary>
public enum EstadoDeLaOfertaDeProfesional
{
    /// <summary>El orquestador la tomó (o no había nada que retirar).</summary>
    Hecha,

    /// <summary>El despliegue no dice dónde vive <c>Bff.Salud</c>: no se intentó.</summary>
    SinDestino,

    /// <summary>El orquestador la rechazó o no respondió; el motivo queda en el log.</summary>
    Fallida,
}

/// <summary>Publica y retira la oferta de un profesional en <c>Synergos.Bff.Salud</c>.</summary>
public interface IPublicadorDeOfertasDeProfesionales
{
    /// <summary>Publica la oferta del profesional tal como lo sirve el directorio: su agenda, en la zona del sitio.</summary>
    Task<EstadoDeLaOfertaDeProfesional> PublicarAsync(MedicalDoctor profesional, CancellationToken cancellationToken = default);

    /// <summary>Retira la oferta del profesional. Retirar la que no está no hace nada.</summary>
    Task<EstadoDeLaOfertaDeProfesional> RetirarAsync(string profesional, CancellationToken cancellationToken = default);
}

/// <summary>
/// Publica y retira la oferta de un profesional en <c>Synergos.Bff.Salud</c>: la oferta común del núcleo
/// (<c>POST v1/ofertas</c> y <c>POST v1/ofertas/{pieza}/retirar</c>) con la agenda de la pieza (NN-22 de 003, T097 del
/// núcleo). El molde de <c>PublicadorDeOfertasDeCursos</c> (003-salud-cita, T047).
/// </summary>
/// <remarks>
/// <para><b>Manda lo que el directorio sirve y nada más</b>: la pieza es el slug del profesional (el id con que la cita
/// lo halla en la agenda), y su agenda es la que declara la ficha: los días, de la hora de inicio a la de cierre, y la
/// duración de cada cita, en la zona del sitio (<c>Synergos:Listados:ZonaHoraria</c>, la misma con que el asistente
/// dice la hora). Un cupo por franja: un profesional atiende a una persona a la vez.</para>
///
/// <para><b>Precio cero, a propósito</b>: lo que se cobra en la cita es el SERVICIO (<c>salud.servicio:consulta</c>,
/// el copago), no el profesional. La oferta común exige un precio; el del profesional no lo cotiza nadie.</para>
///
/// <para><b>Sin destino no intenta</b>: un clon limpio no tiene orquestador. Con destino y el orquestador caído o en
/// desacuerdo, devuelve <see cref="EstadoDeLaOfertaDeProfesional.Fallida"/> y nunca lanza, porque quien llama es el
/// publicar del editor.</para>
/// </remarks>
public sealed class PublicadorDeOfertasDeProfesionales : IPublicadorDeOfertasDeProfesionales
{
    /// <summary>Cliente nombrado que registra el composer, hacia <c>Synergos:Salud:BaseUrl</c>.</summary>
    public const string ClientName = "synergos-bff-salud-oferta";

    /// <summary>El rechazo del orquestador a retirar la oferta de un profesional que no tiene.</summary>
    public const string NoExiste = "salud.oferta_no_existe";

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly IHttpClientFactory _clients;
    private readonly ILogger<PublicadorDeOfertasDeProfesionales> _log;
    private readonly bool _hayDestino;
    private readonly string _moneda;
    private readonly string _zona;

    /// <param name="clients">La fábrica de clientes; el de <see cref="ClientName"/> lo arma la pieza del árbol.</param>
    /// <param name="log">Dónde queda lo que no se pudo publicar.</param>
    /// <param name="hayDestino">Si el despliegue configuró dónde vive el orquestador.</param>
    /// <param name="moneda">La moneda del sitio (<c>Synergos:Cart:Currency</c>).</param>
    /// <param name="zona">La zona del sitio (<c>Synergos:Listados:ZonaHoraria</c>), en la que se lee el horario.</param>
    public PublicadorDeOfertasDeProfesionales(
        IHttpClientFactory clients, ILogger<PublicadorDeOfertasDeProfesionales> log, bool hayDestino, string moneda, string zona)
    {
        _clients = clients;
        _log = log;
        _hayDestino = hayDestino;
        _moneda = moneda;
        _zona = zona;
    }

    /// <inheritdoc />
    public Task<EstadoDeLaOfertaDeProfesional> PublicarAsync(MedicalDoctor profesional, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profesional);
        return EnviarAsync(profesional.Id, "publicar", () => new HttpRequestMessage(HttpMethod.Post, "v1/ofertas")
        {
            Content = JsonContent.Create(Oferta(profesional, _moneda, _zona)),
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<EstadoDeLaOfertaDeProfesional> RetirarAsync(string profesional, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profesional);
        return EnviarAsync(profesional, "retirar", () => new HttpRequestMessage(
            HttpMethod.Post, $"v1/ofertas/{Uri.EscapeDataString(profesional)}/retirar"), cancellationToken, NoExiste);
    }

    /// <summary>
    /// El cuerpo de <c>PublishDeclaredOffer</c>: el profesional es la pieza, sin variantes, y su agenda es la de la
    /// ficha. Cada día atendido es un bloque de la hora de inicio a la de cierre.
    /// </summary>
    internal static object Oferta(MedicalDoctor p, string moneda, string zona) => new
    {
        pieza = p.Id,
        precio = 0m,
        moneda,
        agenda = new
        {
            cupos = 1,
            franjaMinutos = p.SlotMinutes,
            horario = p.WorkingDays
                .Select(d => new { dia = d.ToString(), desde = Hora(p.SlotStartHour), hasta = Hora(p.SlotEndHour) })
                .ToList(),
            zona,
            avisoMinutos = 0,
        },
    };

    private static string Hora(int hora) => hora.ToString("00", CultureInfo.InvariantCulture) + ":00";

    private async Task<EstadoDeLaOfertaDeProfesional> EnviarAsync(
        string profesional, string que, Func<HttpRequestMessage> peticion, CancellationToken cancellationToken, string? nadaQueHacer = null)
    {
        if (!_hayDestino)
        {
            _log.LogDebug("La oferta del profesional {Profesional} no se {Que}: no hay orquestador de Salud configurado.", profesional, que);
            return EstadoDeLaOfertaDeProfesional.SinDestino;
        }

        using var req = peticion();
        // Una llave por publicación: los reintentos de la cadena repiten ESTA petición, y otro publicar es otra.
        req.Headers.Add("Idempotency-Key", "oferta-" + Guid.NewGuid().ToString("n"));

        try
        {
            using var res = await _clients.CreateClient(ClientName).SendAsync(req, cancellationToken).ConfigureAwait(false);
            if (res.IsSuccessStatusCode)
            {
                _log.LogInformation("Oferta del profesional {Profesional}: {Que} en el orquestador, hecho.", profesional, que);
                return EstadoDeLaOfertaDeProfesional.Hecha;
            }

            var rechazo = await RechazoDelArbolDeServicios.LeerAsync(res, Json, cancellationToken).ConfigureAwait(false);
            if (nadaQueHacer is not null && string.Equals(rechazo?.Codigo, nadaQueHacer, StringComparison.Ordinal))
            {
                _log.LogDebug("La oferta del profesional {Profesional} no estaba publicada: nada que {Que}.", profesional, que);
                return EstadoDeLaOfertaDeProfesional.Hecha;
            }
            _log.LogWarning(
                "El orquestador no pudo {Que} la oferta del profesional {Profesional}: {Estado} {Codigo} {Detalle}.",
                que, profesional, (int)res.StatusCode, rechazo?.Codigo, rechazo?.Detalle);
            return EstadoDeLaOfertaDeProfesional.Fallida;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            _log.LogWarning(ex, "El orquestador de Salud no respondió al {Que} la oferta del profesional {Profesional}.", que, profesional);
            return EstadoDeLaOfertaDeProfesional.Fallida;
        }
    }
}
