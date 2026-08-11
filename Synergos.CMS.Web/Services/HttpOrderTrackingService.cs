using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// El <see cref="IOrderTrackingService"/> que valida el avance contra
/// <c>Synergos.Api.Workflow</c> (HU #46).
/// </summary>
/// <remarks>
/// <para><b>El pipeline deja de ser una constante en C#.</b> Había cuatro, uno por dominio, cada
/// uno un <c>static readonly</c> en una clase distinta; meter una etapa era un despliegue del
/// portal. La capacidad los tiene como dato.</para>
///
/// <para><b>Una definición por dominio</b> (<c>tracking.shop</c>, <c>tracking.travel</c>…) y no
/// una compartida. Los nombres de estado se repiten entre pipelines —<c>paid</c> está en tres,
/// <c>completed</c> en dos— así que una definición única leería la etapa de un dominio contra el
/// pipeline de otro: «enviado» convertido en «matriculado» sin que nada falle. Es el mismo
/// motivo por el que cada instancia del stub tiene su propio <c>storeNamespace</c>.</para>
///
/// <para><b>LEER no sale a la red, y es la diferencia deliberada con la HU #44.</b> El timeline
/// se pinta en cada vista de pedido: el almacén del CMS sigue siendo el modelo de lectura y la
/// capacidad sólo valida el avance. Con <c>Api.Workflow</c> caída, quien compró <b>sigue viendo
/// dónde va su pedido</b> y sólo se para avanzarlo. En Gobierno estaba prohibido caer al motor
/// local porque el riesgo era <i>decidir</i> con un proceso que quizá ya no es el vigente; acá
/// es <i>mostrar</i> lo que ya pasó, que no decide nada.</para>
///
/// <para><b>Saltar etapas se dispara EN SECUENCIA</b>, no con una transición de salto. El pedido
/// de verdad pasó por cada etapa —el pipeline es secuencial por construcción— y declarar saltos
/// haría perder las intermedias en la historia de la capacidad, que es justo lo que un timeline
/// necesita. El sello de tiempo de las intermedias ya era el mismo que el de la destino desde
/// antes de este cableado: la ficción existía, no se está inventando una nueva.</para>
/// </remarks>
public sealed class HttpOrderTrackingService : IOrderTrackingService
{
    /// <summary>Cabecera de la llave compartida. La misma que exige toda capacidad.</summary>
    public const string ApiKeyHeader = "X-Synergos-Key";

    /// <summary>Cliente nombrado que registra el composer.</summary>
    public const string ClientName = "synergos-api-workflow-tracking";

    /// <summary>Quién avanza un pedido: el sistema. No es el Kind del pedido.</summary>
    public const string ActorKind = "synergos.sistema";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IHttpClientFactory _factory;
    private readonly IOrderTrackingService _local;
    private readonly TrackingSettings _settings;
    private readonly IReadOnlyList<string> _pipeline;
    private readonly string _domain;

    /// <param name="factory">De dónde sale el cliente HTTP.</param>
    /// <param name="local">
    /// El almacén del CMS, que sigue siendo el modelo de LECTURA y quien sella las fechas.
    /// </param>
    /// <param name="settings">La sección <c>Synergos:Tracking</c>.</param>
    /// <param name="pipeline">
    /// Las etapas de ESTE dominio, en orden.
    /// </param>
    /// <param name="domain">
    /// Qué pipeline es éste: <c>shop</c>, <c>travel</c>, <c>events</c>, <c>academy</c>. Se le pega
    /// al prefijo para formar la clave de definición y el <c>Kind</c> del sujeto.
    /// </param>
    /// <remarks>
    /// <para><b>Por qué el ORDEN sigue siendo de este lado, y qué NO mudó por tanto.</b> Esta HU
    /// muda la <i>legalidad</i> —qué etapa puede seguir a cuál— a la capacidad. El orden y las
    /// <b>etiquetas</b> («Enviado», «Matriculado») se quedan acá, porque son presentación del
    /// dominio y la capacidad no sabe de eso (<c>CLAUDE.md</c> §12). Consecuencia honesta:
    /// añadir una etapa sigue necesitando su rótulo de este lado; lo que deja de necesitar es
    /// tocar la tabla de qué sigue a qué.</para>
    ///
    /// <para><b>Si el orden local y la definición no coinciden, se nota.</b> El recorrido se
    /// calcula con el orden local y cada paso lo valida la capacidad, así que un desacuerdo sale
    /// como rechazo con su motivo — no como un avance silencioso a un sitio equivocado.</para>
    /// </remarks>
    public HttpOrderTrackingService(
        IHttpClientFactory factory,
        IOrderTrackingService local,
        IOptions<TrackingSettings> settings,
        IReadOnlyList<OrderTrackingStageDefinition> pipeline,
        string domain)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _local = local ?? throw new ArgumentNullException(nameof(local));
        _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));

        ArgumentNullException.ThrowIfNull(pipeline);
        if (pipeline.Count == 0)
        {
            throw new ArgumentException("El pipeline requiere al menos una etapa.", nameof(pipeline));
        }
        _pipeline = pipeline.Select(e => e.Stage).ToList();

        if (string.IsNullOrWhiteSpace(domain))
        {
            // Sin dominio no hay definición que pedir, y caer a una compartida es exactamente el
            // defecto que este cableado existe para no cometer.
            throw new ArgumentException("Cada instancia necesita su dominio.", nameof(domain));
        }
        _domain = domain.Trim().ToLowerInvariant();
    }

    /// <summary>La clave de la definición de ESTE dominio.</summary>
    public string DefinitionKey => $"{_settings.DefinitionPrefix}.{_domain}";

    /// <summary>El <c>Kind</c> con el que viaja el pedido de ESTE dominio.</summary>
    public string SubjectKind => DefinitionKey;

    /// <summary>
    /// Lee del almacén del CMS. <b>No sale a la red</b> — ver el comentario de la clase.
    /// </summary>
    public Task<OrderTimeline?> GetTimelineAsync(string orderRef, CancellationToken cancellationToken = default)
        => _local.GetTimelineAsync(orderRef, cancellationToken);

    public async Task<OrderTimeline> AdvanceAsync(
        string orderRef,
        string stage,
        string? note = null,
        CancellationToken cancellationToken = default)
    {
        // Las validaciones de forma las sigue haciendo el motor local: son suyas y no cuestan una
        // llamada. Que la etapa pertenezca al pipeline lo dirá además la capacidad, pero saberlo
        // acá evita salir a la red por algo que ya se sabe.
        var actual = await _local.GetTimelineAsync(orderRef, cancellationToken);

        // Idempotente/monotónico: etapa ya alcanzada o anterior → ni se llama ni se falla, igual
        // que el motor en proceso. Preguntárselo a la capacidad daría `transition_not_allowed`,
        // que responde otra pregunta: «¿es legal esta transición?» y no «¿hace falta hacer algo?».
        if (actual is not null && YaAlcanzada(actual, stage))
        {
            return actual;
        }

        var faltantes = PorRecorrer(actual, stage);
        if (faltantes.Count > 0)
        {
            await AvanzarEnLaCapacidadAsync(orderRef, actual, faltantes, note, cancellationToken);
        }

        // Las fechas las sella el CMS, que es donde viven. La capacidad dijo que se puede.
        return await _local.AdvanceAsync(orderRef, stage, note, cancellationToken);
    }

    /// <summary>Si la etapa pedida ya está alcanzada (o es anterior a la actual).</summary>
    private static bool YaAlcanzada(OrderTimeline timeline, string stage)
    {
        var limpia = stage.Trim();
        var destino = timeline.Stages.FirstOrDefault(
            s => string.Equals(s.Stage, limpia, StringComparison.OrdinalIgnoreCase));

        return destino?.Reached == true;
    }

    /// <summary>
    /// Las etapas que hay que recorrer para llegar a la pedida, en orden.
    /// </summary>
    /// <remarks>
    /// <b>En secuencia y no de un salto.</b> El pedido pasó por cada una —el pipeline es
    /// secuencial— y declarar transiciones de salto perdería las intermedias en la historia de la
    /// capacidad, que es lo que un timeline necesita.
    /// </remarks>
    private IReadOnlyList<string> PorRecorrer(OrderTimeline? actual, string stage)
    {
        var limpia = stage.Trim();

        var indiceDestino = -1;
        for (var i = 0; i < _pipeline.Count; i++)
        {
            if (string.Equals(_pipeline[i], limpia, StringComparison.OrdinalIgnoreCase))
            {
                indiceDestino = i;
                break;
            }
        }

        // Que no esté en el pipeline lo rechaza el motor local con su propio mensaje; acá sólo
        // significa que no hay nada que recorrer.
        if (indiceDestino < 0) return Array.Empty<string>();

        var alcanzadas = (actual?.Stages ?? Array.Empty<OrderTimelineStage>())
            .Where(s => s.Reached)
            .Select(s => s.Stage)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return _pipeline
            .Take(indiceDestino + 1)
            .Where(e => !alcanzadas.Contains(e))
            .ToList();
    }

    /// <summary>Arranca el proceso si hace falta y dispara las transiciones que faltan.</summary>
    private async Task AvanzarEnLaCapacidadAsync(
        string orderRef,
        OrderTimeline? actual,
        IReadOnlyList<string> faltantes,
        string? note,
        CancellationToken ct)
    {
        var instancia = await EnRed(() => InstanciaAsync(orderRef, actual, ct));

        foreach (var etapa in faltantes)
        {
            // La primera etapa del pipeline es el estado INICIAL de la definición: arrancar la
            // instancia ya la deja ahí, así que no hay transición que disparar hacia ella.
            if (string.Equals(instancia.State, etapa, StringComparison.OrdinalIgnoreCase)) continue;

            // La nota SOLO en la etapa destino, igual que el motor local. Lo destapó una
            // verificación en vivo: sellándola en cada paso, la historia de la capacidad decía
            // «Entregado» también en «Enviado» — una nota que nadie escribió sobre ese paso.
            var esDestino = ReferenceEquals(etapa, faltantes[^1]) || etapa == faltantes[^1];
            instancia = await EnRed(() => DispararAsync(instancia, etapa, esDestino ? note : null, ct));
        }
    }

    /// <summary>
    /// La instancia del pedido, o una recién arrancada.
    /// </summary>
    /// <remarks>
    /// <para><b>Acá SÍ se puede adelantar un pedido en vuelo, al revés que en la HU #44</b>, y la
    /// diferencia importa. En Gobierno la historia de la capacidad <b>es</b> el registro legal, así
    /// que fabricarla habría escrito fechas y actores que no ocurrieron. Acá la capacidad es un
    /// <b>motor de reglas</b> y las fechas que importan siguen siendo las del CMS, intactas en su
    /// almacén: lo que se reconstruye es <i>dónde va</i> el pedido, no <i>cuándo pasó</i>.</para>
    ///
    /// <para>Si algún día la historia de la capacidad pasara a ser la fuente de las fechas, esta
    /// decisión hay que revisarla — por eso queda escrita y no supuesta.</para>
    /// </remarks>
    private async Task<InstanceDto> InstanciaAsync(string orderRef, OrderTimeline? actual, CancellationToken ct)
    {
        using var http = Client();

        var url = $"v1/instances?subjectKind={Uri.EscapeDataString(SubjectKind)}"
                  + $"&subjectId={Uri.EscapeDataString(orderRef.Trim())}";

        using var buscar = await http.GetAsync(url, ct);
        await GritarSiFalla(buscar, ct);

        var pagina = await buscar.Content.ReadFromJsonAsync<PageDto<InstanceDto>>(Json, ct);
        if (pagina?.Items is { Count: > 0 } encontradas)
        {
            return encontradas[0];
        }

        using var peticion = new HttpRequestMessage(HttpMethod.Post, "v1/instances")
        {
            Content = JsonContent.Create(new StartDto(DefinitionKey, SubjectKind, orderRef.Trim()), options: Json),
        };
        // La llave es el pedido: un reintento tras un timeout encuentra la instancia que él mismo
        // creó en vez de abrir una segunda sobre el mismo pedido.
        peticion.Headers.TryAddWithoutValidation("Idempotency-Key", $"track-{_domain}-{orderRef.Trim()}");

        using var creada = await http.SendAsync(peticion, ct);
        await GritarSiFalla(creada, ct);

        var instancia = await creada.Content.ReadFromJsonAsync<InstanceDto>(Json, ct)
            ?? throw new InvalidOperationException("Api.Workflow no devolvió la instancia recién creada.");

        // Un pedido que ya venía en vuelo: se lo pone al día hasta donde el CMS dice que está.
        // Ver el comentario del método — acá reconstruir el DÓNDE no miente sobre el CUÁNDO.
        foreach (var alcanzada in (actual?.Stages ?? Array.Empty<OrderTimelineStage>()).Where(s => s.Reached))
        {
            if (string.Equals(instancia.State, alcanzada.Stage, StringComparison.OrdinalIgnoreCase)) continue;
            instancia = await DispararAsync(instancia, alcanzada.Stage, "puesta al día del cableado", ct);
        }

        return instancia;
    }

    private async Task<InstanceDto> DispararAsync(
        InstanceDto instancia, string etapa, string? note, CancellationToken ct)
    {
        using var http = Client();

        // La transición se llama como la etapa destino. Un nombre propio («despachar») obligaría a
        // una segunda tabla de este lado para traducirlo, que es lo que este cableado quita.
        // Quién avanza: el sistema, no una persona. El Kind NO es el del sujeto — un pedido no
        // es el actor de su propio avance, y confundirlos deja la historia diciendo que
        // «tracking.shop» despachó algo. El día que la HU #14 llegue acá, esto pasa a salir del
        // token en vez de ser una constante; hoy los roles van vacíos porque no hay nada que
        // probar y fingirlos sería peor.
        var cuerpo = new FireDto(etapa, ActorKind, _domain, Array.Empty<string>(),
            string.IsNullOrWhiteSpace(note) ? null : note.Trim());

        using var peticion = new HttpRequestMessage(HttpMethod.Post, $"v1/instances/{instancia.Id}/fire")
        {
            Content = JsonContent.Create(cuerpo, options: Json),
        };
        peticion.Headers.TryAddWithoutValidation(
            "Idempotency-Key", $"track-{instancia.Id}-{instancia.State}-{etapa}");

        using var respuesta = await http.SendAsync(peticion, ct);

        if (respuesta.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.Forbidden
            or HttpStatusCode.BadRequest)
        {
            // Lo que la capacidad rechaza por regla se cuenta con el vocabulario que el seam ya
            // tiene: una etapa que no sigue a la actual es un argumento malo, no una caída.
            throw new ArgumentException(await MotivoAsync(respuesta, ct), nameof(etapa));
        }

        await GritarSiFalla(respuesta, ct);

        return await respuesta.Content.ReadFromJsonAsync<InstanceDto>(Json, ct)
            ?? throw new InvalidOperationException("Api.Workflow no devolvió la instancia tras la transición.");
    }

    private HttpClient Client()
    {
        var http = _factory.CreateClient(ClientName);
        if (http.BaseAddress is null)
        {
            http.BaseAddress = new Uri(_settings.BaseUrl.TrimEnd('/') + "/");
        }
        if (!http.DefaultRequestHeaders.Contains(ApiKeyHeader) && !string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            http.DefaultRequestHeaders.TryAddWithoutValidation(ApiKeyHeader, _settings.ApiKey);
        }
        http.Timeout = TimeSpan.FromSeconds(Math.Max(1, _settings.TimeoutSeconds));
        return http;
    }

    /// <summary>Convierte «no se pudo hablar con la capacidad» en el idioma del seam.</summary>
    /// <remarks>
    /// La lección de la HU #44, aplicada de entrada: una <see cref="HttpRequestException"/> cruda
    /// no dice nada al borde y arrastra la dirección interna al mensaje.
    /// </remarks>
    private static async Task<T> EnRed<T>(Func<Task<T>> llamada)
    {
        try
        {
            return await llamada();
        }
        catch (HttpRequestException)
        {
            throw new InvalidOperationException(
                "No se pudo validar el avance del pedido: Api.Workflow no responde. "
                + "El pedido NO avanzó; se puede reintentar. Consultar el estado sigue funcionando.");
        }
        catch (TaskCanceledException)
        {
            throw new InvalidOperationException(
                "Api.Workflow tardó demasiado en contestar. El pedido NO avanzó; se puede reintentar.");
        }
    }

    private static async Task GritarSiFalla(HttpResponseMessage respuesta, CancellationToken ct)
    {
        if (respuesta.IsSuccessStatusCode) return;

        throw new InvalidOperationException(
            $"Api.Workflow no pudo atender el avance ({(int)respuesta.StatusCode}): "
            + await MotivoAsync(respuesta, ct));
    }

    private static async Task<string> MotivoAsync(HttpResponseMessage respuesta, CancellationToken ct)
    {
        try
        {
            var problema = await respuesta.Content.ReadFromJsonAsync<ProblemDto>(Json, ct);
            if (!string.IsNullOrWhiteSpace(problema?.Detail)) return problema!.Detail!;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or HttpRequestException)
        {
            // Un cuerpo que no es un problema tampoco es un motivo.
        }

        return $"la capacidad respondió {(int)respuesta.StatusCode} sin motivo legible.";
    }

    // ── Lo que viaja ────────────────────────────────────────────────────────

    private sealed record InstanceDto(string Id, string DefinitionKey, string State);

    private sealed record PageDto<T>(IReadOnlyList<T> Items, int Total, int Offset, bool HasMore);

    private sealed record StartDto(string DefinitionKey, string SubjectKind, string SubjectId);

    private sealed record FireDto(
        string Transition, string ActorKind, string ActorId, IReadOnlyList<string> ActorRoles, string? Note);

    private sealed record ProblemDto(string? Title, string? Detail, string? Code);
}
