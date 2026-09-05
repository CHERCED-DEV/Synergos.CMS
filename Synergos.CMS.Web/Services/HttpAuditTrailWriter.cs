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
/// La bitácora contra <c>Synergos.Api.Audit</c> (HU #15), envolviendo a la local.
/// </summary>
/// <remarks>
/// <para><b>Escribe LOCAL primero y reenvía después</b>, y ese orden es la decisión de fondo. Las
/// lecturas de este seam son SÍNCRONAS —<see cref="IAuditTrailWriter.GetRecent"/> y compañía— y la
/// bitácora del backoffice se pinta en cada carga, así que el JSONL sigue siendo el modelo de
/// lectura con la capacidad encendida. Es la forma del seguimiento de pedidos (#46): con
/// <c>Api.Audit</c> caída el administrador SIGUE viendo qué pasó; lo que se para es que el asiento
/// salga de esta máquina. Al revés, un fallo de red dejaría al backoffice sin un asiento que sí se
/// podía guardar.</para>
///
/// <para><b>Si el reenvío falla, queda escrito que falló.</b> Es el §5 de la HU con todas las
/// letras: «lo que pase es una decisión escrita, no un <c>catch</c> vacío». Se anota un SEGUNDO
/// asiento local —<c>platform.audit.forward</c> / <c>failure</c>— que nombra el id del que no
/// llegó y por qué. Un rastro que se pierde en silencio se pierde justo el día que hace falta, y
/// no hay forma de saber que faltaba.</para>
///
/// <para><b>Ese asiento del hueco se escribe por el escritor LOCAL, no por éste.</b> Reenviarlo
/// sería intentar contarle a la capacidad caída que no se pudo hablar con ella — un lazo que no
/// termina mientras dure la caída.</para>
///
/// <para><b>El correo no sale de esta máquina.</b> Lo que viaja como actor es un seudónimo estable
/// del correo; el nombre para leer se queda en el JSONL. Es la lección de #35 y del defecto #47:
/// un dato personal escrito en el disco de otro servicio es un segundo sitio donde borrar el día
/// que alguien ejerce el derecho al olvido, y la bitácora es justo la que más se conserva.</para>
///
/// <para><b>Y el asiento no afirma que el actor fuera quien dice ser.</b> Lo que viaja es
/// <see cref="AuditEvent.Assertion"/>, y cuando el asiento no registra ninguna se manda el SUELO
/// —<c>CmsSession</c>, que significa «nos fiamos de quien llama»—, porque desde la #72 la
/// capacidad exige una y sin ella cada asiento se volvería un hueco. Ese suelo es la <i>ausencia</i>
/// de comprobación, no una comprobación inventada; lo que nunca se manda de más es
/// <c>IdentityToken</c>, y si un asiento lo declarara sin prueba la capacidad lo rechazaría
/// (<c>assertion_not_proven</c>).</para>
/// </remarks>
public sealed class HttpAuditTrailWriter : IAuditTrailWriter
{
    /// <summary>Cliente nombrado que registra el composer.</summary>
    public const string ClientName = "synergos-api-audit";

    /// <summary>Cabecera de la llave compartida. La misma que exige toda capacidad.</summary>
    public const string ApiKeyHeader = "X-Synergos-Key";

    /// <summary>La acción con la que se anota que un asiento no llegó a la capacidad.</summary>
    public const string ForwardFailureAction = "platform.audit.forward";

    /// <summary>Actor de los asientos que no tienen persona detrás.</summary>
    private const string ActorDelSistema = "sistema";

    /// <summary>Tope de <c>Api.Audit</c> para un valor de detalle. Se recorta acá, no allá.</summary>
    private const int MaxDetalle = 512;

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly IAuditTrailWriter _local;
    private readonly IHttpClientFactory _clients;
    private readonly IOptionsMonitor<AuditSettings> _settings;
    private readonly ILogger<HttpAuditTrailWriter> _log;

    public HttpAuditTrailWriter(
        IAuditTrailWriter local,
        IHttpClientFactory clients,
        IOptionsMonitor<AuditSettings> settings,
        ILogger<HttpAuditTrailWriter> log)
    {
        _local = local;
        _clients = clients;
        _settings = settings;
        _log = log;
    }

    public async Task WriteAsync(AuditEvent evt, CancellationToken cancellationToken)
    {
        // Primero lo durable de este lado. Lo que se lee sale de acá.
        await _local.WriteAsync(evt, cancellationToken).ConfigureAwait(false);

        // El asiento del hueco NO se reenvía: sería contarle a la capacidad caída que no se pudo
        // hablar con ella, y el fallo del reenvío generaría otro asiento del hueco, sin fin.
        if (string.Equals(evt.Action, ForwardFailureAction, StringComparison.Ordinal)) return;

        var causa = await ReenviarAsync(evt, cancellationToken).ConfigureAwait(false);
        if (causa is null) return;

        _log.LogError("El asiento {Id} ({Action}) no llegó a Api.Audit: {Causa}", evt.Id, evt.Action, causa);

        await _local.WriteAsync(
            new AuditEvent(
                Id: Guid.NewGuid().ToString("N"),
                OccurredAtUtc: DateTime.UtcNow,
                ActorEmail: string.Empty,
                ActorName: ActorDelSistema,
                Action: ForwardFailureAction,
                Resource: evt.Id,
                Outcome: "failure",
                Detail: Recortar($"{evt.Action}: {causa}"),
                // No consta, y es la verdad: no actuó nadie, falló una máquina.
                Assertion: IdentityAssertions.None),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Manda el asiento. Devuelve <c>null</c> si llegó, o la causa si no.</summary>
    private async Task<string?> ReenviarAsync(AuditEvent evt, CancellationToken ct)
    {
        var s = _settings.CurrentValue;

        using var req = new HttpRequestMessage(HttpMethod.Post, "v1/entries")
        {
            Content = JsonContent.Create(new
            {
                actorKind = s.ActorKind,
                // El SEUDÓNIMO, no el correo. Estable, así que sigue agrupando por persona sin
                // que el dato personal salga de esta máquina.
                actorId = Seudonimo(evt.ActorEmail),
                actorRoles = Array.Empty<string>(),
                action = evt.Action,
                targetKind = s.TargetKind,
                // Un asiento sin recurso sigue contestando «quién hizo qué», así que no se
                // descarta: se nombra la ausencia. Api.Audit exige las dos partes del Ref.
                targetId = string.IsNullOrWhiteSpace(evt.Resource) ? "(sin recurso)" : evt.Resource.Trim(),
                details = Detalles(evt),
                // CON QUÉ SE AFIRMÓ, EN EL CAMPO QUE LA CAPACIDAD RESUELVE (#72), no en un
                // detalle: ella lo guarda como `ActedWith` tras comprobarlo, y tenerlo además
                // suelto en `details` daría dos sitios que pueden discrepar sobre un mismo hecho.
                //
                // El SUELO es `CmsSession` y no es un relleno: significa «nos fiamos de quien
                // llama», o sea la AUSENCIA de comprobación, que es exactamente lo que hay en un
                // asiento que no registra ninguna. Lo que sí sería inventar es mandar
                // `IdentityToken`, y eso no pasa acá — se manda lo que el asiento diga, y si dice
                // algo fuerte sin prueba la capacidad lo rechaza a gritos (`assertion_not_proven`).
                //
                // Y no se puede omitir: sin afirmación la capacidad rechaza con
                // `access_requires_identity`, así que cada asiento se volvería un hueco.
                assertion = string.IsNullOrWhiteSpace(evt.Assertion)
                    ? IdentityAssertions.CmsSession
                    : evt.Assertion,
            }),
        };

        // El id del asiento ES la llave: el seam ya deduplica por él de este lado, así que un
        // reintento tras un timeout no puede escribir dos veces lo mismo en la capacidad.
        req.Headers.TryAddWithoutValidation("Idempotency-Key", $"cms-audit-{evt.Id}");

        try
        {
            using var res = await _clients.CreateClient(ClientName).SendAsync(req, ct).ConfigureAwait(false);
            if (res.IsSuccessStatusCode) return null;

            if (res.StatusCode == HttpStatusCode.Unauthorized)
            {
                return "401 — revisar Synergos:Audit:ApiKey";
            }

            var problema = await LeerProblemaAsync(res, ct).ConfigureAwait(false);
            return $"{(int)res.StatusCode} {problema.Code ?? "-"}: {problema.Detail ?? "-"}";
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Se va el proceso o se cortó la petición: no es un fallo del reenvío y anotarlo como
            // tal llenaría la bitácora de huecos falsos en cada apagado.
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            return ex.Message;
        }
    }

    private static Dictionary<string, string> Detalles(AuditEvent evt)
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["outcome"] = Recortar(evt.Outcome),
        };

        if (!string.IsNullOrWhiteSpace(evt.Detail)) d["detail"] = Recortar(evt.Detail);
        return d;
    }

    private static string Recortar(string? texto)
    {
        var t = texto ?? string.Empty;
        return t.Length <= MaxDetalle ? t : t[..MaxDetalle];
    }

    /// <summary>
    /// Cómo ve la capacidad a quien actuó: una huella estable del correo, nunca el correo.
    /// </summary>
    /// <remarks>
    /// <c>string.GetHashCode()</c> NO sirve: .NET lo aleatoriza por proceso, así que la misma
    /// persona sería un actor distinto tras cada reinicio y la bitácora dejaría de agrupar.
    /// </remarks>
    internal static string Seudonimo(string? actorEmail)
    {
        var correo = (actorEmail ?? string.Empty).Trim().ToLowerInvariant();
        if (correo.Length == 0) return ActorDelSistema;

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(correo));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }

    private static async Task<ProblemDto> LeerProblemaAsync(HttpResponseMessage res, CancellationToken ct)
    {
        try
        {
            return await res.Content.ReadFromJsonAsync<ProblemDto>(Json, ct).ConfigureAwait(false) ?? new ProblemDto();
        }
        catch (Exception ex) when (ex is JsonException or HttpRequestException or NotSupportedException)
        {
            return new ProblemDto();
        }
    }

    // ── Lo que se LEE sale del JSONL, siempre ───────────────────────────────
    //
    // No es una simplificación: el seam lee de forma síncrona y la capacidad habla HTTP. Bloquear
    // sobre la red en cada carga del backoffice sería peor que no tener la capacidad, y un
    // administrador que no puede mirar la bitácora porque un servicio no contesta es exactamente
    // el modo de fallo que #46 evitó en el timeline de pedidos.

    public IReadOnlyList<AuditEvent> GetRecent(int maxItems, string? actorEmailFilter = null, string? actionFilter = null)
        => _local.GetRecent(maxItems, actorEmailFilter, actionFilter);

    public IReadOnlyList<AuditEvent> GetByDateRange(DateTime fromUtc, DateTime toUtc, int maxItems,
        string? actorEmailFilter = null, string? actionFilter = null)
        => _local.GetByDateRange(fromUtc, toUtc, maxItems, actorEmailFilter, actionFilter);

    public AuditEvent? GetById(string id) => _local.GetById(id);

    private sealed record ProblemDto
    {
        public string? Title { get; init; }
        public string? Detail { get; init; }
        public string? Code { get; init; }
    }
}
