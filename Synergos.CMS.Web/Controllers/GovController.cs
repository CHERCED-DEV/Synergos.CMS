using Microsoft.AspNetCore.Mvc;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;

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
/// <item><see cref="ICaseWorkflowService"/> — decisión del funcionario (approve/reject/
///   request-info) → máquina de estados auditada (<see cref="IAuditTrailWriter"/>).</item>
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

    public GovController(
        ITramiteCatalogProvider catalog,
        IApplicationService applications,
        ICaseWorkflowService workflow,
        ICaseTrackingProvider tracking,
        IDocumentUploadService documents,
        IPrivateFileStore files,
        IMessagingService messaging,
        IMemberAccessGate gate)
    {
        _catalog = catalog;
        _applications = applications;
        _workflow = workflow;
        _tracking = tracking;
        _documents = documents;
        _files = files;
        _messaging = messaging;
        _gate = gate;
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
            FeeMinor: s.FeeMinor,
            Currency: s.Currency)));
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
    // POST /api/gov/application { serviceId, answers } → { application:{...} }
    [HttpPost("application")]
    public async Task<IActionResult> CreateApplication(
        [FromBody] CreateApplicationRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.ServiceId))
        {
            return BadRequest(new { error = "serviceId es requerido." });
        }

        var answers = request.Answers ?? new Dictionary<string, string>();
        var citizen = ResolveCitizen(answers);

        RadicarResult result;
        try
        {
            result = await _applications.RadicarAsync(request.ServiceId.Trim(), answers, citizen, cancellationToken);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        return Ok(new ApplicationResponse(ToApplicationSummaryDto(result.Case)));
    }

    // ── 5. Mis solicitudes (carpeta del ciudadano) ─────────────────────
    // GET /api/gov/applications?citizen= → { applications:[...] }
    // El ?citizen=<email> DESAPARECE: era el IDOR. La bandeja es la del member de la sesión.
    [HttpGet("applications")]
    public async Task<IActionResult> Applications(CancellationToken cancellationToken)
    {
        var (denied, actorKey) = RequireMember();
        if (denied is not null) { return denied; }

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

        var detail = await _tracking.GetCaseAsync(id ?? string.Empty, cancellationToken);
        if (detail is null)
        {
            return NotFound(new { error = $"Solicitud '{id}' no encontrada." });
        }

        var foreign = DenyIfForeignCase(detail, actorKey);
        if (foreign is not null) { return foreign; }

        var messages = await LoadMessagesAsync(detail, cancellationToken);
        return Ok(new ApplicationDetailResponse(new ApplicationDetailDto(
            Id: detail.CaseId,
            Reference: detail.Radicado,
            ServiceId: detail.TramiteId,
            ServiceName: detail.TramiteName,
            Status: GovStatusSlugs.ToSlug(detail.Status),
            SubmittedAt: detail.RadicadoAt,
            CurrentStage: detail.CurrentStage,
            Timeline: detail.Timeline.Select(ToTimelineDto).ToList(),
            Documents: detail.Documents.Select(ToDocumentDto).ToList(),
            Messages: messages)));
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
            return NotFound(new { error = ex.Message });
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
            updated = await _workflow.DecideAsync(request.CaseId.Trim(), request.Outcome.Trim(), request.Note ?? string.Empty, cancellationToken);
        }
        catch (ArgumentException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }

        return Ok(new CaseResponse(await ToCaseDtoAsync(updated, cancellationToken)));
    }

    // ══════════════════════ Mappers ══════════════════════

    private ServiceDto ToServiceDto(TramiteSummary t) => new(
        Id: t.Id,
        Name: t.Name,
        Summary: t.Summary,
        Category: t.Category,
        Agency: t.Agency,
        EstimatedDays: t.EstimatedDays,
        FeeMinor: t.FeeMinor,
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

    private ApplicationSummaryDto ToApplicationSummaryDto(CaseDetail c) => new(
        Id: c.CaseId,
        Reference: c.Radicado,
        ServiceId: c.TramiteId,
        ServiceName: c.TramiteName,
        Status: GovStatusSlugs.ToSlug(c.Status),
        SubmittedAt: c.RadicadoAt,
        CurrentStage: c.CurrentStage);

    private static ApplicationSummaryDto ToApplicationSummaryFromInbox(CaseInboxItem i) => new(
        Id: i.CaseId,
        Reference: i.Radicado,
        ServiceId: string.Empty, // la lista de la carpeta no necesita el serviceId
        ServiceName: i.TramiteName,
        Status: GovStatusSlugs.ToSlug(i.Status),
        SubmittedAt: i.RadicadoAt,
        CurrentStage: i.CurrentStage);

    private static QueueCaseDto ToQueueDto(CaseInboxItem i) => new(
        Id: i.CaseId,
        Reference: i.Radicado,
        ServiceName: i.TramiteName,
        CitizenName: i.CitizenName,
        Status: GovStatusSlugs.ToSlug(i.Status),
        SubmittedAt: i.RadicadoAt,
        Priority: i.Priority.ToString().ToLowerInvariant(),
        SlaDaysLeft: i.SlaDaysLeft);

    // El detalle del funcionario muestra las respuestas con su ETIQUETA humana (no el
    // fieldId), resolviéndolas contra la definición del formulario del trámite. Si el
    // trámite ya no existe en el catálogo, cae al fieldId como etiqueta.
    private async Task<CaseDto> ToCaseDtoAsync(CaseDetail c, CancellationToken cancellationToken)
    {
        var labels = await ResolveFieldLabelsAsync(c.TramiteId, cancellationToken);
        return new CaseDto(
            Application: new CaseApplicationDto(
                Id: c.CaseId,
                Reference: c.Radicado,
                ServiceName: c.TramiteName,
                CitizenName: c.Citizen.Name,
                Status: GovStatusSlugs.ToSlug(c.Status),
                SubmittedAt: c.RadicadoAt,
                CurrentStage: c.CurrentStage),
            Answers: c.FormData
                .Select(kv => new AnswerDto(labels.GetValueOrDefault(kv.Key, kv.Key), kv.Value))
                .ToList(),
            Documents: c.Documents.Select(ToDocumentDto).ToList(),
            Timeline: c.Timeline.Select(ToTimelineDto).ToList(),
            Decision: c.Decision is null
                ? null
                : new DecisionDto(c.Decision.Outcome, c.Decision.Note, c.Decision.DecidedAt, c.Decision.DecidedBy));
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

    private static TimelineDto ToTimelineDto(CaseTimelineEntry e) => new(
        Id: e.Id,
        Label: e.Label,
        Date: e.Date,
        State: e.State,
        Note: e.Note);

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

    public sealed record CreateApplicationRequest(string ServiceId, Dictionary<string, string>? Answers);

    // (El antiguo `DocumentRequest(ApplicationId, Name)` murió con T6: adjuntar ya no es
    //  un JSON con un nombre, es un multipart con el fichero.)

    public sealed record DecisionRequest(string CaseId, string Outcome, string? Note);

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
        string Currency);

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

    public sealed record ApplicationSummaryDto(
        string Id,
        string Reference,
        string ServiceId,
        string ServiceName,
        string Status,
        DateTimeOffset SubmittedAt,
        string CurrentStage);

    public sealed record ApplicationResponse(ApplicationSummaryDto Application);

    public sealed record ApplicationsResponse(IReadOnlyList<ApplicationSummaryDto> Applications);

    public sealed record TimelineDto(string Id, string Label, DateTimeOffset Date, string State, string Note);

    public sealed record DocumentDto(
        string Id,
        string Name,
        string Status,
        DateTimeOffset UploadedAt,
        string? ContentType = null,
        long SizeBytes = 0,
        string? DownloadUrl = null);

    public sealed record MessageDto(string Id, string Author, string Body, DateTimeOffset CreatedAtUtc, bool Outgoing);

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
        IReadOnlyList<MessageDto> Messages);

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
        int SlaDaysLeft);

    public sealed record QueueResponse(IReadOnlyList<QueueCaseDto> Cases);

    public sealed record CaseApplicationDto(
        string Id,
        string Reference,
        string ServiceName,
        string CitizenName,
        string Status,
        DateTimeOffset SubmittedAt,
        string CurrentStage);

    public sealed record AnswerDto(string Label, string Value);

    public sealed record DecisionDto(string Outcome, string Note, DateTimeOffset DecidedAtUtc, string DecidedBy);

    public sealed record CaseDto(
        CaseApplicationDto Application,
        IReadOnlyList<AnswerDto> Answers,
        IReadOnlyList<DocumentDto> Documents,
        IReadOnlyList<TimelineDto> Timeline,
        DecisionDto? Decision);

    public sealed record CaseResponse(CaseDto Case);
}
