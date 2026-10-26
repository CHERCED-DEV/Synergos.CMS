using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Synergos.CMS.Application.Dinero;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services.Gobierno;
using Synergos.CMS.Web.Services.Puerta;

namespace Synergos.CMS.Web.Controllers;

/// <summary>
/// API JSON del vertical <strong>Gobierno / Trámites</strong> (OLA 8 — portal de
/// servicios al ciudadano GOV.CO/GOV.UK, doc gobierno.md). La consume el módulo Angular
/// <c>module-gov-services</c> con normalizers ESTRICTOS: entrar al dominio = caer directo
/// en la app real, con DOS caras — ciudadano (catálogo → ficha → formulario → radicar →
/// carpeta/seguimiento) y funcionario (cola → expediente → decisión con máquina de
/// estados auditada).
/// </summary>
/// <remarks>
/// La capa Web SOLO orquesta y mapea a DTOs JSON estables (claves camelCase — contrato
/// estricto de la UI) — toda la lógica vive en los seams (Application, sin Umbraco —
/// ADR 0002), reusando el MOTOR:
/// <list type="bullet">
/// <item><see cref="ITramiteCatalogProvider"/> — catálogo buscable + ficha con pasos +
///   elegibilidad + requisitos + formulario dinámico seccionado (patrón GOV.UK).</item>
/// <item><see cref="IApplicationService"/> — radicar = crear el expediente; la tasa es
///   un pago OPCIONAL (<see cref="IPaymentProvider"/> solo si Fee &gt; 0).</item>
/// <item><see cref="ICaseWorkflowService"/> — el funcionario toma y decide (take/approve/reject/
///   request-info) → máquina de estados auditada (<see cref="IAuditTrailWriter"/>), y las
///   acciones que el proceso permite a cada cara (<c>actions</c>, 009).</item>
/// <item><see cref="ICaseTrackingProvider"/> — carpeta del ciudadano + cola del
///   funcionario (prioridad + SLA).</item>
/// <item><see cref="IDocumentUploadService"/> — adjuntar documentos (metadata).</item>
/// <item><see cref="IMessagingService"/> (contexto <c>gov</c>) — correspondencia del
///   expediente con la entidad.</item>
/// </list>
/// </remarks>
[ApiController]
[Route("api/gov")]
public sealed class GovController : ControllerBase
{
    private const string OfficerParticipant = GovCorrespondenceSeeder.OfficerParticipant;
    private const string MessagingContext = GovCorrespondenceSeeder.Context;

    /// <summary>Carpeta lógica del almacén privado donde viven los adjuntos de Gobierno.</summary>
    private const string GovDocumentScope = "gov-documents";

    /// <summary>
    /// Tope de subida (10 MB). Se declara como const para poder usarlo en
    /// <see cref="RequestSizeLimitAttribute"/>, que exige constante en tiempo de
    /// compilación — por eso no sale de la configuración.
    /// </summary>
    private const long MaxUploadBytes = 10 * 1024 * 1024;

    /// <summary>
    /// Tipos aceptados. Es un allowlist a propósito (no una lista de prohibidos): lo que
    /// no está, no entra. Coincide con el <c>accept</c> que ofrece la UI.
    /// </summary>
    private static readonly HashSet<string> AllowedUploadContentTypes = new(StringComparer.Ordinal)
    {
        "application/pdf",
        "image/jpeg",
        "image/png",
    };

    private readonly ITramiteCatalogProvider _catalog;
    private readonly IApplicationService _applications;
    private readonly ICaseWorkflowService _workflow;
    private readonly ICaseTrackingProvider _tracking;
    private readonly IDocumentUploadService _documents;
    private readonly IPrivateFileStore _files;
    private readonly IMessagingService _messaging;
    private readonly IMemberAccessGate _gate;
    private readonly IGovActNotificationService _notifications;
    private readonly IAuditTrailWriter _audit;
    private readonly ArtefactoDelExpediente? _artefacto;

    /// <param name="artefacto">Lo que el CMS guarda de una radicación por la puerta (009, T027). Nulo en las pruebas
    /// que no la usan: la ruta contesta que la radicación por la puerta no está abierta.</param>
    public GovController(
        ITramiteCatalogProvider catalog,
        IApplicationService applications,
        ICaseWorkflowService workflow,
        ICaseTrackingProvider tracking,
        IDocumentUploadService documents,
        IPrivateFileStore files,
        IMessagingService messaging,
        IMemberAccessGate gate,
        IGovActNotificationService notifications,
        IAuditTrailWriter audit,
        ArtefactoDelExpediente? artefacto = null)
    {
        _catalog = catalog;
        _applications = applications;
        _workflow = workflow;
        _tracking = tracking;
        _documents = documents;
        _files = files;
        _messaging = messaging;
        _gate = gate;
        _notifications = notifications;
        _audit = audit;
        _artefacto = artefacto;
    }

    /// <summary>
    /// Se queda solo con el nombre del fichero que mandó el navegador, sin ruta. Un
    /// cliente puede enviar <c>../../algo.pdf</c> en el multipart; el nombre se guarda y
    /// se devuelve al descargar, así que nunca debe llevar separadores.
    /// </summary>
    private static string SafeFileName(string? rawName)
    {
        var name = Path.GetFileName((rawName ?? string.Empty).Trim());
        return string.IsNullOrWhiteSpace(name) ? "documento" : name;
    }

    // ── T2 Gobierno (ADR 0103, calcado de ShopCatalogController) ────────────
    // La identidad del ciudadano sale del GATE (cookie de sesión), NUNCA del body ni del
    // query. Antes salía del formulario: cualquiera radicaba a nombre de otro poniendo su
    // correo, y cualquiera listaba los expedientes ajenos con ?citizen=<email>.

    /// <summary>
    /// Exige un Member autenticado. Devuelve (401, default) si es anónimo, o (null, actorKey)
    /// con la key server-trusted si hay sesión. Molde de <c>ShopCatalogController</c>.
    /// </summary>
    private (IActionResult? denied, Guid actorKey) RequireMember()
    {
        if (!_gate.IsAuthenticated || _gate.CurrentMemberKey is not Guid actorKey)
        {
            return (Unauthorized(new { error = "Se requiere iniciar sesión." }), default);
        }
        return (null, actorKey);
    }

    /// <summary>
    /// Guard de ownership del expediente.
    /// </summary>
    /// <remarks>
    /// <b>Aquí NO hay excepción de invitado, al revés que en Tienda</b>, y la diferencia es
    /// el id: el <c>orderRef</c> es <c>ord_{guid:N}</c> —inadivinable— así que funciona como
    /// credencial bearer y el autoservicio de invitado es seguro. El radicado es
    /// <c>SG-2026-000001</c>: <b>SECUENCIAL</b>. Sin dueño, o el expediente es de todos (se
    /// enumera contando) o de nadie. Se elige de nadie.
    ///
    /// <para><c>StatusCode(403)</c> y NO <c>Forbid()</c>: con auth de members
    /// <c>Forbid()</c> REDIRIGE al login en vez de denegar.</para>
    /// </remarks>
    private IActionResult? DenyIfForeignCase(CaseDetail detail, Guid actorKey)
    {
        if (detail.Citizen.MemberKey == actorKey || _gate.HasAnyRole("admin"))
        {
            return null;
        }
        return StatusCode(StatusCodes.Status403Forbidden, new { error = "El expediente no es suyo." });
    }

    /// <summary>
    /// Rol(es) que dan acceso a la consola del funcionario. <c>funcionario</c> es el rol de
    /// dominio (member group de la entidad, patrón <c>doctor,nurse,reception</c> de Healthcare);
    /// <c>admin</c> entra como superusuario, igual que en el override de ownership.
    /// </summary>
    private const string OfficerRolesCsv = "funcionario,admin";

    /// <summary>
    /// Exige un funcionario para la cola/expediente/decisión. 401 si es anónimo, 403 si está
    /// autenticado pero SIN el rol. Molde de <c>DashboardApiController.Deny</c>.
    /// </summary>
    /// <remarks>
    /// Estas 3 rutas exponen PII de OTROS ciudadanos (cédula, correo, teléfono en las
    /// respuestas del formulario) y permiten decidir expedientes — al revés que la carpeta del
    /// ciudadano, aquí NO basta con estar logueado: un ciudadano cualquiera no es funcionario.
    /// <para><c>StatusCode(403)</c> y NO <c>Forbid()</c>: con auth de members <c>Forbid()</c>
    /// REDIRIGE al login en vez de denegar (misma razón que <see cref="DenyIfForeignCase"/>).</para>
    /// </remarks>
    private IActionResult? RequireOfficer()
    {
        if (!_gate.IsAuthenticated)
        {
            return Unauthorized(new { error = "Inicie sesión como funcionario." });
        }
        if (!_gate.HasAnyRole(OfficerRolesCsv))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Su cuenta no tiene permiso de funcionario." });
        }
        return null;
    }

    /// <summary>
    /// La persona de la sesión, como actor de una acción sobre el expediente (D-5 de 009).
    /// </summary>
    /// <remarks>
    /// <para><b>Sale del GATE, nunca del cuerpo</b>, como el dueño del expediente: quien firma una
    /// decisión es quien está autenticado, no quien diga la petición. Antes toda decisión la firmaba
    /// un actor de demo fijo y el control interno no podía saber quién decidió.</para>
    /// <para>La identidad es la clave del miembro; el nombre y el correo, los de su cuenta.</para>
    /// </remarks>
    private CaseActor ActorDeLaSesion(string kind)
    {
        var correo = _gate.CurrentMemberEmail?.Trim() ?? string.Empty;
        var id = _gate.CurrentMemberKey is Guid clave && clave != Guid.Empty
            ? clave.ToString("N")
            : (string.IsNullOrWhiteSpace(correo) ? "sin-identidad" : correo);
        var nombre = _gate.CurrentMemberDisplayName?.Trim();
        return new CaseActor(kind, id, string.IsNullOrWhiteSpace(nombre) ? correo : nombre, correo);
    }

    // ══════════════════════ CIUDADANO ══════════════════════

    // ── 1. Catálogo de trámites ─────────────────────────────────────────
    // GET /api/gov/services?q=&category= → { services:[...] }
    [HttpGet("services")]
    public async Task<IActionResult> Services(
        [FromQuery] string? q,
        [FromQuery] string? category,
        CancellationToken cancellationToken)
    {
        var services = await _catalog.SearchAsync(q, category, cancellationToken);
        return Ok(new ServicesResponse(services.Select(ToServiceDto).ToList()));
    }

    // ── 2. Ficha del trámite (pasos + elegibilidad + requisitos + tasa) ─
    // GET /api/gov/service/{id} → { service:{...} }
    [HttpGet("service/{id}")]
    public async Task<IActionResult> Service(string id, CancellationToken cancellationToken)
    {
        var detail = await _catalog.GetAsync(id ?? string.Empty, cancellationToken);
        if (detail is null)
        {
            return NotFound(new { error = $"Trámite '{id}' no encontrado." });
        }

        var s = detail.Summary;
        return Ok(new ServiceDetailResponse(new ServiceDetailDto(
            Id: s.Id,
            Name: s.Name,
            Summary: s.Summary,
            Category: s.Category,
            Agency: s.Agency,
            Steps: detail.Steps.Select(st => new StepDto(st.Id, st.Title, st.Detail)).ToList(),
            Eligibility: detail.Eligibility,
            Required: detail.Required,
            FeeMinor: ToFeeMinor(s.FeeMinor, s.Currency),
            Currency: s.Currency,
            // La clave con que el trámite se radica por la puerta (009, T031): la del nodo con el catálogo en el
            // contenido, la misma con que el publicador ofrece su tasa; en la siembra, su id.
            Key: s.Clave ?? s.Id)));
    }

    // ── 3. Formulario dinámico seccionado ───────────────────────────────
    // GET /api/gov/form/{serviceId} → { form:{ id, title, sections:[...] } }
    [HttpGet("form/{serviceId}")]
    public async Task<IActionResult> Form(string serviceId, CancellationToken cancellationToken)
    {
        var detail = await _catalog.GetAsync(serviceId ?? string.Empty, cancellationToken);
        if (detail is null)
        {
            return NotFound(new { error = $"Trámite '{serviceId}' no encontrado." });
        }

        var f = detail.FormDefinition;
        return Ok(new FormResponse(new FormDto(
            Id: f.Id,
            Title: f.Title,
            Sections: f.Sections.Select(sec => new FormSectionDto(
                sec.Id,
                sec.Title,
                sec.Fields.Select(ToFieldDto).ToList())).ToList())));
    }

    // ── 4. Radicar solicitud (crea el expediente; tasa = pago opcional) ─
    // POST /api/gov/application { serviceId, answers, notificationConsent? } → { application:{...} }
    //
    // notificationConsent: { accepted, textVersion } — lo que contestó sobre la notificación
    // electrónica de los actos (009, S-7), guardado con las respuestas. Sin ella es «no consta»
    // y radica igual; con ella, la versión del texto es obligatoria: es lo que prueba qué aceptó.
    [HttpPost("application")]
    public async Task<IActionResult> CreateApplication(
        [FromBody] CreateApplicationRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.ServiceId))
        {
            return BadRequest(new { error = "serviceId es requerido." });
        }

        NotificationConsent? consent = null;
        if (request.NotificationConsent is NotificationConsentRequest respuesta)
        {
            if (string.IsNullOrWhiteSpace(respuesta.TextVersion))
            {
                return BadRequest(new { error = "notificationConsent.textVersion es requerido: es lo que prueba qué texto se aceptó." });
            }
            consent = new NotificationConsent(respuesta.Accepted, respuesta.TextVersion.Trim(), DateTimeOffset.UtcNow);
        }

        var answers = request.Answers ?? new Dictionary<string, string>();
        var citizen = ResolveCitizen(answers);

        RadicarResult result;
        try
        {
            result = await _applications.RadicarAsync(
                request.ServiceId.Trim(), answers, citizen, consent, cancellationToken);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Motivo() });
        }

        return Ok(new ApplicationResponse(ToApplicationSummaryDto(result.Case)));
    }

    // ── 4 bis. Las respuestas de una radicación por la puerta (009, T027) ──
    // POST /api/gov/radicaciones/{radicacion}/respuestas { answers } → { radicado, caseId }   🔒 sesión
    //
    // Entre abrir y cerrar: el orquestador ya autorizó la tasa y el CMS guarda lo que él no lleva —las
    // respuestas, el contacto de la sesión y el radicado—, sólo sobre una radicación en curso del miembro.
    // Repetirla reemplaza mientras siga abierta. Con el mismo filtro de mismo origen y JSON que la puerta.
    [HttpPost("radicaciones/{radicacion}/respuestas")]
    public async Task<IActionResult> RespuestasDeLaRadicacion(string radicacion, CancellationToken cancellationToken)
    {
        SinCache();
        var (denied, _) = RequireMember();
        if (denied is not null) { return denied; }
        if (FiltroMismoOrigenJson.Origen(Request) is { } ajeno) return Problema(ajeno);
        if (FiltroMismoOrigenJson.Tipo(Request) is { } tipo) return Problema(tipo);
        var (cuerpo, grande) = await FiltroMismoOrigenJson.LeerCuerpoAsync(Request, cancellationToken);
        if (grande is not null) return Problema(grande);
        if (_artefacto is null) return Problema(SinArtefacto);

        RespuestasDeLaRadicacionRequest? pedido;
        try { pedido = JsonSerializer.Deserialize<RespuestasDeLaRadicacionRequest>(cuerpo!, LecturaWeb); }
        catch (JsonException) { pedido = null; }

        var answers = pedido?.Answers ?? new Dictionary<string, string>();
        var r = await _artefacto.GuardarRespuestasAsync(radicacion, ResolveCitizen(answers), answers, cancellationToken);
        return r.Bien
            ? Ok(new RespuestasDeLaRadicacionResponse(r.Expediente!.Radicado, r.Expediente.CaseId))
            : Problema(r);
    }

    /// <summary>
    /// Confirma o borra lo que quedó por confirmar de la puerta antes de una lectura: nunca falla hacia quien lee, y
    /// sin artefacto no hace nada.
    /// </summary>
    private Task ReconciliarDeLaPuertaAsync(Func<ExpedienteDeLaPuerta, bool> lasQueTocan, CancellationToken cancellationToken)
        => _artefacto?.ReconciliarAsync(lasQueTocan, cancellationToken) ?? Task.CompletedTask;

    private static readonly JsonSerializerOptions LecturaWeb = new(JsonSerializerDefaults.Web);

    private static readonly ResultadoDelExpediente SinArtefacto = new(
        StatusCodes.Status503ServiceUnavailable, "gov.radicacion_no_disponible", "La radicación por la puerta no está abierta en este sitio.");

    /// <summary>Que nadie guarde la respuesta: lleva el radicado y las respuestas de una persona.</summary>
    private void SinCache()
    {
        if (HttpContext is { } http) http.Response.Headers.CacheControl = "no-store";
    }

    private static ContentResult Problema(FalloDeLaPuerta f) => Problema(f.Estado, f.Codigo, f.Mensaje, f.Transitorio, null);

    private static ContentResult Problema(ResultadoDelExpediente r)
        => Problema(r.Estado, r.Codigo!, r.Mensaje ?? string.Empty, r.Transitorio, r.EstadoDeLaRadicacion);

    /// <summary>Un «no» de la radicación como problem+json, con <c>code</c> y <c>transient</c> como la puerta.</summary>
    private static ContentResult Problema(int estado, string codigo, string mensaje, bool transitorio, string? deLaRadicacion) => new()
    {
        StatusCode = estado,
        ContentType = "application/problem+json",
        Content = JsonSerializer.Serialize(new
        {
            type = "about:blank",
            title = ReasonPhrases.GetReasonPhrase(estado),
            status = estado,
            detail = mensaje,
            code = codigo,
            transient = transitorio,
            // En qué quedó la saga, cuando eso es el motivo: «Completed», «Compensated».
            radicacionStatus = deLaRadicacion,
        }, LecturaWeb),
    };

    // ── 5. Mis solicitudes (carpeta del ciudadano) ─────────────────────
    // GET /api/gov/applications?citizen= → { applications:[...] }
    // El ?citizen=<email> DESAPARECE: era el IDOR. La bandeja es la del member de la sesión.
    [HttpGet("applications")]
    public async Task<IActionResult> Applications(CancellationToken cancellationToken)
    {
        var (denied, actorKey) = RequireMember();
        if (denied is not null) { return denied; }

        await ReconciliarDeLaPuertaAsync(e => e.Dueno == actorKey, cancellationToken);
        var items = await _tracking.ListForMemberAsync(actorKey, cancellationToken);
        return Ok(new ApplicationsResponse(items.Select(ToApplicationSummaryFromInbox).ToList()));
    }

    // ── 6. Detalle de una solicitud (timeline + documentos + mensajes) ──
    // GET /api/gov/application/{id} → { application:{...} }
    [HttpGet("application/{id}")]
    public async Task<IActionResult> Application(string id, CancellationToken cancellationToken)
    {
        var (denied, actorKey) = RequireMember();
        if (denied is not null) { return denied; }

        await ReconciliarDeLaPuertaAsync(e => e.Dueno == actorKey, cancellationToken);
        var detail = await _tracking.GetCaseAsync(id ?? string.Empty, cancellationToken);
        if (detail is null)
        {
            return NotFound(new { error = $"Solicitud '{id}' no encontrada." });
        }

        var foreign = DenyIfForeignCase(detail, actorKey);
        if (foreign is not null) { return foreign; }

        return Ok(new ApplicationDetailResponse(await ToApplicationDetailDtoAsync(detail, cancellationToken)));
    }

    // ── 6 bis. Subsanar (el ciudadano dueño) ────────────────────────────
    // POST /api/gov/application/{id}/resubmit { note? } → { application:{...} }   🔒 sesión + dueño
    //
    // El ciudadano envía lo que se le pidió y el expediente vuelve a revisión (D-2 de 009). Sólo
    // el dueño: subsanar el expediente de otro sería responder por él. Uno que ya volvió a revisión
    // no hace nada (el doble clic, escenario 6 de la US1); desde cualquier otro estado, 409 con el
    // motivo. Quien firma es la persona de la sesión, con la cara del ciudadano.
    [HttpPost("application/{id}/resubmit")]
    public async Task<IActionResult> Resubmit(
        string id,
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] ResubmitRequest? request,
        CancellationToken cancellationToken)
    {
        var (denied, actorKey) = RequireMember();
        if (denied is not null) { return denied; }

        var detail = await _tracking.GetCaseAsync(id ?? string.Empty, cancellationToken);
        if (detail is null)
        {
            return NotFound(new { error = $"Solicitud '{id}' no encontrada." });
        }

        var foreign = DenyIfForeignCase(detail, actorKey);
        if (foreign is not null) { return foreign; }

        CaseDetail updated;
        try
        {
            updated = await _workflow.ResubmitAsync(
                detail.CaseId, ActorDeLaSesion(CaseRoles.KindCiudadano), request?.Note, cancellationToken);
        }
        catch (ArgumentException ex)
        {
            // El expediente existe: lo que no existe es «subsanar» en el proceso que lo lleva.
            return Conflict(new { error = ex.Motivo() });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Motivo() });
        }

        return Ok(new ApplicationDetailResponse(await ToApplicationDetailDtoAsync(updated, cancellationToken)));
    }

    /// <summary>El expediente hacia su dueño: la lectura y lo que devuelve subsanar son lo mismo.</summary>
    private async Task<ApplicationDetailDto> ToApplicationDetailDtoAsync(CaseDetail detail, CancellationToken cancellationToken)
    {
        var messages = await LoadMessagesAsync(detail, cancellationToken);
        var (actions, stale) = await AccionesAsync(detail.CaseId, CaseRoles.Ciudadano, cancellationToken);
        return new ApplicationDetailDto(
            Id: detail.CaseId,
            Reference: detail.Radicado,
            ServiceId: detail.TramiteId,
            ServiceName: detail.TramiteName,
            Status: GovStatusSlugs.ToSlug(detail.Status),
            SubmittedAt: detail.RadicadoAt,
            CurrentStage: detail.CurrentStage,
            Timeline: detail.Timeline.Select(ToTimelineDto).ToList(),
            Documents: detail.Documents.Select(ToDocumentDto).ToList(),
            Messages: messages,
            Actions: actions,
            FeeMinor: ToFeeMinor(detail.FeeMinor, detail.Currency),
            FeeStatus: ToFeeStatusSlug(detail.FeeStatus),
            Stale: stale);
    }

    // ── 7. Adjuntar un documento al expediente (T6 — bytes de verdad) ───
    // POST /api/gov/document  multipart: file + applicationId → { document:{...} }
    //
    // Es el PRIMER endpoint del proyecto que recibe un binario. Antes aceptaba
    // `{applicationId, name}` en JSON y respondía `accepted` sin que existiera fichero
    // alguno: el ciudadano adjuntaba el escaneo de su cédula y no se guardaba nada.
    [HttpPost("document")]
    [RequestSizeLimit(MaxUploadBytes)]
    public async Task<IActionResult> Document(
        [FromForm] IFormFile? file,
        [FromForm] string? applicationId,
        CancellationToken cancellationToken)
    {
        // Autenticar ANTES de leer el fichero: no se procesa —ni se sube a memoria— el
        // input de un anónimo. Adjuntar es ESCRIBIR sobre el expediente, así que exige
        // ser su dueño; sin esto cualquiera ensucia el expediente de otro.
        var (denied, actorKey) = RequireMember();
        if (denied is not null) { return denied; }

        if (file is null || file.Length == 0 || string.IsNullOrWhiteSpace(applicationId))
        {
            return BadRequest(new { error = "applicationId y file son requeridos." });
        }
        if (file.Length > MaxUploadBytes)
        {
            return BadRequest(new { error = $"El documento supera el máximo de {MaxUploadBytes / (1024 * 1024)} MB." });
        }

        var contentType = (file.ContentType ?? string.Empty).Trim().ToLowerInvariant();
        if (!AllowedUploadContentTypes.Contains(contentType))
        {
            return BadRequest(new { error = "Solo se aceptan documentos PDF, JPG o PNG." });
        }

        var target = await _tracking.GetCaseAsync(applicationId.Trim(), cancellationToken);
        if (target is null)
        {
            return NotFound(new { error = $"Expediente '{applicationId}' no encontrado." });
        }
        var foreign = DenyIfForeignCase(target, actorKey);
        if (foreign is not null) { return foreign; }

        // Se lee a memoria solo DESPUÉS de pasar auth + ownership + límites.
        byte[] content;
        await using (var stream = file.OpenReadStream())
        using (var buffer = new MemoryStream())
        {
            await stream.CopyToAsync(buffer, cancellationToken);
            content = buffer.ToArray();
        }

        CitizenDocumentRef doc;
        try
        {
            doc = await _documents.UploadAsync(
                applicationId.Trim(),
                SafeFileName(file.FileName),
                contentType,
                content,
                cancellationToken);
        }
        catch (ArgumentException ex)
        {
            return NotFound(new { error = ex.Motivo() });
        }

        return Ok(new DocumentResponse(ToDocumentDto(doc)));
    }

    // ── 7b. Descargar un documento del expediente (T6) ───────────────────
    // GET /api/gov/document/{caseId}/{docId} → el binario
    //
    // El id del fichero es opaco, pero eso NO es la autorización: se comprueba permiso
    // en CADA descarga. Lo puede bajar el DUEÑO del expediente o un FUNCIONARIO (que
    // necesita ver los adjuntos para decidir) — nadie más.
    [HttpGet("document/{caseId}/{docId}")]
    public async Task<IActionResult> DownloadDocument(
        string caseId,
        string docId,
        CancellationToken cancellationToken)
    {
        var (denied, actorKey) = RequireMember();
        if (denied is not null) { return denied; }

        var target = await _tracking.GetCaseAsync(caseId ?? string.Empty, cancellationToken);
        if (target is null)
        {
            return NotFound(new { error = $"Expediente '{caseId}' no encontrado." });
        }

        var isOwner = target.Citizen.MemberKey == actorKey;
        if (!isOwner && !_gate.HasAnyRole(OfficerRolesCsv))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "El documento no es suyo." });
        }

        var doc = target.Documents.FirstOrDefault(d =>
            string.Equals(d.Id, docId, StringComparison.Ordinal));
        if (doc is null || string.IsNullOrWhiteSpace(doc.FileId))
        {
            // Sin FileId = documento previo a T6 (metadata sin binario). No es un error
            // del servidor: sencillamente no hay nada que descargar.
            return NotFound(new { error = "El documento no tiene un archivo asociado." });
        }

        var stored = await _files.ReadAsync(GovDocumentScope, doc.FileId, cancellationToken);
        if (stored is null)
        {
            return NotFound(new { error = "El archivo no está disponible." });
        }

        // `attachment` + nosniff: el navegador lo descarga, nunca lo interpreta como
        // documento activo en el origen del sitio (un SVG/HTML disfrazado no ejecuta).
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(stored.Content, stored.ContentType, stored.FileName);
    }

    // ══════════════════════ FUNCIONARIO ══════════════════════

    // ── 8. Cola del funcionario ─────────────────────────────────────────
    // GET /api/gov/queue?agency=&status= → { cases:[...] }   🔒 rol funcionario
    [HttpGet("queue")]
    public async Task<IActionResult> Queue(
        [FromQuery] string? agency,
        [FromQuery] string? status,
        CancellationToken cancellationToken)
    {
        if (RequireOfficer() is { } denied) { return denied; }

        await ReconciliarDeLaPuertaAsync(_ => true, cancellationToken);
        var cases = await _tracking.GetQueueAsync(agency, status, cancellationToken);
        return Ok(new QueueResponse(cases.Select(ToQueueDto).ToList()));
    }

    // ── 9. Detalle del expediente (funcionario) ─────────────────────────
    // GET /api/gov/case/{id} → { case:{ application, answers, documents, timeline, decision? } }
    // 🔒 rol funcionario — expone PII (cédula/correo/teléfono) de OTROS ciudadanos.
    [HttpGet("case/{id}")]
    public async Task<IActionResult> Case(string id, CancellationToken cancellationToken)
    {
        if (RequireOfficer() is { } denied) { return denied; }

        var detail = await _tracking.GetCaseAsync(id ?? string.Empty, cancellationToken);
        if (detail is null)
        {
            return NotFound(new { error = $"Expediente '{id}' no encontrado." });
        }

        return Ok(new CaseResponse(await ToCaseDtoAsync(detail, cancellationToken)));
    }

    // ── 9 bis. Tomar el expediente (funcionario) ────────────────────────
    // POST /api/gov/case/{id}/take { note? } → { case:{...} }   🔒 rol funcionario
    //
    // De «radicado» a «en revisión», a nombre de quien lo toma (D-1 de 009): decidir exige
    // tomar antes (P4). Tomar uno que YA está en revisión no hace nada —el doble clic y el
    // reintento tras un corte dan el expediente como quedó—; desde cualquier otro estado, 409
    // con el motivo. Un expediente de la v1 no se toma (su proceso no tiene ese paso): también 409.
    [HttpPost("case/{id}/take")]
    public async Task<IActionResult> Take(
        string id,
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] TakeRequest? request,
        CancellationToken cancellationToken)
    {
        if (RequireOfficer() is { } denied) { return denied; }

        var detail = await _tracking.GetCaseAsync(id ?? string.Empty, cancellationToken);
        if (detail is null)
        {
            return NotFound(new { error = $"Expediente '{id}' no encontrado." });
        }

        CaseDetail updated;
        try
        {
            updated = await _workflow.TakeAsync(
                detail.CaseId, ActorDeLaSesion(CaseRoles.KindFuncionario), request?.Note, cancellationToken);
        }
        catch (ArgumentException ex)
        {
            // El expediente existe: lo que no existe es «tomar» en el proceso que lo lleva.
            return Conflict(new { error = ex.Motivo() });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Motivo() });
        }

        return Ok(new CaseResponse(await ToCaseDtoAsync(updated, cancellationToken)));
    }

    // ── 10. Decisión del funcionario (máquina de estados auditada) ──────
    // POST /api/gov/decision { caseId, outcome, note } → { case:{...} }   🔒 rol funcionario
    [HttpPost("decision")]
    public async Task<IActionResult> Decision(
        [FromBody] DecisionRequest? request,
        CancellationToken cancellationToken)
    {
        if (RequireOfficer() is { } denied) { return denied; }

        if (request is null || string.IsNullOrWhiteSpace(request.CaseId) || string.IsNullOrWhiteSpace(request.Outcome))
        {
            return BadRequest(new { error = "caseId y outcome (approve | reject | request-info) son requeridos." });
        }

        CaseDetail updated;
        try
        {
            updated = await _workflow.DecideAsync(
                request.CaseId.Trim(), request.Outcome.Trim(), request.Note ?? string.Empty,
                ActorDeLaSesion(CaseRoles.KindFuncionario), cancellationToken);
        }
        catch (ArgumentException ex)
        {
            return NotFound(new { error = ex.Motivo() });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Motivo() });
        }

        return Ok(new CaseResponse(await ToCaseDtoAsync(updated, cancellationToken)));
    }

    // ══════════════════════ ACTOS NOTIFICADOS (HU #62) ══════════════════════
    //
    // Lo que estos tres endpoints existen para poder sostener no es «se le avisó»: es
    // CUÁNDO ACCEDIÓ y CÓMO SE SUPO QUE ERA ÉL. Un correo enviado prueba que salió del
    // servidor, no que llegó a quien tenía que llegar, y el término de un recurso no
    // empieza a contar con lo que salió.

    // ── 11. Notificar un acto (funcionario) ─────────────────────────────
    // POST /api/gov/notification { caseId, title, body, documentRef?, acknowledgeBefore? }
    //   → { notification:{...} }   🔒 rol funcionario
    [HttpPost("notification")]
    public async Task<IActionResult> Notify(
        [FromBody] NotifyActRequest? request,
        CancellationToken cancellationToken)
    {
        if (RequireOfficer() is { } denied) { return denied; }

        if (request is null
            || string.IsNullOrWhiteSpace(request.CaseId)
            || string.IsNullOrWhiteSpace(request.Title))
        {
            return BadRequest(new { error = "caseId y title son requeridos." });
        }

        var detail = await _tracking.GetCaseAsync(request.CaseId.Trim(), cancellationToken);
        if (detail is null)
        {
            return NotFound(new { error = $"Expediente '{request.CaseId}' no encontrado." });
        }

        // El destinatario sale del EXPEDIENTE, no del cuerpo: notificar a quien mande la
        // petición sería dejar que un funcionario le ponga en conocimiento un acto a
        // cualquiera, y el dueño del expediente es un dato que ya está.
        if (detail.Citizen.MemberKey is not Guid citizenKey || citizenKey == Guid.Empty)
        {
            // Los expedientes radicados antes de ADR 0103 no tienen Member detrás. No se
            // adivina: notificar electrónicamente a alguien que no tiene con qué acceder
            // dejaría escrito un término que nadie puede empezar a contar.
            return Conflict(new
            {
                error = "El expediente no tiene un ciudadano con sesión; no admite notificación electrónica.",
            });
        }

        // La notificación electrónica exige que el ciudadano la haya ACEPTADO al radicar (009, S-7):
        // el término de un recurso empezaría a correr sobre un medio que nadie autorizó. Rechazada o
        // «no consta» da lo mismo —no consta no es una aceptación—: no se notifica por aquí, y el
        // acto queda para otro medio, que esto no hace.
        if (detail.NotificationConsent is not { Accepted: true })
        {
            return Conflict(new
            {
                error = "El ciudadano no aceptó la notificación electrónica, o no consta que la aceptara: "
                    + "el acto se notifica por otro medio.",
                code = "gov.sin_aceptacion",
            });
        }

        GovActNotification notification;
        try
        {
            notification = await _notifications.NotifyAsync(
                caseId: detail.CaseId,
                radicado: detail.Radicado,
                citizenMemberKey: citizenKey,
                title: request.Title.Trim(),
                body: request.Body ?? string.Empty,
                documentRef: request.DocumentRef,
                acknowledgeBeforeUtc: request.AcknowledgeBefore,
                cancellationToken: cancellationToken);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Motivo() });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Motivo() });
        }

        // Al funcionario SÍ se le devuelve el cuerpo: es quien lo escribió.
        return Ok(new ActNotificationResponse(ToActNotificationDto(notification, revealBody: true)));
    }

    // ── 12. Mis notificaciones (bandeja del ciudadano) ──────────────────
    // GET /api/gov/notifications → { notifications:[...] }
    //
    // La bandeja NO trae el cuerpo del acto mientras no esté abierto — ver
    // ToActNotificationDto. Poder leerlo desde el listado convertiría el acuse en
    // decoración: el ciudadano se enteraría de lo resuelto sin que nada registrara que
    // accedió, que es exactamente lo que esta HU existe para impedir.
    [HttpGet("notifications")]
    public async Task<IActionResult> Notifications(CancellationToken cancellationToken)
    {
        var (denied, actorKey) = RequireMember();
        if (denied is not null) { return denied; }

        var items = await _notifications.GetForCitizenAsync(actorKey, cancellationToken);
        return Ok(new ActNotificationsResponse(
            items.Select(n => ToActNotificationDto(n, revealBody: false)).ToList()));
    }

    // ── 13. Abrir un acto (empieza el término) ──────────────────────────
    // POST /api/gov/notification/{id}/open → { notification:{...} }
    //
    // Es POST y no GET a propósito: abrir un acto ESCRIBE —deja registrado el acceso— y
    // un GET lo dispararía un prefetch del navegador o un rastreador siguiendo el enlace
    // de un correo, arrancando el término sin que nadie hubiera leído nada.
    [HttpPost("notification/{id}/open")]
    public async Task<IActionResult> OpenNotification(string id, CancellationToken cancellationToken)
    {
        var (denied, actorKey) = RequireMember();
        if (denied is not null) { return denied; }

        GovActNotification opened;
        try
        {
            opened = await _notifications.AcknowledgeAsync(id ?? string.Empty, actorKey, cancellationToken);
        }
        catch (ArgumentException ex)
        {
            return NotFound(new { error = ex.Motivo() });
        }
        catch (GovActNotAddresseeException)
        {
            // Ajena y vencida son cosas distintas hacia fuera —403 «no es suya» y 409 «se
            // pasó el plazo»—, y una sola de las dos le dice al ciudadano qué hacer a
            // continuación. Por eso el seam lanza un TIPO y no un mensaje: mirar el texto
            // habría atado esta decisión a una frase que cualquiera reescribe.
            await AsentarAccesoRechazadoAsync(id ?? string.Empty, cancellationToken);
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "La notificación no es suya." });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Motivo() });
        }

        return Ok(new ActNotificationResponse(ToActNotificationDto(opened, revealBody: true)));
    }

    /// <summary>
    /// Deja asiento de que alguien ajeno intentó abrir un acto notificado (HU #15).
    /// </summary>
    /// <remarks>
    /// <para><b>Que el intento se rechace no lo vuelve ruido: es el dato que un auditor pide
    /// primero.</b> Hasta acá el 403 salía y no quedaba escrito en ninguna parte, así que el día
    /// que alguien preguntara quién intentó leer un expediente ajeno no había nada que mirar.</para>
    ///
    /// <para><b>El asiento dice CmsSession y nunca más que eso</b>, y ésa es la parte fina. Este
    /// camino no presenta ni verifica ningún token: lo único que respalda al actor es nuestra
    /// propia cookie. Que el despliegue <i>sepa emitir</i> identidad verificable
    /// (<c>Synergos:Identity:Mode=Api</c>) no cambia lo que aquí se comprobó — escribir
    /// <c>IdentityToken</c> porque se podría haber presentado uno guardaría como hecho lo que
    /// nadie verificó. Es el defecto #42 con otro disfraz, sobre el registro que más se conserva.</para>
    ///
    /// <para><b>Y no se le devuelve nada al llamador si el asiento falla.</b> Escribirlo es
    /// mejor-esfuerzo hacia fuera y decisión escrita hacia dentro: el escritor ya guarda local
    /// primero y anota el hueco si el reenvío no llega (<c>HttpAuditTrailWriter</c>), así que
    /// tragarse una excepción acá convertiría el 403 en un 500 sin proteger nada.</para>
    /// </remarks>
    private async Task AsentarAccesoRechazadoAsync(string notificationId, CancellationToken cancellationToken)
    {
        var correo = _gate.CurrentMemberEmail ?? string.Empty;

        var evt = new AuditEvent(
            Id: Guid.NewGuid().ToString("N"),
            OccurredAtUtc: DateTime.UtcNow,
            ActorEmail: correo,
            ActorName: _gate.CurrentMemberDisplayName ?? correo,
            Action: "gov.notification.open",
            Resource: notificationId,
            Outcome: "failure",
            Detail: "not_the_addressee",
            Assertion: IdentityAssertions.CmsSession);

        await _audit.WriteAsync(evt, cancellationToken);
    }

    // ══════════════════════ Mappers ══════════════════════

    private ServiceDto ToServiceDto(TramiteSummary t) => new(
        Id: t.Id,
        Name: t.Name,
        Summary: t.Summary,
        Category: t.Category,
        Agency: t.Agency,
        EstimatedDays: t.EstimatedDays,
        FeeMinor: ToFeeMinor(t.FeeMinor, t.Currency),
        Currency: t.Currency);

    private static FieldDto ToFieldDto(TramiteFormField f)
    {
        // options solo viaja para select/radio; pattern solo si está definido —
        // el normalizer estricto de la UI acepta ausencia (null) de ambos.
        var options = f.Options.Count > 0
            ? f.Options.Select(o => new FieldOptionDto(o.Value, o.Label)).ToList()
            : null;
        return new FieldDto(
            Id: f.Id,
            Label: f.Label,
            Type: f.Type,
            Required: f.Required,
            Help: f.Help,
            Options: options,
            Pattern: f.Pattern);
    }

    private static ApplicationSummaryDto ToApplicationSummaryDto(CaseDetail c) => new(
        Id: c.CaseId,
        Reference: c.Radicado,
        ServiceId: c.TramiteId,
        ServiceName: c.TramiteName,
        Status: GovStatusSlugs.ToSlug(c.Status),
        SubmittedAt: c.RadicadoAt,
        CurrentStage: c.CurrentStage,
        FeeMinor: ToFeeMinor(c.FeeMinor, c.Currency),
        FeeStatus: ToFeeStatusSlug(c.FeeStatus));

    private static ApplicationSummaryDto ToApplicationSummaryFromInbox(CaseInboxItem i) => new(
        Id: i.CaseId,
        Reference: i.Radicado,
        ServiceId: string.Empty, // la lista de la carpeta no necesita el serviceId
        ServiceName: i.TramiteName,
        Status: GovStatusSlugs.ToSlug(i.Status),
        SubmittedAt: i.RadicadoAt,
        CurrentStage: i.CurrentStage,
        FeeMinor: ToFeeMinor(i.FeeMinor, currency: null),
        FeeStatus: ToFeeStatusSlug(i.FeeStatus));

    // La tasa viaja en las unidades MENORES de su moneda (COP: centavos), con la tabla con la que
    // la UI la divide para pintarla (#196, G-13). Antes salía el monto en pesos con el nombre
    // feeMinor, y la UI sólo lo pintaba bien porque el Intl de Chromium da 0 decimales a COP. La
    // bandeja no trae la moneda del
    // expediente (sólo dice si hay tasa): sin moneda, los dos decimales de casi todas.
    private static long ToFeeMinor(decimal fee, string? currency) => UnidadesMenores.Desde(Math.Max(0m, fee), currency);

    /// <summary>
    /// El estado del cobro de la tasa como lo lee la UI, o <c>null</c> cuando no consta.
    /// </summary>
    /// <remarks>
    /// <para><b>No inventa ninguno.</b> Traduce el nombre que el agregado guardó
    /// (<c>CaseState.PaymentStatus</c> — los de <c>PaymentStatus</c> más el
    /// <c>Unavailable</c> de <c>StubApplicationService</c>) al slug en minúsculas que usa el
    /// resto de este borde. Lo que no reconoce baja a minúsculas y sale tal cual: es el mismo
    /// valor con otra caja, no un relleno. Una tasa exenta no abre sesión y llega en blanco,
    /// y en blanco se queda.</para>
    /// </remarks>
    private static string? ToFeeStatusSlug(string? raw) => string.IsNullOrWhiteSpace(raw)
        ? null
        : raw.Trim() switch
        {
            "RequiresAction" => "requires-action",
            var other => other.ToLowerInvariant(),
        };

    /// <summary>
    /// Un acto notificado, hacia la UI.
    /// </summary>
    /// <remarks>
    /// <para><b>El cuerpo y el PDF sólo salen si el acto ya está abierto</b>
    /// (<paramref name="revealBody"/> es la excepción del funcionario y la del propio acuse, que
    /// devuelve lo que se acaba de abrir). Si la bandeja los trajera, el ciudadano leería lo
    /// resuelto sin que nada registrara su acceso y el término no empezaría nunca — que es justo
    /// lo que esta superficie existe para impedir.</para>
    ///
    /// <para><b><c>OpenedBy</c> no se mapea.</b> Es la llave del Member y no tiene por qué salir
    /// del servidor: es la lección de #47, donde el seudónimo del comprador acabó en pantalla,
    /// aplicada antes de que pase.</para>
    /// </remarks>
    private static ActNotificationDto ToActNotificationDto(GovActNotification n, bool revealBody)
    {
        var visible = revealBody || n.Opened;
        return new ActNotificationDto(
            Id: n.Id,
            CaseId: n.CaseId,
            Reference: n.Radicado,
            Title: n.Title,
            Body: visible ? n.Body : null,
            DocumentRef: visible ? n.DocumentRef : null,
            NotifiedAt: n.NotifiedAtUtc,
            AcknowledgeBefore: n.AcknowledgeBeforeUtc,
            OpenedAt: n.OpenedAtUtc,
            OpenedWith: n.OpenedWith,
            Opened: n.Opened);
    }

    private static QueueCaseDto ToQueueDto(CaseInboxItem i) => new(
        Id: i.CaseId,
        Reference: i.Radicado,
        ServiceName: i.TramiteName,
        CitizenName: i.CitizenName,
        Status: GovStatusSlugs.ToSlug(i.Status),
        SubmittedAt: i.RadicadoAt,
        Priority: i.Priority.ToString().ToLowerInvariant(),
        SlaDaysLeft: i.SlaDaysLeft,
        FeeMinor: ToFeeMinor(i.FeeMinor, currency: null),
        FeeStatus: ToFeeStatusSlug(i.FeeStatus));

    // El detalle del funcionario muestra las respuestas con su ETIQUETA humana (no el
    // fieldId), resolviéndolas contra la definición del formulario del trámite. Si el
    // trámite ya no existe en el catálogo, cae al fieldId como etiqueta.
    private async Task<CaseDto> ToCaseDtoAsync(CaseDetail c, CancellationToken cancellationToken)
    {
        var labels = await ResolveFieldLabelsAsync(c.TramiteId, cancellationToken);
        // Lo que el funcionario necesita ver del expediente no es que se notificó, es si el
        // ciudadano ABRIÓ — y con qué se afirmó que era él. Es lo único que la entidad puede
        // sostener el día que alguien recurre tarde.
        var notificaciones = await _notifications.GetForCaseAsync(c.CaseId, cancellationToken);
        var (actions, stale) = await AccionesAsync(c.CaseId, CaseRoles.Funcionario, cancellationToken);
        return new CaseDto(
            Application: new CaseApplicationDto(
                Id: c.CaseId,
                Reference: c.Radicado,
                ServiceName: c.TramiteName,
                CitizenName: c.Citizen.Name,
                Status: GovStatusSlugs.ToSlug(c.Status),
                SubmittedAt: c.RadicadoAt,
                CurrentStage: c.CurrentStage,
                FeeMinor: ToFeeMinor(c.FeeMinor, c.Currency),
                FeeStatus: ToFeeStatusSlug(c.FeeStatus)),
            Answers: c.FormData
                .Select(kv => new AnswerDto(labels.GetValueOrDefault(kv.Key, kv.Key), kv.Value))
                .ToList(),
            Documents: c.Documents.Select(ToDocumentDto).ToList(),
            Timeline: c.Timeline.Select(ToTimelineDto).ToList(),
            Notifications: notificaciones.Select(n => ToActNotificationDto(n, revealBody: true)).ToList(),
            Decision: c.Decision is null
                ? null
                : new DecisionDto(c.Decision.Outcome, c.Decision.Note, c.Decision.DecidedAt, c.Decision.DecidedBy),
            Actions: actions,
            Stale: stale,
            NotificationConsent: NotificationConsentSlugs.ToSlug(c.NotificationConsent));
    }

    /// <summary>
    /// Lo que el proceso del expediente permite a la cara que mira (D-3 de 009), o nada y
    /// <c>stale</c> si no se pudo consultar.
    /// </summary>
    /// <remarks>
    /// La consola y el portal pintan sólo esto, en vez de la lista fija que ofrecía tres acciones
    /// que la capacidad rechazaba. Con la capacidad de procesos caída no se ofrece nada de una
    /// tabla local (FR-012), y <c>stale</c> lo DICE: lo que se ve es lo último que el CMS sabe y
    /// no hay acciones ahora. Sin esa marca, «sin botones» se leería como «no hay nada que hacer».
    /// </remarks>
    private async Task<(IReadOnlyList<string> Actions, bool Stale)> AccionesAsync(
        string caseId, string role, CancellationToken cancellationToken)
    {
        try
        {
            return ((await _workflow.ActionsAsync(caseId, role, cancellationToken)).ToList(), false);
        }
        catch (InvalidOperationException)
        {
            return (Array.Empty<string>(), true);
        }
    }

    private async Task<IReadOnlyDictionary<string, string>> ResolveFieldLabelsAsync(string tramiteId, CancellationToken cancellationToken)
    {
        var detail = await _catalog.GetAsync(tramiteId, cancellationToken);
        if (detail is null)
        {
            return new Dictionary<string, string>();
        }
        return detail.FormDefinition.Sections
            .SelectMany(s => s.Fields)
            .GroupBy(f => f.Id)
            .ToDictionary(g => g.Key, g => g.First().Label);
    }

    // `downloadUrl` solo viaja si hay binario detrás: así la UI sabe cuándo ofrecer
    // descarga sin adivinar. Los documentos previos a T6 (sin FileId) salen con null y
    // la UI los pinta como texto, igual que antes.
    private static DocumentDto ToDocumentDto(CitizenDocumentRef d) => new(
        Id: d.Id,
        Name: d.Name,
        Status: d.Status,
        UploadedAt: d.UploadedAt,
        ContentType: d.ContentType,
        SizeBytes: d.SizeBytes,
        DownloadUrl: string.IsNullOrWhiteSpace(d.FileId)
            ? null
            : $"/api/gov/document/{Uri.EscapeDataString(d.CaseId)}/{Uri.EscapeDataString(d.Id)}");

    /// <summary>
    /// Un hito del timeline, hacia la UI.
    /// </summary>
    /// <remarks>
    /// <para><b>Un hito <c>pending</c> sale SIN fecha, y ésa es la corrección.</b>
    /// <see cref="CaseTimelineEntry.Date"/> no es anulable, así que el motor rellena los hitos
    /// que todavía no ocurrieron con la fecha de RADICACIÓN — un valor de relleno que hacia
    /// fuera se lee como un hecho. El ciudadano veía «Decisión · 24 de junio de 2026» sobre una
    /// decisión que nadie ha tomado, y en un expediente administrativo una fecha que no ocurrió
    /// es lo único que no se puede enseñar: de fechas así dependen los términos.</para>
    ///
    /// <para>La UI ya lo contempla —<c>date: entry.date ? formatDate(entry.date) : undefined</c>—
    /// y su modelo lo dice con todas las letras («may be empty for pending nodes»). Lo que
    /// faltaba era que el borde emitiera el vacío en vez de un relleno.</para>
    /// </remarks>
    private static TimelineDto ToTimelineDto(CaseTimelineEntry e) => new(
        Id: e.Id,
        Label: e.Label,
        Date: IsPending(e.State) ? null : e.Date,
        State: e.State,
        Note: e.Note);

    private static bool IsPending(string? state)
        => string.Equals(state?.Trim(), "pending", StringComparison.OrdinalIgnoreCase);

    // Resuelve el ciudadano desde las respuestas del formulario (once-only lo prellena):
    // nombre + correo + documento + teléfono si el trámite los pidió.
    /// <summary>
    /// El solicitante del expediente. <b>Con sesión, la identidad la sella el SERVIDOR</b>
    /// (gate → cookie) y se ignoran el nombre/correo del formulario; sin sesión, invitado con
    /// lo que se tecleó y sin dueño.
    /// </summary>
    /// <remarks>
    /// Anti-tampering: si el correo saliera del formulario, cualquiera radicaría a nombre de
    /// otro. Calco literal del checkout de Tienda (ADR 0103). La cédula y el teléfono SÍ
    /// siguen viniendo del formulario: son datos del trámite, no identidad.
    /// </remarks>
    private GovCitizen ResolveCitizen(IReadOnlyDictionary<string, string> answers)
    {
        answers.TryGetValue("nombreCompleto", out var name);
        answers.TryGetValue("correo", out var email);
        answers.TryGetValue("cedula", out var doc);
        answers.TryGetValue("telefono", out var phone);

        var documentId = string.IsNullOrWhiteSpace(doc) ? null : doc;
        var phoneNumber = string.IsNullOrWhiteSpace(phone) ? null : phone;

        if (_gate.IsAuthenticated && _gate.CurrentMemberKey is Guid memberKey)
        {
            return new GovCitizen(
                Name: _gate.CurrentMemberDisplayName ?? name ?? string.Empty,
                Email: _gate.CurrentMemberEmail ?? email ?? string.Empty,
                DocumentId: documentId,
                Phone: phoneNumber,
                MemberKey: memberKey);
        }

        return new GovCitizen(name ?? string.Empty, email ?? string.Empty, documentId, phoneNumber);
    }

    // La correspondencia del expediente vive en IMessagingService (contexto 'gov',
    // contextRef = radicado). Se localiza el hilo por la bandeja del ciudadano filtrando
    // el contexto — sin reconstruir el threadId. outgoing = el mensaje lo envió el
    // ciudadano (desde su perspectiva de carpeta).
    private async Task<IReadOnlyList<MessageDto>> LoadMessagesAsync(CaseDetail detail, CancellationToken cancellationToken)
    {
        var citizenEmail = detail.Citizen.Email;
        if (string.IsNullOrWhiteSpace(citizenEmail))
        {
            return Array.Empty<MessageDto>();
        }

        var wanted = $"{MessagingContext}:{detail.Radicado}";
        var inbox = await _messaging.GetInboxAsync(citizenEmail, cancellationToken);
        var summary = inbox.FirstOrDefault(t => string.Equals(t.ContextRef, wanted, StringComparison.OrdinalIgnoreCase));
        if (summary is null)
        {
            return Array.Empty<MessageDto>();
        }

        var thread = await _messaging.GetThreadAsync(summary.ThreadId, cancellationToken);
        if (thread is null)
        {
            return Array.Empty<MessageDto>();
        }

        return thread.Messages
            .Select(m => new MessageDto(
                Id: m.MessageId,
                Author: string.Equals(m.From, citizenEmail, StringComparison.OrdinalIgnoreCase) ? detail.Citizen.Name : "Entidad",
                Body: m.Body,
                CreatedAtUtc: m.SentAt,
                Outgoing: string.Equals(m.From, citizenEmail, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    // ══════════════════════ Request DTOs ══════════════════════

    public sealed record CreateApplicationRequest(
        string ServiceId,
        Dictionary<string, string>? Answers,
        NotificationConsentRequest? NotificationConsent = null);

    /// <summary>Lo que contestó sobre la notificación electrónica, con la versión del texto que leyó (009, S-7).</summary>
    public sealed record NotificationConsentRequest(bool Accepted, string? TextVersion);

    /// <summary>Las respuestas de una radicación por la puerta (009, T027): la aceptación ya va en la saga.</summary>
    public sealed record RespuestasDeLaRadicacionRequest(Dictionary<string, string>? Answers);

    /// <summary>El radicado que acuñó el CMS para la radicación, y el expediente donde quedó.</summary>
    public sealed record RespuestasDeLaRadicacionResponse(string Radicado, string CaseId);

    // (El antiguo `DocumentRequest(ApplicationId, Name)` murió con T6: adjuntar ya no es
    //  un JSON con un nombre, es un multipart con el fichero.)

    public sealed record DecisionRequest(string CaseId, string Outcome, string? Note);

    /// <summary>Tomar el expediente: la nota es opcional y el cuerpo también.</summary>
    public sealed record TakeRequest(string? Note);

    /// <summary>Subsanar: un mensaje opcional para la entidad; el cuerpo también es opcional.</summary>
    public sealed record ResubmitRequest(string? Note);

    /// <summary>
    /// Lo que el funcionario manda para poner un acto en conocimiento.
    /// </summary>
    /// <remarks>
    /// <b>No lleva destinatario</b>: sale del expediente. Dejar que el cuerpo lo nombrara
    /// permitiría notificarle a cualquiera un acto que no es suyo, y el radicado es secuencial.
    /// </remarks>
    public sealed record NotifyActRequest(
        string CaseId,
        string Title,
        string? Body,
        string? DocumentRef,
        DateTimeOffset? AcknowledgeBefore);

    // ══════════════════════ Response DTOs (camelCase por defecto) ══════════════════════

    public sealed record ServiceDto(
        string Id,
        string Name,
        string Summary,
        string Category,
        string Agency,
        int EstimatedDays,
        decimal FeeMinor,
        string Currency);

    public sealed record ServicesResponse(IReadOnlyList<ServiceDto> Services);

    public sealed record StepDto(string Id, string Title, string Detail);

    public sealed record ServiceDetailDto(
        string Id,
        string Name,
        string Summary,
        string Category,
        string Agency,
        IReadOnlyList<StepDto> Steps,
        IReadOnlyList<string> Eligibility,
        IReadOnlyList<string> Required,
        decimal FeeMinor,
        string Currency,
        string Key);

    public sealed record ServiceDetailResponse(ServiceDetailDto Service);

    public sealed record FieldOptionDto(string Value, string Label);

    public sealed record FieldDto(
        string Id,
        string Label,
        string Type,
        bool Required,
        string Help,
        IReadOnlyList<FieldOptionDto>? Options,
        string? Pattern);

    public sealed record FormSectionDto(string Id, string Title, IReadOnlyList<FieldDto> Fields);

    public sealed record FormDto(string Id, string Title, IReadOnlyList<FormSectionDto> Sections);

    public sealed record FormResponse(FormDto Form);

    /// <summary>
    /// La forma compacta de un expediente hacia la carpeta del ciudadano.
    /// </summary>
    /// <remarks>
    /// <b><see cref="FeeStatus"/> y <c>null</c></b>: ver la nota de <see cref="CaseDto"/>. Se
    /// emite SIEMPRE declarada aunque a veces sea nula, porque una clave que a veces no está
    /// deja al normalizador del otro lado poniendo su propio valor (#116).
    /// </remarks>
    public sealed record ApplicationSummaryDto(
        string Id,
        string Reference,
        string ServiceId,
        string ServiceName,
        string Status,
        DateTimeOffset SubmittedAt,
        string CurrentStage,
        long FeeMinor = 0,
        string? FeeStatus = null);

    public sealed record ApplicationResponse(ApplicationSummaryDto Application);

    public sealed record ApplicationsResponse(IReadOnlyList<ApplicationSummaryDto> Applications);

    /// <summary>
    /// Un hito del expediente. <see cref="Date"/> es <c>null</c> cuando el hito todavía no
    /// ocurrió — ver <c>ToTimelineDto</c>: una fecha de relleno en un hito pendiente se lee
    /// como un hecho, y acá los hechos con fecha sostienen términos.
    /// </summary>
    public sealed record TimelineDto(string Id, string Label, DateTimeOffset? Date, string State, string Note);

    public sealed record DocumentDto(
        string Id,
        string Name,
        string Status,
        DateTimeOffset UploadedAt,
        string? ContentType = null,
        long SizeBytes = 0,
        string? DownloadUrl = null);

    public sealed record MessageDto(string Id, string Author, string Body, DateTimeOffset CreatedAtUtc, bool Outgoing);

    /// <summary>El expediente hacia su dueño.</summary>
    /// <param name="Actions">Lo que el proceso le permite al ciudadano desde el estado (hoy, subsanar en
    /// «información solicitada»), con los nombres de cable del proceso (D-3 de 009).</param>
    /// <param name="Stale"><c>true</c> si el proceso no contestó: no hay acciones ahora, y no porque no
    /// haya nada que hacer.</param>
    public sealed record ApplicationDetailDto(
        string Id,
        string Reference,
        string ServiceId,
        string ServiceName,
        string Status,
        DateTimeOffset SubmittedAt,
        string CurrentStage,
        IReadOnlyList<TimelineDto> Timeline,
        IReadOnlyList<DocumentDto> Documents,
        IReadOnlyList<MessageDto> Messages,
        IReadOnlyList<string> Actions,
        long FeeMinor = 0,
        string? FeeStatus = null,
        bool Stale = false);

    public sealed record ApplicationDetailResponse(ApplicationDetailDto Application);

    public sealed record DocumentResponse(DocumentDto Document);

    public sealed record QueueCaseDto(
        string Id,
        string Reference,
        string ServiceName,
        string CitizenName,
        string Status,
        DateTimeOffset SubmittedAt,
        string Priority,
        int SlaDaysLeft,
        long FeeMinor = 0,
        string? FeeStatus = null);

    public sealed record QueueResponse(IReadOnlyList<QueueCaseDto> Cases);

    public sealed record CaseApplicationDto(
        string Id,
        string Reference,
        string ServiceName,
        string CitizenName,
        string Status,
        DateTimeOffset SubmittedAt,
        string CurrentStage,
        long FeeMinor = 0,
        string? FeeStatus = null);

    public sealed record AnswerDto(string Label, string Value);

    public sealed record DecisionDto(string Outcome, string Note, DateTimeOffset DecidedAtUtc, string DecidedBy);

    /// <summary>
    /// El expediente entero hacia la cara del funcionario.
    /// </summary>
    /// <remarks>
    /// <para><b>El estado de la tasa (<c>application.feeStatus</c>) se emite desde #116.</b> El
    /// expediente lo guardaba desde la ADR 0116 fase 5 y ninguna superficie lo devolvía, así
    /// que un cobro que no salió quedaba escrito donde no mira nadie — una escritura sin camino
    /// de lectura. Con el motor en proceso daba igual (decía siempre <c>Captured</c>); con
    /// <c>Api.Payments</c> detrás, <c>unavailable</c> es el caso que hay que perseguir.</para>
    /// <para><b><c>null</c> es «no consta» y no «cobrada»</b>, y se lee junto a <c>feeMinor</c>:
    /// sin tasa no hay cobro que tener estado; con tasa y sin estado, el expediente es anterior
    /// al campo. Los dos casos son verdad y ninguno se rellena.</para>
    /// <para><b><c>actions</c> es lo que el proceso permite al funcionario desde el estado</b> (D-3 de
    /// 009): la consola pinta sólo eso. <c>stale</c> dice que el proceso no contestó y por eso no
    /// hay acciones ahora.</para>
    /// <para><b><c>notificationConsent</c></b> (<c>accepted | declined | unknown</c>, S-7): si el
    /// ciudadano aceptó la notificación electrónica al radicar. Sin aceptación no se le notifica
    /// por aquí, y la consola no lo ofrece.</para>
    /// </remarks>
    public sealed record CaseDto(
        CaseApplicationDto Application,
        IReadOnlyList<AnswerDto> Answers,
        IReadOnlyList<DocumentDto> Documents,
        IReadOnlyList<TimelineDto> Timeline,
        IReadOnlyList<ActNotificationDto> Notifications,
        DecisionDto? Decision,
        IReadOnlyList<string> Actions,
        bool Stale = false,
        string NotificationConsent = NotificationConsentSlugs.Unknown);

    public sealed record CaseResponse(CaseDto Case);

    public sealed record ActNotificationDto(
        string Id,
        string CaseId,
        string Reference,
        string Title,
        string? Body,
        string? DocumentRef,
        DateTimeOffset NotifiedAt,
        DateTimeOffset? AcknowledgeBefore,
        DateTimeOffset? OpenedAt,
        string? OpenedWith,
        bool Opened);

    public sealed record ActNotificationResponse(ActNotificationDto Notification);

    public sealed record ActNotificationsResponse(IReadOnlyList<ActNotificationDto> Notifications);
}
