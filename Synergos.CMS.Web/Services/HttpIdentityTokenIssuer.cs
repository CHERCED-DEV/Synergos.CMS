using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Consigue tokens de identidad de verdad, contra <c>Synergos.Api.Identity</c> (HU #14).
/// </summary>
/// <remarks>
/// <para><b>Da de alta al sujeto si hace falta, y sólo la primera vez.</b> Un token se emite
/// para un principal que exista; el CMS conoce a quien actúa y la capacidad no, así que alguien
/// tiene que presentarlos. Se hace con la llave de idempotencia derivada del propio sujeto: dos
/// peticiones simultáneas del mismo funcionario no crean dos principales.</para>
///
/// <para><b>Y NO reescribe los roles de un principal que ya existe</b>, aunque la configuración
/// del CMS diga otra cosa. Es deliberado: si un despliegue pudiera re-otorgar roles como efecto
/// secundario de arrancar, un valor mal escrito en un fichero de configuración ascendería a
/// alguien sin que nadie lo decidiera — y los roles son justo lo que esta HU existe para dejar de
/// creerle al llamador. Cambiarlos es un acto sobre <c>Api.Identity</c>
/// (<c>/v1/principals/{id}/roles/grant</c>), no un efecto de desplegar.</para>
///
/// <para><b>Guarda el token hasta poco antes de que venza.</b> Sin caché, cada decisión de
/// ventanilla costaría dos llamadas a una capacidad que corre sobre fichero JSON con
/// <c>lock</c> de proceso. El margen de renovación está para que ninguna petición salga con un
/// token que vence en el camino.</para>
///
/// <para><b>Nunca lanza</b> (ver <see cref="IIdentityTokenIssuer"/>). Todo lo que sale mal
/// termina en <c>null</c> y en el log: quien llama sigue declarando quién actúa, y la capacidad
/// decide si eso le alcanza. Un trámite no se cae porque la identidad esté caída.</para>
/// </remarks>
public sealed class HttpIdentityTokenIssuer : IIdentityTokenIssuer
{
    /// <summary>Cliente nombrado que registra el composer.</summary>
    public const string ClientName = "synergos-api-identity";

    /// <summary>Cabecera de la llave compartida. La misma que exige toda capacidad.</summary>
    public const string ApiKeyHeader = "X-Synergos-Key";

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly IHttpClientFactory _clients;
    private readonly IOptionsMonitor<IdentitySettings> _settings;
    private readonly ILogger<HttpIdentityTokenIssuer> _log;
    private readonly Func<DateTimeOffset> _now;

    private readonly ConcurrentDictionary<string, Vigente> _cache = new(StringComparer.Ordinal);

    public HttpIdentityTokenIssuer(
        IHttpClientFactory clients,
        IOptionsMonitor<IdentitySettings> settings,
        ILogger<HttpIdentityTokenIssuer> log,
        Func<DateTimeOffset>? now = null)
    {
        _clients = clients;
        _settings = settings;
        _log = log;
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task<string?> IssueAsync(
        IdentitySubject subject, CancellationToken cancellationToken = default)
    {
        if (subject is null || string.IsNullOrWhiteSpace(subject.Kind) || string.IsNullOrWhiteSpace(subject.Id))
        {
            return null;
        }

        var clave = $"{subject.Kind}|{subject.Id}";
        var margen = TimeSpan.FromSeconds(Math.Max(0, _settings.CurrentValue.RenewSkewSeconds));

        if (_cache.TryGetValue(clave, out var guardado) && guardado.SirveHasta(_now() + margen))
        {
            return guardado.Token;
        }

        try
        {
            await AsegurarPrincipalAsync(subject, cancellationToken).ConfigureAwait(false);

            var emitido = await EmitirAsync(subject, cancellationToken).ConfigureAwait(false);
            if (emitido is null) return null;

            _cache[clave] = emitido;
            return emitido.Token;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;   // quien pidió se fue; no es un fallo de la identidad
        }
        catch (Exception ex)
        {
            // Se traga TODO a propósito, y es la única vez que eso está bien en este repo: la
            // alternativa es que una identidad caída pare las decisiones de ventanilla. Sin
            // token se sigue declarando, que es exactamente lo que se hacía antes de esta HU.
            _log.LogWarning(ex, "No se pudo conseguir identidad verificada para {Kind}:{Id}; se sigue declarando.",
                subject.Kind, subject.Id);
            return null;
        }
    }

    // ── El cable ────────────────────────────────────────────────────────────

    private async Task AsegurarPrincipalAsync(IdentitySubject subject, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "v1/principals")
        {
            Content = JsonContent.Create(new
            {
                subjectKind = subject.Kind,
                subjectId = subject.Id,
                // Sin credencial: quien actúa ya entró por la sesión del CMS, y fabricarle una
                // contraseña que nadie va a usar sería inventar un secreto que hay que custodiar.
                secret = (string?)null,
                roles = subject.Roles ?? Array.Empty<string>(),
            }),
        };
        req.Headers.Add("Idempotency-Key", LlaveDe(subject));

        using var res = await Cliente().SendAsync(req, ct).ConfigureAwait(false);

        // 409 es el caso NORMAL a partir de la segunda vez: el sujeto ya está registrado. No es
        // un fallo y no se reintenta — se sigue a pedir el token.
        if (res.IsSuccessStatusCode || res.StatusCode == HttpStatusCode.Conflict) return;

        // Cualquier otra cosa se registra y se sigue: pedir el token dirá si de verdad no existe.
        _log.LogWarning("Api.Identity respondió {Status} al dar de alta {Kind}:{Id}.",
            (int)res.StatusCode, subject.Kind, subject.Id);
    }

    private async Task<Vigente?> EmitirAsync(IdentitySubject subject, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "v1/tokens")
        {
            Content = JsonContent.Create(new { subjectKind = subject.Kind, subjectId = subject.Id }),
        };

        using var res = await Cliente().SendAsync(req, ct).ConfigureAwait(false);
        if (!res.IsSuccessStatusCode)
        {
            _log.LogWarning("Api.Identity respondió {Status} al emitir token para {Kind}:{Id}; se sigue declarando.",
                (int)res.StatusCode, subject.Kind, subject.Id);
            return null;
        }

        var cuerpo = await res.Content.ReadFromJsonAsync<TokenDto>(Json, ct).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(cuerpo?.Token) ? null : new Vigente(cuerpo!.Token, cuerpo.ExpiresAtUtc);
    }

    private HttpClient Cliente() => _clients.CreateClient(ClientName);

    /// <summary>
    /// La llave de alta: determinista sobre el sujeto.
    /// </summary>
    /// <remarks>
    /// Que dos altas del mismo sujeto compartan llave es el punto: son la misma operación
    /// lógica, y con una llave nueva cada vez dos peticiones simultáneas del mismo funcionario
    /// crearían dos principales para la misma persona.
    /// </remarks>
    internal static string LlaveDe(IdentitySubject subject)
    {
        var semilla = $"{subject.Kind}|{subject.Id}";
        return "cms-principal-"
            + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(semilla)))[..32].ToLowerInvariant();
    }

    /// <summary>Un token con su vencimiento, tal como lo devolvió la capacidad.</summary>
    private sealed record Vigente(string Token, DateTimeOffset ExpiraUtc)
    {
        public bool SirveHasta(DateTimeOffset cuando) => cuando < ExpiraUtc;
    }

    private sealed record TokenDto(string? Token, DateTimeOffset ExpiresAtUtc, DateTimeOffset SessionEndsAtUtc);
}
