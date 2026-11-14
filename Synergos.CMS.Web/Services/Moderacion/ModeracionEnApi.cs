using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Synergos.CMS.Interfaces.Moderacion;
using Synergos.CMS.Web.Composers;

namespace Synergos.CMS.Web.Services.Moderacion;

/// <summary>
/// La moderación en <c>Api</c> (012-blogs-social, I5): los casos viven en <c>Api.Moderation</c>, según el contrato que
/// publica (<c>docs/contracts/openapi/Synergos.Api.Moderation.json</c>). Retirar lo sigue haciendo el CMS.
/// </summary>
/// <remarks>
/// <para><b>La capacidad abre un caso por denuncia; la cola del CMS es un caso por objetivo.</b> Al leer la cola se
/// agrupan los abiertos de cada objetivo, y decidir decide todos los abiertos del objetivo. Lo que la cola muestra de
/// lo denunciado se lee al pintarla con el <see cref="ITipoModerable"/> de su tipo: la capacidad no guarda contenido.</para>
///
/// <para><b>Por el cable no viaja un correo</b> (I0): quien denuncia y quien decide van por su llave pública. La misma
/// persona denunciando lo mismo es la misma petición (<c>Idempotency-Key</c> de su llave y el objetivo).</para>
///
/// <para><b>Lo que no contestó no se finge</b>: la red, el plazo, un 5xx, un 401 de llave o un cuerpo ilegible son
/// <see cref="ModeracionNoDisponibleException"/>, y quien llama dice que no se pudo.</para>
/// </remarks>
public sealed class ModeracionEnApi : IModeracion
{
    /// <summary>El cliente nombrado hacia <c>Api.Moderation</c>.</summary>
    public const string ClientName = "synergos-api-moderation";

    /// <summary>La clase de quien denuncia o decide en la capacidad: un miembro, por su llave pública.</summary>
    public const string Miembro = "miembro";

    /// <summary>Cuántos casos se piden por lectura: el tope de la capacidad.</summary>
    private const int Limite = 100;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _clients;
    private readonly IEnumerable<ITipoModerable> _tipos;
    private readonly TimeProvider _reloj;
    private readonly ILogger<ModeracionEnApi> _log;

    /// <summary>Sobre el cliente del árbol de servicios, lo denunciable de cada vertical y el reloj.</summary>
    public ModeracionEnApi(
        IHttpClientFactory clients,
        IEnumerable<ITipoModerable> tipos,
        TimeProvider reloj,
        ILogger<ModeracionEnApi> log)
    {
        _clients = clients ?? throw new ArgumentNullException(nameof(clients));
        _tipos = tipos ?? throw new ArgumentNullException(nameof(tipos));
        _reloj = reloj ?? throw new ArgumentNullException(nameof(reloj));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <inheritdoc />
    public async Task<CasoDeModeracion> DenunciarAsync(
        DenunciaNueva denuncia,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(denuncia);
        using var peticion = new HttpRequestMessage(HttpMethod.Post, "v1/cases")
        {
            Content = JsonContent.Create(
                new ReportBody(denuncia.Objetivo.Tipo, denuncia.Objetivo.Id, Miembro, denuncia.Denunciante, denuncia.Motivo),
                options: Json),
        };
        peticion.Headers.TryAddWithoutValidation(
            ClienteDelArbolDeServicios.CabeceraDeIdempotencia,
            Llave(denuncia.Objetivo.Tipo, denuncia.Objetivo.Id, denuncia.Denunciante));

        var caso = await LeerAsync<CaseDto>(peticion, cancellationToken).ConfigureAwait(false)
            ?? throw NoContesto("la denuncia no devolvió su caso");
        return Caso([caso], denuncia.Resumen, denuncia.Enlace, null);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CasoDeModeracion>> AbiertosAsync(
        int cuantos,
        CancellationToken cancellationToken = default)
    {
        using var peticion = new HttpRequestMessage(HttpMethod.Get, $"v1/cases?limit={Limite}");
        var cola = await LeerAsync<PageDto<CaseDto>>(peticion, cancellationToken).ConfigureAwait(false)
            ?? throw NoContesto("la cola no devolvió su página");

        var casos = new List<CasoDeModeracion>();
        foreach (var grupo in cola.Items.Where(EstaAbierto).GroupBy(c => (c.TargetKind, c.TargetId)))
        {
            var (resumen, enlace) = await ResumenAsync(grupo.Key.TargetKind, grupo.Key.TargetId, cancellationToken)
                .ConfigureAwait(false);
            casos.Add(Caso(grupo.ToList(), resumen, enlace, null));
        }

        return casos
            .OrderBy(c => c.Denunciado)
            .ThenBy(c => c.Id, StringComparer.Ordinal)
            .Take(Math.Clamp(cuantos, 1, 200))
            .ToList();
    }

    /// <inheritdoc />
    public async Task<CasoDeModeracion?> CasoAsync(string id, CancellationToken cancellationToken = default)
    {
        var caso = await UnCasoAsync(id, cancellationToken).ConfigureAwait(false);
        if (caso is null)
        {
            return null;
        }

        var (resumen, enlace) = await ResumenAsync(caso.TargetKind, caso.TargetId, cancellationToken).ConfigureAwait(false);
        return Caso([caso], resumen, enlace, null);
    }

    /// <inheritdoc />
    public async Task<DecisionTomada> DecidirAsync(
        string id,
        DecisionDeModeracion decision,
        QuienModera quien,
        string porQue,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(quien);
        var caso = await UnCasoAsync(id, cancellationToken).ConfigureAwait(false);
        if (caso is null)
        {
            return new DecisionTomada(ResultadoDeDecidir.NoExiste, null);
        }

        var palabra = decision == DecisionDeModeracion.Retirar ? "Remove" : "Keep";
        var delObjetivo = await DelObjetivoAsync(caso.TargetKind, caso.TargetId, cancellationToken).ConfigureAwait(false);
        var (resumen, enlace) = await ResumenAsync(caso.TargetKind, caso.TargetId, cancellationToken).ConfigureAwait(false);

        if (delObjetivo.Any(c => c.Decision is { } ya && !string.Equals(ya, palabra, StringComparison.OrdinalIgnoreCase)))
        {
            return new DecisionTomada(ResultadoDeDecidir.YaDecididoDistinto, Caso(delObjetivo, resumen, enlace, null));
        }

        foreach (var abierto in delObjetivo.Where(EstaAbierto))
        {
            using var peticion = new HttpRequestMessage(HttpMethod.Post, $"v1/cases/{Uri.EscapeDataString(abierto.Id)}/decide")
            {
                Content = JsonContent.Create(new DecideBody(palabra, Miembro, quien.Llave, porQue), options: Json),
            };
            var decidido = await LeerAsync<CaseDto>(peticion, cancellationToken, conflictoEsNulo: true).ConfigureAwait(false);
            if (decidido is null)
            {
                // Otra persona lo decidió entre la lectura y la escritura: vale si decidió lo mismo.
                var visto = await UnCasoAsync(abierto.Id, cancellationToken).ConfigureAwait(false);
                if (visto?.Decision is { } suya && !string.Equals(suya, palabra, StringComparison.OrdinalIgnoreCase))
                {
                    return new DecisionTomada(ResultadoDeDecidir.YaDecididoDistinto, Caso(delObjetivo, resumen, enlace, null));
                }
            }
        }

        return new DecisionTomada(
            ResultadoDeDecidir.Decidido,
            Caso(delObjetivo, resumen, enlace, new Decidido(decision, quien.Nombre, porQue, _reloj.GetUtcNow())) with { Id = caso.Id });
    }

    private static bool EstaAbierto(CaseDto caso) => string.Equals(caso.Status, "Open", StringComparison.OrdinalIgnoreCase);

    /// <summary>Un caso de la cola: el más antiguo de los del objetivo da el id, el motivo y la fecha.</summary>
    private static CasoDeModeracion Caso(
        IReadOnlyList<CaseDto> delObjetivo,
        string resumen,
        string? enlace,
        Decidido? decidido)
    {
        var primero = delObjetivo.OrderBy(c => c.ReportedAtUtc).ThenBy(c => c.Id, StringComparer.Ordinal).First();
        return new CasoDeModeracion(
            primero.Id,
            new ObjetivoModerable(primero.TargetKind, primero.TargetId),
            resumen,
            enlace,
            primero.Reason,
            delObjetivo.Count,
            primero.ReportedAtUtc,
            decidido?.Decision,
            decidido?.Quien,
            decidido?.PorQue,
            decidido?.Cuando);
    }

    /// <summary>Lo que la cola muestra de lo denunciado, leído ahora por su tipo; si ya no se ve, su tipo y su id.</summary>
    private async Task<(string Resumen, string? Enlace)> ResumenAsync(string tipo, string id, CancellationToken ct)
    {
        var deEseTipo = _tipos.FirstOrDefault(t => string.Equals(t.Tipo, tipo, StringComparison.OrdinalIgnoreCase));
        var contenido = deEseTipo is null ? null : await deEseTipo.LeerAsync(id, string.Empty, ct).ConfigureAwait(false);
        return contenido is null ? ($"{tipo} {id}", null) : (contenido.Resumen, contenido.Enlace);
    }

    private async Task<CaseDto?> UnCasoAsync(string id, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        using var peticion = new HttpRequestMessage(HttpMethod.Get, $"v1/cases/{Uri.EscapeDataString(id.Trim())}");
        return await LeerAsync<CaseDto>(peticion, ct, noExisteEsNulo: true).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<CaseDto>> DelObjetivoAsync(string tipo, string id, CancellationToken ct)
    {
        using var peticion = new HttpRequestMessage(
            HttpMethod.Get,
            $"v1/cases?targetKind={Uri.EscapeDataString(tipo)}&targetId={Uri.EscapeDataString(id)}&limit={Limite}");
        var pagina = await LeerAsync<PageDto<CaseDto>>(peticion, ct).ConfigureAwait(false)
            ?? throw NoContesto("los casos del objetivo no devolvieron su página");
        return pagina.Items;
    }

    /// <summary>
    /// Manda y lee. Lo que no contestó como se esperaba es <see cref="ModeracionNoDisponibleException"/>; un 404 o un
    /// 409, si quien llama sabe qué hacer con ellos, es <c>null</c>.
    /// </summary>
    private async Task<T?> LeerAsync<T>(
        HttpRequestMessage peticion,
        CancellationToken ct,
        bool noExisteEsNulo = false,
        bool conflictoEsNulo = false)
        where T : class
    {
        HttpResponseMessage respuesta;
        try
        {
            respuesta = await _clients.CreateClient(ClientName).SendAsync(peticion, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            throw NoContesto(ex.GetType().Name, ex);
        }

        using (respuesta)
        {
            if ((noExisteEsNulo && respuesta.StatusCode == HttpStatusCode.NotFound)
                || (conflictoEsNulo && respuesta.StatusCode == HttpStatusCode.Conflict))
            {
                return null;
            }

            if (!respuesta.IsSuccessStatusCode)
            {
                throw NoContesto($"{(int)respuesta.StatusCode} en {peticion.Method} {peticion.RequestUri}");
            }

            try
            {
                return await respuesta.Content.ReadFromJsonAsync<T>(Json, ct).ConfigureAwait(false);
            }
            catch (JsonException ex)
            {
                throw NoContesto("un cuerpo ilegible", ex);
            }
        }
    }

    private ModeracionNoDisponibleException NoContesto(string causa, Exception? inner = null)
    {
        _log.LogWarning(inner, "Api.Moderation no contestó ({Causa}): lo pedido no quedó.", causa);
        var mensaje = $"Api.Moderation no contestó: {causa}.";
        return inner is null ? new ModeracionNoDisponibleException(mensaje) : new ModeracionNoDisponibleException(mensaje, inner);
    }

    /// <summary>La llave de una denuncia: la misma persona y el mismo objetivo son la misma petición.</summary>
    private static string Llave(string tipo, string id, string denunciante)
        => "cms-denuncia-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{tipo}\n{id}\n{denunciante}")))
            .ToLowerInvariant()[..40];

    private sealed record Decidido(DecisionDeModeracion Decision, string Quien, string PorQue, DateTimeOffset Cuando);

    /// <summary>Lo que se manda al denunciar (<c>ReportRequest</c>).</summary>
    private sealed record ReportBody(string TargetKind, string TargetId, string ReporterKind, string ReporterId, string Reason);

    /// <summary>Lo que se manda al decidir (<c>DecideRequest</c>).</summary>
    private sealed record DecideBody(string Decision, string DecidedByKind, string DecidedById, string Note);

    /// <summary>Lo que el CMS lee de un caso (<c>CaseResponse</c>).</summary>
    private sealed record CaseDto(
        string Id,
        string TargetKind,
        string TargetId,
        string Reason,
        string Status,
        string? Decision,
        DateTimeOffset ReportedAtUtc);

    /// <summary>Lo que el CMS lee de una página de casos (<c>PageResponse</c>).</summary>
    private sealed record PageDto<T>(IReadOnlyList<T> Items, int Total, int Offset, bool HasMore);
}

/// <summary>
/// La moderación encendida en <c>Api</c> sin dirección: no se adivina un puerto, así que no contesta. Denunciar dice que
/// no se pudo y la cola no se pinta vacía (012-blogs-social, I5).
/// </summary>
public sealed class ModeracionSinDestino : IModeracion
{
    private const string Motivo = "Synergos:Moderation:Mode es Api y no hay BaseUrl: la moderación no contesta.";

    /// <inheritdoc />
    public Task<CasoDeModeracion> DenunciarAsync(DenunciaNueva denuncia, CancellationToken cancellationToken = default)
        => Task.FromException<CasoDeModeracion>(new ModeracionNoDisponibleException(Motivo));

    /// <inheritdoc />
    public Task<IReadOnlyList<CasoDeModeracion>> AbiertosAsync(int cuantos, CancellationToken cancellationToken = default)
        => Task.FromException<IReadOnlyList<CasoDeModeracion>>(new ModeracionNoDisponibleException(Motivo));

    /// <inheritdoc />
    public Task<CasoDeModeracion?> CasoAsync(string id, CancellationToken cancellationToken = default)
        => Task.FromException<CasoDeModeracion?>(new ModeracionNoDisponibleException(Motivo));

    /// <inheritdoc />
    public Task<DecisionTomada> DecidirAsync(
        string id,
        DecisionDeModeracion decision,
        QuienModera quien,
        string porQue,
        CancellationToken cancellationToken = default)
        => Task.FromException<DecisionTomada>(new ModeracionNoDisponibleException(Motivo));
}
