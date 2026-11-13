using System.Text.Json;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// <see cref="IConsultaDeSalud"/> contra el orquestador de Salud (<c>Synergos:Salud:BaseUrl</c>, 003-salud-cita I3). Lo
/// que no contesta o contesta otra forma es «sin respuesta»; un no del orquestador, con su código.
/// </summary>
public sealed class HttpConsultaDeSalud : IConsultaDeSalud
{
    public const string ClientName = "synergos-bff-salud-consulta";

    /// <summary>La clase con que el personal marca un paso en Workflow.</summary>
    public const string ClaseDelPersonal = "salud.personal";

    private readonly IHttpClientFactory _clients;
    private readonly ILogger<HttpConsultaDeSalud> _logger;

    public HttpConsultaDeSalud(IHttpClientFactory clients, ILogger<HttpConsultaDeSalud> logger)
    {
        _clients = clients;
        _logger = logger;
    }

    public async Task<IReadOnlyList<CitaDelDia>?> AgendaAsync(
        string profesional, DateTimeOffset desde, DateTimeOffset hasta, CancellationToken cancellationToken)
    {
        var url = $"v1/agenda?profesional={Uri.EscapeDataString(profesional)}"
            + $"&desde={Uri.EscapeDataString(desde.ToString("O"))}&hasta={Uri.EscapeDataString(hasta.ToString("O"))}";
        try
        {
            using var r = await _clients.CreateClient(ClientName).GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (!r.IsSuccessStatusCode)
            {
                _logger.LogWarning("La agenda del día no se pudo leer: el orquestador contestó {Estado}.", (int)r.StatusCode);
                return null;
            }
            var cuerpo = await r.Content.ReadFromJsonAsync<JsonElement>(cancellationToken).ConfigureAwait(false);
            return cuerpo.GetProperty("citas").EnumerateArray().Select(c => new CitaDelDia(
                c.GetProperty("reservaId").GetString()!, c.GetProperty("inicio").GetDateTimeOffset(),
                c.GetProperty("fin").GetDateTimeOffset(), c.GetProperty("pacienteKind").GetString()!,
                c.GetProperty("pacienteId").GetString()!, c.GetProperty("llegada").GetString()!)).ToList();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException
                                       or InvalidOperationException)
        {
            _logger.LogWarning(ex, "La agenda del día no se pudo leer.");
            return null;
        }
    }

    public Task<ResultadoDeLaConsulta> MarcarLlegadaAsync(string reservaId, string paso, Guid quien, CancellationToken cancellationToken)
        => EnviarAsync($"v1/agenda/{Uri.EscapeDataString(reservaId)}/llegada",
            new { paso, actorKind = ClaseDelPersonal, actorId = quien.ToString("N") },
            c => (c.GetProperty("llegada").GetString(), null), cancellationToken);

    public Task<ResultadoDeLaConsulta> SellarAsync(string llave, string huella, CancellationToken cancellationToken)
        => EnviarAsync("v1/sellos", new { llave, contenido = huella },
            c => (c.GetProperty("sello").GetString(), c.GetProperty("llaveId").GetString()), cancellationToken);

    public Task<ResultadoDeLaConsulta> ComprobarSelloAsync(string llave, string huella, string sello, CancellationToken cancellationToken)
        => EnviarAsync("v1/sellos/comprobar", new { llave, contenido = huella, sello },
            c => (c.GetProperty("llaveId").GetString(), null), cancellationToken);

    private async Task<ResultadoDeLaConsulta> EnviarAsync(
        string ruta, object cuerpo, Func<JsonElement, (string? Valor, string? Extra)> leer, CancellationToken cancellationToken)
    {
        try
        {
            using var r = await _clients.CreateClient(ClientName).PostAsJsonAsync(ruta, cuerpo, cancellationToken).ConfigureAwait(false);
            var texto = await r.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var json = string.IsNullOrWhiteSpace(texto) ? default : JsonDocument.Parse(texto).RootElement;
            if (r.IsSuccessStatusCode)
            {
                var (valor, extra) = leer(json);
                return new ResultadoDeLaConsulta(EstadoDeLaConsulta.Hecho, valor, extra);
            }
            if ((int)r.StatusCode is >= 400 and < 500 && json.ValueKind == JsonValueKind.Object
                && json.TryGetProperty("code", out var codigo) && codigo.GetString() is { Length: > 0 } c)
            {
                return new ResultadoDeLaConsulta(EstadoDeLaConsulta.Rechazado, Codigo: c);
            }
            _logger.LogWarning("El orquestador de Salud contestó {Estado} en {Ruta}.", (int)r.StatusCode, ruta);
            return ResultadoDeLaConsulta.SinRespuesta;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException
                                       or InvalidOperationException)
        {
            _logger.LogWarning(ex, "El orquestador de Salud no contestó en {Ruta}.", ruta);
            return ResultadoDeLaConsulta.SinRespuesta;
        }
    }
}

/// <summary>Sin orquestador declarado: la consulta no contesta, y el CMS lo dice (503).</summary>
public sealed class ConsultaDeSaludSinDestino : IConsultaDeSalud
{
    public Task<IReadOnlyList<CitaDelDia>?> AgendaAsync(string profesional, DateTimeOffset desde, DateTimeOffset hasta, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<CitaDelDia>?>(null);

    public Task<ResultadoDeLaConsulta> MarcarLlegadaAsync(string reservaId, string paso, Guid quien, CancellationToken cancellationToken)
        => Task.FromResult(ResultadoDeLaConsulta.SinRespuesta);

    public Task<ResultadoDeLaConsulta> SellarAsync(string llave, string huella, CancellationToken cancellationToken)
        => Task.FromResult(ResultadoDeLaConsulta.SinRespuesta);

    public Task<ResultadoDeLaConsulta> ComprobarSelloAsync(string llave, string huella, string sello, CancellationToken cancellationToken)
        => Task.FromResult(ResultadoDeLaConsulta.SinRespuesta);
}
