using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// El <see cref="ICaseWorkflowService"/> que resuelve la legalidad contra
/// <c>Synergos.Api.Workflow</c> (HU #44).
/// </summary>
/// <remarks>
/// <para><b>La tabla de transiciones deja de estar en C#.</b> Hasta acá, qué podía pasarle a un
/// expediente estaba escrito en el CMS y desplegado con el sitio: añadir un paso de revisión era
/// un cambio de código. La capacidad las tiene como DATO, así que el proceso de un trámite cambia
/// publicando una definición.</para>
///
/// <para><b>Contra la CAPACIDAD, no contra un orquestador</b> — como la visita al inmueble
/// (#33a) y al revés que la compra de una entrada (#35). Decidir es UN paso y no hay plata en
/// medio: no queda nada a medias que deshacer si algo falla. Hay gate: <c>GobWiringTests</c>.
/// (Por la puerta, el trámite va al orquestador de Gobierno: la regla de forma de la ADR 0140,
/// 009-P1. Esta ruta directa se conserva detrás de su interruptor.)</para>
///
/// <para><b>El expediente se queda de este lado</b>, igual que la entrada de un evento se queda
/// en el CMS aunque el aforo lo lleve <c>Api.Inventory</c>. La capacidad es dueña de «qué se
/// puede hacer y desde dónde»; el radicado, el ciudadano, los documentos y la timeline son del
/// CMS. Por eso este cliente hace dos cosas: preguntar si la transición es legal y
/// <b>anotar de su lado lo que la capacidad no lleva</b> (<see cref="GovCaseDecisionRecorder"/>).</para>
///
/// <para><b>El estado destino se lee de la DEFINICIÓN DE LA INSTANCIA, no de una tabla local ni de
/// los ajustes</b> (009, FR-008). Un expediente sigue toda su vida con la definición con que nació:
/// uno de la v1 se decide con las reglas de la v1 aunque los nuevos arranquen con la v2. Leer la de
/// los ajustes haría que un expediente en marcha cambiara de reglas a mitad de camino.</para>
///
/// <para><b>Y cada definición se cachea para siempre, lo cual es demostrable y no una apuesta.</b>
/// La capacidad se NIEGA a reescribir una definición viva (<c>workflow.key_taken</c>): cambiarle
/// las transiciones a instancias en marcha las dejaría en estados imposibles. Versionar es
/// publicar otra clave, y la de los expedientes nuevos es configuración de este lado
/// (<see cref="GobSettings.DefinitionKey"/>). Así que una definición leída no puede cambiar bajo
/// nuestros pies mientras la clave sea la misma.</para>
///
/// <para><b>Con la capacidad caída NO se cae a la tabla local.</b> Se falla con el motivo puesto.
/// Caer al stub en silencio convertiría una caída en decisiones tomadas con un proceso que quizá
/// ya no es el vigente, y nadie se enteraría — el mismo criterio que la HU #27 aplicó a los
/// cobros.</para>
/// </remarks>
public sealed class HttpCaseWorkflowService : ICaseWorkflowService, IDisposable
{
    /// <summary>Cliente nombrado que registra el composer.</summary>
    public const string ClientName = "synergos-api-workflow";

    /// <summary>El vocabulario con el que este vertical nombra a quien decide.</summary>
    public const string ActorKind = CaseRoles.KindFuncionario;

    /// <summary>
    /// Los roles del funcionario de ventanilla.
    /// </summary>
    /// <remarks>
    /// <b>Están en UN sitio porque ahora se usan para dos cosas</b>: viajan en el cuerpo —el
    /// camino degradado— y se registran en <c>Api.Identity</c> al dar de alta la identidad, que
    /// es de donde salen firmados. Con dos listas, un despliegue acabaría presentando un token
    /// con un rol y declarando otro, y el mismo funcionario decidiría distinto según qué mire la
    /// capacidad.
    /// </remarks>
    public static readonly IReadOnlyList<string> OfficerRoles = new[] { "funcionario" };

    /// <summary>Los roles del ciudadano que actúa sobre su expediente (subsanar), con la misma regla.</summary>
    public static readonly IReadOnlyList<string> CitizenRoles = new[] { "ciudadano" };

    /// <summary>Las acciones que <see cref="DecideAsync"/> admite: las decisiones del funcionario.</summary>
    private static readonly string[] Desenlaces = { "approve", "reject", "request-info" };

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IHttpClientFactory _factory;
    private readonly GovCaseDecisionRecorder _recorder;
    private readonly GobSettings _settings;
    private readonly IIdentityTokenIssuer _identidad;
    private readonly Func<DateTimeOffset> _now;
    private readonly SemaphoreSlim _defGate = new(1, 1);
    private readonly ConcurrentDictionary<string, DefinitionDto> _definiciones = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public void Dispose() => _defGate.Dispose();

    public HttpCaseWorkflowService(
        IHttpClientFactory factory,
        StubApplicationService cases,
        IOptions<GobSettings> settings,
        IAuditTrailWriter? audit = null,
        ITransactionalNotifier? notifier = null,
        Func<DateTimeOffset>? now = null,
        IIdentityTokenIssuer? identity = null)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));
        // Sin emisor se declara quién actúa, que es lo que se hacía antes de la HU #14. Va al
        // final y con default para no re-ligar los args posicionales de este ctor otra vez.
        _identidad = identity ?? new StubIdentityTokenIssuer();
        _recorder = new GovCaseDecisionRecorder(cases, audit, notifier);
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    public Task<CaseDetail> DecideAsync(
        string caseId,
        string outcome,
        string note,
        CaseActor actor,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(outcome)
            || !Desenlaces.Contains(outcome.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "outcome es requerido: approve | reject | request-info.", nameof(outcome));
        }

        return TransitarAsync(caseId, outcome.Trim(), note, actor, OfficerRoles, cancellationToken);
    }

    public Task<CaseDetail> TakeAsync(
        string caseId,
        CaseActor actor,
        string? note,
        CancellationToken cancellationToken = default)
        => TransitarAsync(caseId, "take", note ?? string.Empty, actor, OfficerRoles, cancellationToken);

    /// <summary>
    /// El ciudadano subsana: dispara <c>resubmit</c> con SU identidad y el rol de su cara, y la guarda
    /// de la capacidad rechaza a cualquier otra (FR-006).
    /// </summary>
    public Task<CaseDetail> ResubmitAsync(
        string caseId,
        CaseActor actor,
        string? note,
        CancellationToken cancellationToken = default)
        => TransitarAsync(caseId, "resubmit", note ?? string.Empty, actor, CitizenRoles, cancellationToken);

    /// <summary>
    /// Lo que la definición DE LA INSTANCIA permite desde el estado de la instancia a esa cara.
    /// </summary>
    /// <remarks>
    /// <para><b>El estado es el de la instancia, no el de la proyección.</b> Es contra ella que la
    /// capacidad juzgará la próxima transición: si las dos difirieran, ofrecer desde la proyección
    /// pondría un botón que la capacidad rechazaría.</para>
    /// <para><b>Sin instancia</b>, sólo el expediente que sigue en el estado inicial tiene acciones: las
    /// de la definición de los nuevos, con la que se arrancaría al actuar. Uno ya movido no tiene
    /// ninguna hasta migrarlo, que es lo mismo que contestaría al intentarlo.</para>
    /// </remarks>
    public async Task<IReadOnlyList<string>> ActionsAsync(
        string caseId,
        string role,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(caseId))
        {
            throw new ArgumentException("El expediente es obligatorio.", nameof(caseId));
        }

        var current = _recorder.Find(caseId)
            ?? throw new ArgumentException($"Expediente '{caseId.Trim()}' no encontrado.", nameof(caseId));

        var instancia = await EnRed(() => BuscarInstanciaAsync(current, cancellationToken), cancellationToken);
        var definicion = await EnRed(
            () => DefinitionAsync(instancia?.DefinitionKey ?? _settings.DefinitionKey, cancellationToken), cancellationToken);

        var estado = instancia?.State;
        if (estado is null)
        {
            if (!GovStatusSlugs.TryParse(definicion.InitialState, out var inicial) || current.Status != inicial)
            {
                return Array.Empty<string>();
            }
            estado = definicion.InitialState;
        }

        return AccionesDelExpediente.Calcular(
            definicion.Transitions.Select(t => new AccionesDelExpediente.Paso(
                t.Name, t.From, t.To, t.RequiredRoles ?? Array.Empty<string>())),
            definicion.FinalStates ?? Array.Empty<string>(),
            estado,
            role);
    }

    private async Task<CaseDetail> TransitarAsync(
        string caseId,
        string accion,
        string note,
        CaseActor actor,
        IReadOnlyList<string> roles,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(caseId))
        {
            throw new ArgumentException("El expediente es obligatorio.", nameof(caseId));
        }
        ArgumentNullException.ThrowIfNull(actor);

        var current = _recorder.Find(caseId)
            ?? throw new ArgumentException($"Expediente '{caseId.Trim()}' no encontrado.", nameof(caseId));

        // La instancia ANTES que la definición: su definición es la que manda (FR-008). Sin
        // instancia, el expediente todavía no tiene proceso y arrancaría con el de los ajustes.
        var instancia = await EnRed(() => BuscarInstanciaAsync(current, cancellationToken), cancellationToken);
        var definicion = await EnRed(
            () => DefinitionAsync(instancia?.DefinitionKey ?? _settings.DefinitionKey, cancellationToken), cancellationToken);

        // A dónde lleva esta acción, según la DEFINICIÓN: la transición desde el estado actual o,
        // si desde ahí no existe, cualquiera que se llame así (para reconocer la idempotencia).
        // Si la definición no la nombra, la acción no existe en este proceso — y eso es un error
        // de quien llama, no del expediente.
        var desde = GovStatusSlugs.ToSlug(current.Status);
        var destino = (definicion.Transitions.FirstOrDefault(t => Igual(t.Name, accion) && Igual(t.From, desde))
                       ?? definicion.Transitions.FirstOrDefault(t => Igual(t.Name, accion)))?.To;

        if (destino is null || !GovStatusSlugs.TryParse(destino, out var to))
        {
            var posibles = definicion.Transitions.Select(t => t.Name).Distinct(StringComparer.OrdinalIgnoreCase);
            throw new ArgumentException(
                $"'{accion}' no existe en el proceso '{definicion.Key}'. Sí existen: {string.Join(", ", posibles)}.",
                nameof(accion));
        }

        // Idempotente sobre el estado destino, IGUAL que el motor en proceso — y por eso se
        // resuelve ACÁ y no se le pregunta a la capacidad. Ella contestaría otra cosa a lo mismo
        // (`instance_closed`, que es un conflicto), porque responde «¿es legal esta transición?»
        // y no «¿hace falta hacer algo?». Las dos son correctas; dejar subir la suya convertiría
        // el doble clic del funcionario, que hoy no hace nada, en un error en pantalla.
        if (current.Status == to)
        {
            await _recorder.NotifyAsync(current, to, _now(), cancellationToken);
            return current;
        }

        instancia ??= await EnRed(() => ArrancarAsync(current, definicion, cancellationToken), cancellationToken);
        var resultado = await EnRed(
            () => FireAsync(instancia, accion, note, actor, roles, cancellationToken), cancellationToken);

        // El estado lo dice la capacidad, no lo suponemos: si la definición llevara a otro sitio
        // del que creíamos, mandar lo nuestro dejaría al expediente y a la instancia diciendo
        // cosas distintas — y la próxima decisión se tomaría sobre una realidad que no es.
        if (!GovStatusSlugs.TryParse(resultado.State, out var real))
        {
            throw new InvalidOperationException(
                $"El proceso dejó el expediente en '{resultado.State}', que este vertical no sabe representar. "
                + "Revisar la definición publicada en Api.Workflow.");
        }

        return await _recorder.RecordAsync(current, accion, real, note, _now(), actor, cancellationToken);
    }

    private static bool Igual(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>La definición con esa clave, leída una vez por clave.</summary>
    private async Task<DefinitionDto> DefinitionAsync(string clave, CancellationToken ct)
    {
        if (_definiciones.TryGetValue(clave, out var leida)) return leida;

        await _defGate.WaitAsync(ct);
        try
        {
            if (_definiciones.TryGetValue(clave, out leida)) return leida;

            using var http = _factory.CreateClient(ClientName);
            using var respuesta = await http.GetAsync($"v1/definitions/{Uri.EscapeDataString(clave)}", ct);

            if (respuesta.StatusCode == HttpStatusCode.NotFound)
            {
                // Paso de DESPLIEGUE que no es código, y por eso el mensaje lo dice: sin la
                // definición publicada no hay proceso que seguir, y adivinar uno sería volver a
                // tener la tabla acá.
                throw new InvalidOperationException(
                    $"No hay definición '{clave}' en Api.Workflow. Hay que publicarla "
                    + "(POST /v1/definitions) antes de decidir expedientes: es un paso de despliegue.");
            }

            await GritarSiFalla(respuesta, ct);

            var definicion = await respuesta.Content.ReadFromJsonAsync<DefinitionDto>(Json, ct)
                ?? throw new InvalidOperationException("Api.Workflow devolvió una definición vacía.");

            _definiciones[clave] = definicion;
            return definicion;
        }
        finally
        {
            _defGate.Release();
        }
    }

    /// <summary>La instancia del expediente, o null si todavía no tiene proceso.</summary>
    private async Task<InstanceDto?> BuscarInstanciaAsync(CaseDetail @case, CancellationToken ct)
    {
        using var http = _factory.CreateClient(ClientName);

        var url = $"v1/instances?subjectKind={Uri.EscapeDataString(_settings.CaseKind)}"
                  + $"&subjectId={Uri.EscapeDataString(@case.CaseId)}";

        using var buscar = await http.GetAsync(url, ct);
        await GritarSiFalla(buscar, ct);

        var pagina = await buscar.Content.ReadFromJsonAsync<PageDto<InstanceDto>>(Json, ct);
        return pagina?.Items is { Count: > 0 } encontradas ? encontradas[0] : null;
    }

    /// <summary>
    /// Le arranca su proceso a un expediente que todavía no tiene, con la definición de los expedientes nuevos.
    /// </summary>
    /// <remarks>
    /// <para><b>Arrancarla acá sólo es honesto si el expediente NO se ha movido.</b> Un expediente
    /// recién radicado no tiene historia que inventar: <c>Start</c> lo deja en el estado inicial,
    /// que es donde de verdad está. Uno que ya pasó por revisión, no — arrancarle una instancia
    /// ahora la pondría en el inicial y la capacidad diría que un expediente casi resuelto acaba
    /// de empezar.</para>
    ///
    /// <para><b>Por eso los expedientes anteriores al cableado se rechazan de frente</b> en vez de
    /// tratarse en silencio. Adelantarlos a golpe de transiciones escribiría historia falsa —
    /// fechas y actores que no ocurrieron— y parecería que funciona, que es lo peor que puede
    /// hacer una migración.</para>
    /// </remarks>
    private async Task<InstanceDto> ArrancarAsync(CaseDetail @case, DefinitionDto definicion, CancellationToken ct)
    {
        if (!GovStatusSlugs.TryParse(definicion.InitialState, out var inicial) || @case.Status != inicial)
        {
            throw new InvalidOperationException(
                $"El expediente {@case.Radicado} está en '{GovStatusSlugs.ToSlug(@case.Status)}' y no tiene "
                + "proceso en Api.Workflow. Arrancarle uno ahora lo pondría en el estado inicial y diría "
                + "que acaba de empezar. Los expedientes anteriores al cableado hay que migrarlos, no adivinarlos.");
        }

        using var http = _factory.CreateClient(ClientName);

        // La llave es el expediente: si la petición se pierde en el aire, el reintento encuentra
        // la instancia que él mismo creó en vez de abrir una segunda sobre el mismo expediente.
        using var peticion = new HttpRequestMessage(HttpMethod.Post, "v1/instances")
        {
            Content = JsonContent.Create(
                new StartDto(definicion.Key, _settings.CaseKind, @case.CaseId), options: Json),
        };
        peticion.Headers.TryAddWithoutValidation("Idempotency-Key", $"gov-case-{@case.CaseId}");

        using var creada = await http.SendAsync(peticion, ct);
        await GritarSiFalla(creada, ct);

        return await creada.Content.ReadFromJsonAsync<InstanceDto>(Json, ct)
            ?? throw new InvalidOperationException("Api.Workflow no devolvió la instancia recién creada.");
    }

    /// <summary>Dispara la transición y devuelve la instancia como quedó.</summary>
    /// <param name="actor">Quien actúa: la persona de la sesión, con su cara y su identidad (D-5 de 009).</param>
    /// <param name="roles">Los roles de esa cara, de la lista única de este cliente.</param>
    private async Task<InstanceDto> FireAsync(
        InstanceDto instancia, string transicion, string note, CaseActor actor, IReadOnlyList<string> roles,
        CancellationToken ct)
    {
        using var http = _factory.CreateClient(ClientName);

        var cuerpo = new FireDto(
            transicion,
            actor.Kind,
            actor.Id,
            roles,
            string.IsNullOrWhiteSpace(note) ? null : note.Trim());

        using var peticion = new HttpRequestMessage(HttpMethod.Post, $"v1/instances/{instancia.Id}/fire")
        {
            Content = JsonContent.Create(cuerpo, options: Json),
        };

        // La llave lleva el estado de origen: reintentar el MISMO paso no lo da dos veces, y en
        // cambio un paso distinto más tarde sí es otro paso.
        peticion.Headers.TryAddWithoutValidation(
            "Idempotency-Key", $"gov-fire-{instancia.Id}-{instancia.State}-{transicion}");

        // La identidad de quien actúa, si se puede conseguir (HU #14). Los roles del cuerpo
        // SIGUEN yendo y no es redundancia: con token, la capacidad los ignora y usa los del
        // token (#48); sin token —clon limpio, o Api.Identity caída— son el único dato que hay,
        // y un trámite no se para porque la identidad esté caída. Cuál de los dos manda lo
        // decide el despliegue del otro lado, con Workflow:Roles:RequireVerifiedRoles.
        var token = await _identidad.IssueAsync(
            new IdentitySubject(actor.Kind, actor.Id, roles), ct)
            .ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(token))
        {
            peticion.Headers.TryAddWithoutValidation("X-Synergos-Identity", token);
        }

        using var respuesta = await http.SendAsync(peticion, ct);

        // Lo que la capacidad rechaza por REGLA sube como decisión ilegal, que es lo que la cara
        // de funcionario ya sabe mostrar — y ahora con la lista de las que sí se pueden, que la
        // tabla local nunca dio.
        if (respuesta.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.Forbidden
            or HttpStatusCode.BadRequest)
        {
            throw new InvalidOperationException(await MotivoAsync(respuesta, ct));
        }

        await GritarSiFalla(respuesta, ct);

        return await respuesta.Content.ReadFromJsonAsync<InstanceDto>(Json, ct)
            ?? throw new InvalidOperationException("Api.Workflow no devolvió la instancia tras la transición.");
    }

    /// <summary>
    /// Convierte «no se pudo hablar con la capacidad» en el mismo idioma que el resto.
    /// </summary>
    /// <remarks>
    /// <para><b>Lo destapó levantar los procesos, no un test.</b> Con <c>Api.Workflow</c> caída, la
    /// <see cref="HttpRequestException"/> subía cruda hasta el borde, que sólo traduce
    /// <see cref="ArgumentException"/> e <see cref="InvalidOperationException"/> — así que una
    /// capacidad apagada daba un 500 sin explicación en vez de degradar diciendo qué pasa. Y el
    /// mensaje traía la dirección interna («Connection refused (127.0.0.1:5215)»), que no es
    /// asunto de quien usa el portal.</para>
    ///
    /// <para>El expediente <b>no</b> se mueve en ese camino, que era lo importante y ya se cumplía.
    /// Esto es que además se note.</para>
    /// </remarks>
    private static async Task<T> EnRed<T>(Func<Task<T>> llamada, CancellationToken ct)
    {
        try
        {
            return await llamada();
        }
        catch (HttpRequestException)
        {
            throw new InvalidOperationException(
                "No se pudo consultar el proceso del expediente: Api.Workflow no responde. "
                + "La decisión NO se aplicó; se puede reintentar.");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            // Un timeout no dice «no se decidió» — dice «no sé». Pero acá sí se sabe: la decisión
            // se aplica de este lado DESPUÉS de que la capacidad la acepta, así que si no llegó
            // respuesta, el expediente está intacto.
            throw new InvalidOperationException(
                "Api.Workflow tardó demasiado en contestar. La decisión NO se aplicó; se puede reintentar.");
        }
    }

    private static async Task GritarSiFalla(HttpResponseMessage respuesta, CancellationToken ct)
    {
        if (respuesta.IsSuccessStatusCode) return;

        throw new InvalidOperationException(
            $"Api.Workflow no pudo atender la decisión ({(int)respuesta.StatusCode}): "
            + await MotivoAsync(respuesta, ct));
    }

    /// <summary>El motivo que puso la capacidad, o el código si no vino ninguno.</summary>
    private static async Task<string> MotivoAsync(HttpResponseMessage respuesta, CancellationToken ct)
    {
        // Un cuerpo que no es un problema tampoco es un motivo: cae al código de abajo. Lo lee el
        // lector compartido (#129), no una copia privada (#178).
        var problema = await RechazoDelArbolDeServicios.LeerAsync(respuesta, Json, ct).ConfigureAwait(false);

        return !string.IsNullOrWhiteSpace(problema?.Detalle)
            ? problema!.Detalle!
            : $"la capacidad respondió {(int)respuesta.StatusCode} sin motivo legible.";
    }

    // ── Lo que viaja ────────────────────────────────────────────────────────

    private sealed record TransitionDto(string Name, string From, string To, IReadOnlyList<string>? RequiredRoles = null);

    private sealed record DefinitionDto(
        string Key, string InitialState, IReadOnlyList<string> FinalStates, IReadOnlyList<TransitionDto> Transitions);

    private sealed record InstanceDto(string Id, string DefinitionKey, string State);

    private sealed record PageDto<T>(IReadOnlyList<T> Items, int Total, int Offset, bool HasMore);

    private sealed record StartDto(string DefinitionKey, string SubjectKind, string SubjectId);

    private sealed record FireDto(
        string Transition, string ActorKind, string ActorId, IReadOnlyList<string> ActorRoles, string? Note);
}
