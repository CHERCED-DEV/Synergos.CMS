using System.Text.Json;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// <see cref="IConsentReader"/> sobre <c>Api.Consent</c> (<c>POST /v1/grants/check</c>), el mismo que usa la puerta en
/// <c>consent.verificar</c> (003-salud-cita, I2 T054). El sujeto y el propósito van en el cuerpo, nunca en la URL: juntos
/// son un dato personal.
/// </summary>
/// <remarks>
/// <para>Un 2xx con id es <see cref="EstadoDelPermiso.Vigente"/>. Un rechazo de la capacidad con un código
/// <c>consent.*</c> (no se dio, se retiró, venció) es <see cref="EstadoDelPermiso.NoVigente"/>. Todo lo demás —la red,
/// un 5xx, un 401 de llave, un cuerpo ilegible— es <see cref="EstadoDelPermiso.NoSePudoLeer"/>, y la guardia niega.</para>
/// </remarks>
public sealed class HttpConsentReader : IConsentReader
{
    /// <summary>Cliente nombrado que registra el composer.</summary>
    public const string ClientName = "synergos-api-consent";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _clients;
    private readonly ILogger<HttpConsentReader> _log;

    public HttpConsentReader(IHttpClientFactory clients, ILogger<HttpConsentReader> log)
    {
        _clients = clients;
        _log = log;
    }

    public async Task<PermisoLeido> LeerAsync(string sujetoKind, string sujetoId, string proposito, CancellationToken cancellationToken)
    {
        try
        {
            using var respuesta = await _clients.CreateClient(ClientName).PostAsJsonAsync(
                "v1/grants/check",
                new { subjectKind = sujetoKind, subjectId = sujetoId, purpose = proposito },
                cancellationToken).ConfigureAwait(false);

            using var cuerpo = await LeerJsonAsync(respuesta, cancellationToken).ConfigureAwait(false);
            if (respuesta.IsSuccessStatusCode)
            {
                return cuerpo?.RootElement.TryGetProperty("id", out var id) == true && id.GetString() is { Length: > 0 } permiso
                    ? new PermisoLeido(EstadoDelPermiso.Vigente, permiso)
                    : NoSePudo("un 2xx sin id del permiso", proposito);
            }

            var codigo = cuerpo?.RootElement.TryGetProperty("code", out var c) == true ? c.GetString() : null;
            return (int)respuesta.StatusCode is >= 400 and < 500 && codigo?.StartsWith("consent.", StringComparison.Ordinal) == true
                ? PermisoLeido.NoVigente
                : NoSePudo($"{(int)respuesta.StatusCode} {codigo ?? "sin código"}", proposito);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return NoSePudo(ex.GetType().Name, proposito);
        }
    }

    private PermisoLeido NoSePudo(string causa, string proposito)
    {
        _log.LogWarning("Api.Consent no contestó el permiso «{Proposito}» ({Causa}): el acceso se niega.", proposito, causa);
        return PermisoLeido.NoSePudo;
    }

    private static async Task<JsonDocument?> LeerJsonAsync(HttpResponseMessage respuesta, CancellationToken ct)
    {
        var texto = await respuesta.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(texto)) return null;
        try { return JsonDocument.Parse(texto); }
        catch (JsonException) { return null; }
    }
}

/// <summary>
/// El lector cuando <c>Synergos:Consent:Mode = Api</c> y no hay <c>BaseUrl</c>: no adivina un puerto y niega todo.
/// </summary>
public sealed class LectorDePermisosSinDestino : IConsentReader
{
    public Task<PermisoLeido> LeerAsync(string sujetoKind, string sujetoId, string proposito, CancellationToken cancellationToken) =>
        Task.FromResult(PermisoLeido.NoSePudo);
}
