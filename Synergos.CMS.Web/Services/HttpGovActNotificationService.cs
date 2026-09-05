using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// El acto notificado contra <c>Synergos.Api.Messaging</c> (HU #62, rebanada 2).
/// </summary>
/// <remarks>
/// <para><b>Es el primer consumidor de esa capacidad</b>, y no hizo falta añadirle nada: un hilo
/// por expediente, el acto como mensaje con su plazo, el PDF como referencia a
/// <c>Api.Documents</c>, y el acuse con la afirmación de identidad que la capacidad
/// <i>verifica</i> desde la HU #14. Llevaba meses construida y sin nadie que la usara.</para>
///
/// <para><b>Lo que se queda de este lado</b>: el expediente. El radicado, el título del acto,
/// quién es el ciudadano dueño y la bandeja de «mis notificaciones» no cruzan — una capacidad de
/// mensajería no sabe qué es un radicado, y meterle ese sustantivo la inutilizaría para el
/// siguiente dominio.</para>
///
/// <para><b>Y aquí la identidad es de una persona de verdad.</b> La cara de Gobierno autentica
/// Members: quien abre sale de la cookie de sesión, no del cuerpo. Con
/// <c>Synergos:Identity:Mode=Api</c> el CMS pide un token para ese ciudadano y lo presenta, así
/// que el acuse queda respaldado por <c>IdentityToken</c>; sin él, por <c>CmsSession</c>. Los dos
/// son honestos y la capacidad los distingue.</para>
///
/// <para><b>Si el acuse no se pudo registrar, esto LANZA.</b> Devolver la notificación como
/// abierta sin que la capacidad lo haya anotado dejaría al ciudadano leyendo un acto cuyo término
/// nadie empezó a contar — y a la entidad creyendo que notificó.</para>
/// </remarks>
public sealed class HttpGovActNotificationService : IGovActNotificationService
{
    /// <summary>Cliente nombrado que registra el composer.</summary>
    public const string ClientName = "synergos-api-messaging";

    /// <summary>Cabecera de la llave compartida. La misma que exige toda capacidad.</summary>
    public const string ApiKeyHeader = "X-Synergos-Key";

    /// <summary>Cabecera con la que viaja la identidad verificable de quien accede.</summary>
    public const string IdentityHeader = "X-Synergos-Identity";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly JsonSerializerOptions Disco = new() { WriteIndented = true };

    private readonly IHttpClientFactory _clients;
    private readonly IOptionsMonitor<GovNotificationSettings> _settings;
    private readonly IJsonEntityStore _store;
    private readonly IIdentityTokenIssuer _identidad;
    private readonly ILogger<HttpGovActNotificationService> _log;

    public HttpGovActNotificationService(
        IHttpClientFactory clients,
        IOptionsMonitor<GovNotificationSettings> settings,
        IJsonEntityStore store,
        ILogger<HttpGovActNotificationService> log,
        IIdentityTokenIssuer identity)
    {
        _clients = clients;
        _settings = settings;
        _store = store;
        _log = log;
        _identidad = identity;
    }

    // ── Notificar ───────────────────────────────────────────────────────────

    public async Task<GovActNotification> NotifyAsync(
        string caseId,
        string radicado,
        Guid citizenMemberKey,
        string title,
        string body,
        string? documentRef = null,
        DateTimeOffset? acknowledgeBeforeUtc = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(caseId)) throw new ArgumentException("El expediente es obligatorio.", nameof(caseId));
        if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("El acto necesita título.", nameof(title));
        if (citizenMemberKey == Guid.Empty) throw new ArgumentException("Hace falta el ciudadano.", nameof(citizenMemberKey));

        // Un acto se notifica UNA vez: re-notificarlo devuelve el que ya está, sin abrir un
        // segundo hilo ni reiniciar su plazo.
        var previa = (await LeerTodasAsync(cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(g => string.Equals(g.Notification.CaseId, caseId.Trim(), StringComparison.Ordinal)
                              && string.Equals(g.Notification.Title, title.Trim(), StringComparison.Ordinal));
        if (previa is not null) return previa.Notification;

        var s = _settings.CurrentValue;
        var ciudadano = Ciudadano(citizenMemberKey);

        var hilo = await AbrirHiloAsync(caseId.Trim(), ciudadano, s, cancellationToken).ConfigureAwait(false);
        var mensaje = await PublicarActoAsync(
            hilo.Id, body ?? string.Empty, documentRef, acknowledgeBeforeUtc, s, cancellationToken)
            .ConfigureAwait(false);

        var notificacion = new GovActNotification(
            Id: mensaje.Id,
            CaseId: caseId.Trim(),
            Radicado: (radicado ?? string.Empty).Trim(),
            Title: title.Trim(),
            Body: (body ?? string.Empty).Trim(),
            DocumentRef: string.IsNullOrWhiteSpace(documentRef) ? null : documentRef.Trim(),
            NotifiedAtUtc: mensaje.AtUtc,
            AcknowledgeBeforeUtc: mensaje.AcknowledgeBeforeUtc);

        await GuardarAsync(notificacion, citizenMemberKey, hilo.Id, cancellationToken).ConfigureAwait(false);
        return notificacion;
    }

    private async Task<ThreadDto> AbrirHiloAsync(
        string caseId, string ciudadano, GovNotificationSettings s, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "v1/threads")
        {
            Content = JsonContent.Create(new
            {
                topicKind = "gov.expediente",
                topicId = caseId,
                participants = new[]
                {
                    new { kind = s.EntityKind, id = s.EntityId },
                    new { kind = s.CitizenKind, id = ciudadano },
                },
            }),
        };
        // La llave lleva el expediente: el hilo de un expediente es UNO, y reintentar tras un
        // timeout no puede abrir un segundo canal para el mismo trámite.
        req.Headers.TryAddWithoutValidation("Idempotency-Key", $"gov-thread-{caseId}");

        return await EnviarAsync<ThreadDto>(req, "abrir el canal de notificación", ct).ConfigureAwait(false);
    }

    private async Task<MessageDto> PublicarActoAsync(
        string threadId, string body, string? documentRef, DateTimeOffset? plazo,
        GovNotificationSettings s, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"v1/threads/{Uri.EscapeDataString(threadId)}/messages")
        {
            Content = JsonContent.Create(new
            {
                fromKind = s.EntityKind,
                fromId = s.EntityId,
                body,
                // El PDF viaja como REFERENCIA. El binario vive en Api.Documents, con su lista
                // blanca, su huella y sus enlaces firmados — duplicarlo acá crearía dos sitios
                // donde borrar cuando alguien ejerce el derecho al olvido.
                attachments = string.IsNullOrWhiteSpace(documentRef)
                    ? Array.Empty<object>()
                    : new object[] { new { kind = "gov.documento", id = documentRef.Trim() } },
                acknowledgeBeforeUtc = plazo,
            }),
        };
        req.Headers.TryAddWithoutValidation("Idempotency-Key", $"gov-act-{threadId}-{Huella(body)}");

        return await EnviarAsync<MessageDto>(req, "poner el acto en conocimiento", ct).ConfigureAwait(false);
    }

    // ── Abrir ───────────────────────────────────────────────────────────────

    public async Task<GovActNotification> AcknowledgeAsync(
        string notificationId, Guid memberKey, CancellationToken cancellationToken = default)
    {
        var guardada = await LeerAsync(notificationId, cancellationToken).ConfigureAwait(false)
            ?? throw new ArgumentException($"Notificación '{notificationId}' no encontrada.", nameof(notificationId));

        if (guardada.Owner != memberKey)
        {
            throw new GovActNotAddresseeException();
        }

        // El PRIMER acceso es el que cuenta. La capacidad también es idempotente, pero no se
        // delega en eso: lo que fija el término es lo que este lado registró primero.
        if (guardada.Notification.Opened) return guardada.Notification;

        var s = _settings.CurrentValue;
        var ciudadano = Ciudadano(memberKey);

        // La identidad de quien abre, si el despliegue sabe emitirla (HU #14 rebanada 4). Acá es
        // una persona de verdad: sale de la cookie de sesión, no del cuerpo de la petición.
        var token = await _identidad.IssueAsync(
            new IdentitySubject(s.CitizenKind, ciudadano, Array.Empty<string>()), cancellationToken)
            .ConfigureAwait(false);

        using var req = new HttpRequestMessage(
            HttpMethod.Post, $"v1/messages/{Uri.EscapeDataString(guardada.Notification.Id)}/acknowledge")
        {
            Content = JsonContent.Create(new
            {
                whoKind = s.CitizenKind,
                whoId = ciudadano,
                // Lo DECLARADO es lo más débil que se puede afirmar sin prueba. Si se presentó
                // token, la capacidad lo verifica y sube la afirmación por su cuenta — no se le
                // pide que crea lo que decimos (defecto #42).
                assertion = GovActAssertions.CmsSession,
            }),
        };

        if (!string.IsNullOrWhiteSpace(token))
        {
            req.Headers.TryAddWithoutValidation(IdentityHeader, token);
        }

        var mensaje = await EnviarAsync<MessageDto>(req, "registrar el acceso al acto", cancellationToken)
            .ConfigureAwait(false);

        // Lo que quedó anotado lo dice la CAPACIDAD, no este lado: si el token subió la afirmación
        // a IdentityToken, es ella quien lo sabe. Escribir acá «CmsSession» porque es lo que
        // mandamos dejaría el registro mintiendo hacia abajo.
        var acuse = mensaje.Acknowledgments?.FirstOrDefault(a =>
            string.Equals(a.WhoId, ciudadano, StringComparison.Ordinal));

        if (acuse is null)
        {
            throw new InvalidOperationException(
                "Api.Messaging aceptó la petición pero no devolvió el acuse: el acceso no queda certificado.");
        }

        var abierta = guardada.Notification with
        {
            OpenedAtUtc = acuse.AtUtc,
            OpenedBy = memberKey,
            OpenedWith = acuse.Assertion,
        };

        await GuardarAsync(abierta, memberKey, guardada.ThreadId, cancellationToken).ConfigureAwait(false);
        return abierta;
    }

    // ── Las bandejas, que se leen de ESTE lado ──────────────────────────────
    //
    // Igual que el timeline de pedidos (#46): con la capacidad caída, el ciudadano SIGUE viendo
    // sus notificaciones y si las abrió. Lo que se para es notificar y abrir, no mirar.

    public async Task<IReadOnlyList<GovActNotification>> GetForCaseAsync(
        string caseId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(caseId)) return Array.Empty<GovActNotification>();

        return (await LeerTodasAsync(cancellationToken).ConfigureAwait(false))
            .Where(g => string.Equals(g.Notification.CaseId, caseId.Trim(), StringComparison.Ordinal))
            .Select(g => g.Notification)
            .OrderByDescending(n => n.NotifiedAtUtc)
            .ToList();
    }

    public async Task<IReadOnlyList<GovActNotification>> GetForCitizenAsync(
        Guid memberKey, CancellationToken cancellationToken = default)
    {
        if (memberKey == Guid.Empty) return Array.Empty<GovActNotification>();

        return (await LeerTodasAsync(cancellationToken).ConfigureAwait(false))
            .Where(g => g.Owner == memberKey)
            .Select(g => g.Notification)
            .OrderByDescending(n => n.NotifiedAtUtc)
            .ToList();
    }

    // ── Traducciones ────────────────────────────────────────────────────────

    /// <summary>
    /// El ciudadano tal como lo ve la capacidad: la llave del Member, no su correo.
    /// </summary>
    /// <remarks>
    /// El correo es dato personal y quedaría escrito en el disco de otro servicio; la llave es
    /// opaca y estable. Es la lección de #35 y del defecto #47, aplicada de entrada.
    /// </remarks>
    internal static string Ciudadano(Guid memberKey) => memberKey.ToString("N");

    /// <summary>
    /// Huella estable del cuerpo, para la llave de idempotencia del acto.
    /// </summary>
    /// <remarks>
    /// <c>string.GetHashCode()</c> NO sirve: .NET lo aleatoriza por proceso, así que el mismo acto
    /// reintentado tras un reinicio traería otra llave y se publicaría dos veces.
    /// </remarks>
    internal static string Huella(string texto)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(texto));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }

    private async Task<T> EnviarAsync<T>(HttpRequestMessage req, string queHacia, CancellationToken ct)
    {
        var http = _clients.CreateClient(ClientName);

        HttpResponseMessage res;
        try
        {
            res = await http.SendAsync(req, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            // NO se traga: un fallo de red que devolviera «notificado» sería el peor defecto
            // posible de este camino — la entidad creería que un término empezó.
            _log.LogError(ex, "No se pudo {Que}: Api.Messaging no respondió.", queHacia);
            throw new InvalidOperationException($"No pudimos {queHacia}. Vuelve a intentarlo.", ex);
        }

        using (res)
        {
            if (res.IsSuccessStatusCode)
            {
                var cuerpo = await res.Content.ReadFromJsonAsync<T>(Json, ct).ConfigureAwait(false);
                return cuerpo ?? throw new InvalidOperationException($"No pudimos {queHacia}: la respuesta vino vacía.");
            }

            var problema = await LeerProblemaAsync(res, ct).ConfigureAwait(false);

            if (res.StatusCode == HttpStatusCode.Unauthorized)
            {
                _log.LogError("Api.Messaging respondió 401 al {Que}: revisar Synergos:Gob:Notifications:ApiKey.", queHacia);
                throw new InvalidOperationException($"No pudimos {queHacia}.");
            }

            _log.LogWarning("Api.Messaging rechazó {Que} con {Status} ({Code}): {Detalle}",
                queHacia, (int)res.StatusCode, problema.Code ?? "-", problema.Detail ?? "-");

            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(problema.Detail) ? $"No pudimos {queHacia}." : problema.Detail!);
        }
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

    // ── Lo que este lado recuerda ───────────────────────────────────────────

    /// <summary>La notificación, su dueño y el hilo donde vive.</summary>
    /// <remarks>
    /// El dueño NO va dentro de <see cref="GovActNotification"/>: ese tipo cruza hacia la vista y
    /// la llave del Member de otra persona no tiene por qué salir del servidor.
    /// </remarks>
    private sealed record Guardada(GovActNotification Notification, Guid Owner, string ThreadId);

    private Task GuardarAsync(GovActNotification n, Guid owner, string threadId, CancellationToken ct)
        => _store.WriteAsync(
            Synergos.CMS.Application.Services.Impl.StubGovActNotificationService.ResourceType, n.Id,
            JsonSerializer.Serialize(new Guardada(n, owner, threadId), Disco), ct);

    private async Task<Guardada?> LeerAsync(string? id, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        var json = await _store.ReadAsync(
            Synergos.CMS.Application.Services.Impl.StubGovActNotificationService.ResourceType, id.Trim(), ct)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<Guardada>(json, Disco); }
        catch (JsonException) { return null; }
    }

    private async Task<List<Guardada>> LeerTodasAsync(CancellationToken ct)
    {
        var crudas = await _store.ListAsync(
            Synergos.CMS.Application.Services.Impl.StubGovActNotificationService.ResourceType, ct)
            .ConfigureAwait(false);
        var todas = new List<Guardada>(crudas.Count);
        foreach (var json in crudas)
        {
            if (string.IsNullOrWhiteSpace(json)) continue;
            Guardada? g;
            try { g = JsonSerializer.Deserialize<Guardada>(json, Disco); }
            catch (JsonException) { continue; }
            if (g?.Notification is not null) todas.Add(g);
        }
        return todas;
    }

    // Los DTO viven acá y NO en Synergos.CMS.Interfaces: son la forma del contrato HTTP con otro
    // servicio, no vocabulario del dominio del CMS.

    internal sealed record RefDto(string Kind, string Id);

    internal sealed record ThreadDto(string Id, string TopicKind, string TopicId);

    internal sealed record AcknowledgmentDto(string WhoKind, string WhoId, DateTimeOffset AtUtc, string Assertion);

    internal sealed record MessageDto(
        string Id, string ThreadId, string Body, DateTimeOffset AtUtc,
        DateTimeOffset? AcknowledgeBeforeUtc, IReadOnlyList<AcknowledgmentDto>? Acknowledgments);

    private sealed record ProblemDto
    {
        public string? Title { get; init; }
        public string? Detail { get; init; }
        public string? Code { get; init; }
    }
}
