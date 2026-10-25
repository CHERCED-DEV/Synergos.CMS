using System.Text.Json;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services.Academy;

/// <summary>Cómo terminó publicar o retirar la oferta de un curso.</summary>
public enum EstadoDeLaOfertaDeCurso
{
    /// <summary>El orquestador la tomó (o no había nada que retirar).</summary>
    Hecha,

    /// <summary>El despliegue no dice dónde vive <c>Bff.Educacion</c>: no se intentó.</summary>
    SinDestino,

    /// <summary>El orquestador la rechazó o no respondió; el motivo queda en el log.</summary>
    Fallida,
}

/// <summary>Publica y retira la oferta de un curso en <c>Synergos.Bff.Educacion</c>.</summary>
public interface IPublicadorDeOfertasDeCursos
{
    /// <summary>Publica la oferta del curso tal como lo sirve el catálogo: su precio y el de cada plan ofrecido.</summary>
    Task<EstadoDeLaOfertaDeCurso> PublicarAsync(CourseDetail curso, CancellationToken cancellationToken = default);

    /// <summary>Retira la oferta del curso: sus precios dejan de valer. Retirar la que no está no hace nada.</summary>
    Task<EstadoDeLaOfertaDeCurso> RetirarAsync(string curso, CancellationToken cancellationToken = default);
}

/// <summary>
/// Publica y retira la oferta de un curso en <c>Synergos.Bff.Educacion</c> (<c>POST v1/ofertas</c> y
/// <c>POST v1/ofertas/{curso}/retirar</c>, 010-educacion-matricula). El molde de
/// <c>PublicadorDeOfertasDeReservas</c>.
/// </summary>
/// <remarks>
/// <para><b>Manda lo que el catálogo sirve y nada más</b>: el id del curso (el <c>courseSlug</c> con el catálogo en el
/// contenido), su precio sin plan y el total de cada plan, con su moneda; y el título del curso y el nombre de cada plan,
/// que dice el aviso de la matrícula (NM-2). Los precios los registra el orquestador; el
/// CMS no habla con la capacidad de precios. Un total cero se publica explícito: es un curso gratis, no uno sin
/// oferta.</para>
///
/// <para><b>Sólo los planes que la ficha ofrece</b> (<see cref="PlanesOfrecidos"/>): el plan en cuotas queda declarado
/// en el catálogo y sin ofrecer (D-010-P1), y por la puerta no se puede abrir con él.</para>
///
/// <para><b>Sin destino no intenta</b>: un clon limpio no tiene orquestador. Con destino y el orquestador caído o en
/// desacuerdo, devuelve <see cref="EstadoDeLaOfertaDeCurso.Fallida"/> y nunca lanza, porque quien llama es el publicar
/// del editor o el republicar de una persona.</para>
/// </remarks>
public sealed class PublicadorDeOfertasDeCursos : IPublicadorDeOfertasDeCursos
{
    /// <summary>Cliente nombrado que registra el composer, hacia <c>Synergos:Educacion:BaseUrl</c>.</summary>
    public const string ClientName = "synergos-bff-educacion-oferta";

    /// <summary>El rechazo del orquestador a retirar la oferta de un curso que no tiene.</summary>
    public const string NoExiste = "educacion.oferta_no_existe";

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly IHttpClientFactory _clients;
    private readonly ILogger<PublicadorDeOfertasDeCursos> _log;
    private readonly bool _hayDestino;

    /// <param name="clients">La fábrica de clientes; el de <see cref="ClientName"/> lo arma la pieza del árbol.</param>
    /// <param name="log">Dónde queda lo que no se pudo publicar.</param>
    /// <param name="hayDestino">Si el despliegue configuró dónde vive el orquestador.</param>
    public PublicadorDeOfertasDeCursos(IHttpClientFactory clients, ILogger<PublicadorDeOfertasDeCursos> log, bool hayDestino)
    {
        _clients = clients;
        _log = log;
        _hayDestino = hayDestino;
    }

    /// <summary>
    /// Los planes que la ficha ofrece: los de un solo pago. El financiado (<c>Installments &gt; 1</c>) cobraría su
    /// recargo de una vez y la capacidad de pagos no sabe de cuotas, así que no se ofrece por ningún camino (D-010-P1).
    /// </summary>
    public static IEnumerable<CoursePricingPlan> PlanesOfrecidos(CourseDetail curso)
    {
        ArgumentNullException.ThrowIfNull(curso);
        return curso.Plans.Where(p => p.Installments <= 1);
    }

    /// <inheritdoc />
    public Task<EstadoDeLaOfertaDeCurso> PublicarAsync(CourseDetail curso, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(curso);
        return EnviarAsync(curso.Course.Id, "publicar", () => new HttpRequestMessage(HttpMethod.Post, "v1/ofertas")
        {
            Content = JsonContent.Create(Oferta(curso)),
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<EstadoDeLaOfertaDeCurso> RetirarAsync(string curso, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(curso);
        return EnviarAsync(curso, "retirar", () => new HttpRequestMessage(
            HttpMethod.Post, $"v1/ofertas/{Uri.EscapeDataString(curso)}/retirar"), cancellationToken, NoExiste);
    }

    private async Task<EstadoDeLaOfertaDeCurso> EnviarAsync(
        string curso, string que, Func<HttpRequestMessage> peticion, CancellationToken cancellationToken, string? nadaQueHacer = null)
    {
        if (!_hayDestino)
        {
            _log.LogDebug("La oferta del curso {Curso} no se {Que}: no hay orquestador de Educación configurado.", curso, que);
            return EstadoDeLaOfertaDeCurso.SinDestino;
        }

        using var req = peticion();
        // Una llave por publicación: los reintentos de la cadena repiten ESTA petición, y otro publicar es otra.
        req.Headers.Add("Idempotency-Key", "oferta-" + Guid.NewGuid().ToString("n"));

        try
        {
            using var res = await _clients.CreateClient(ClientName).SendAsync(req, cancellationToken).ConfigureAwait(false);
            if (res.IsSuccessStatusCode)
            {
                _log.LogInformation("Oferta del curso {Curso}: {Que} en el orquestador, hecho.", curso, que);
                return EstadoDeLaOfertaDeCurso.Hecha;
            }

            var rechazo = await RechazoDelArbolDeServicios.LeerAsync(res, Json, cancellationToken).ConfigureAwait(false);
            if (nadaQueHacer is not null && string.Equals(rechazo?.Codigo, nadaQueHacer, StringComparison.Ordinal))
            {
                _log.LogDebug("La oferta del curso {Curso} no estaba publicada: nada que {Que}.", curso, que);
                return EstadoDeLaOfertaDeCurso.Hecha;
            }
            _log.LogWarning(
                "El orquestador no pudo {Que} la oferta del curso {Curso}: {Estado} {Codigo} {Detalle}.",
                que, curso, (int)res.StatusCode, rechazo?.Codigo, rechazo?.Detalle);
            return EstadoDeLaOfertaDeCurso.Fallida;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            _log.LogWarning(ex, "El orquestador de Educación no respondió al {Que} la oferta del curso {Curso}.", que, curso);
            return EstadoDeLaOfertaDeCurso.Fallida;
        }
    }

    /// <summary>El cuerpo de <c>PublishCourseOffer</c>, con los nombres del contrato del orquestador.</summary>
    private static object Oferta(CourseDetail c) => new
    {
        curso = c.Course.Id,
        precio = c.Course.Price,
        moneda = c.Course.Currency,
        // Cómo se llaman, como los pinta la ficha: el aviso de la matrícula los dice (NM-2 con T091 del núcleo).
        nombre = c.Course.Title,
        planes = PlanesOfrecidos(c).Select(p => new { codigo = p.Code, total = p.Total, nombre = p.Label }).ToList(),
    };
}
