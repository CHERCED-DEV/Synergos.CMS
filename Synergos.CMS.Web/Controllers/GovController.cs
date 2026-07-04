using Microsoft.AspNetCore.Mvc;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Controllers;

/// <summary>
/// API JSON del vertical <strong>Gobierno / Trámites</strong> (el +1 del doc 19 —
/// portal de servicios al ciudadano, doc gobierno-app-spec; rescate D7 doc 21). La
/// consume el módulo Angular <c>module-gov-services</c>: entrar al dominio = caer
/// directo en la app real (catálogo de trámites → ficha → radicar → seguimiento del
/// expediente, + cola del funcionario con máquina de estados auditada).
/// </summary>
/// <remarks>
/// La capa Web SOLO orquesta y mapea a DTOs JSON estables — toda la lógica vive en
/// los seams (Application, sin Umbraco — ADR 0002), reusando el MOTOR:
/// <list type="bullet">
/// <item><see cref="ITramiteCatalogProvider"/> — catálogo buscable + ficha con el
///   formulario dinámico data-driven (patrón GOV.UK task-list).</item>
/// <item><see cref="IApplicationService"/> — radicar = crear el expediente; la tasa
///   es un pago OPCIONAL (<see cref="IPaymentProvider"/> solo si Fee &gt; 0).</item>
/// <item><see cref="ICaseWorkflowService"/> — máquina de estados del expediente
///   (radicado→en-revisión→subsanación→resuelto/rechazado); cada transición se audita
///   append-only (<see cref="IAuditTrailWriter"/> — el diferenciador del dominio).</item>
/// <item><see cref="ICaseTrackingProvider"/> — seguimiento + bandejas (ciudadano ve
///   los suyos / funcionario ve la cola).</item>
/// <item><see cref="IDocumentUploadService"/> — adjuntar documentos (metadata).</item>
/// </list>
/// El precio se formatea es-CO vía <see cref="IPriceFormatter"/>. Contrato (lo
/// programa el agente UI): <c>GET tramites · GET tramite/{id} · POST application ·
/// POST application/{id}/documents · GET case/{id} · GET inbox ·
/// POST case/{id}/transition</c>.
/// </remarks>
[ApiController]
[Route("api/gov")]
public sealed class GovController : ControllerBase
{
    private readonly ITramiteCatalogProvider _catalog;
    private readonly IApplicationService _applications;
    private readonly ICaseWorkflowService _workflow;
    private readonly ICaseTrackingProvider _tracking;
    private readonly IDocumentUploadService _documents;
    private readonly IGovFeeCalculator _fees;
    private readonly IPriceFormatter _priceFormatter;

    public GovController(
        ITramiteCatalogProvider catalog,
        IApplicationService applications,
        ICaseWorkflowService workflow,
        ICaseTrackingProvider tracking,
        IDocumentUploadService documents,
        IGovFeeCalculator fees,
        IPriceFormatter priceFormatter)
    {
        _catalog = catalog;
        _applications = applications;
        _workflow = workflow;
        _tracking = tracking;
        _documents = documents;
        _fees = fees;
        _priceFormatter = priceFormatter;
    }

    // ── 1. Catálogo de trámites (home del dominio) ──────────────────────
    // GET /api/gov/tramites?q=&category= → { tramites:[...] }
    [HttpGet("tramites")]
    public async Task<IActionResult> Tramites(
        [FromQuery] string? q,
        [FromQuery] string? category,
        CancellationToken cancellationToken)
    {
        var tramites = await _catalog.SearchAsync(q, category, cancellationToken);
        return Ok(new TramitesResponse(tramites.Select(ToTramiteDto).ToList()));
    }

    // ── 2. Ficha del trámite (form definition data-driven + requisitos + tasa) ──
    // GET /api/gov/tramite/{id} → { tramite, formDefinition, requirements, fee }
    [HttpGet("tramite/{id}")]
    public async Task<IActionResult> Tramite(string id, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return BadRequest(new { error = "El id del trámite es requerido." });
        }

        var detail = await _catalog.GetAsync(id, cancellationToken);
        if (detail is null)
        {
            return NotFound(new { error = $"Trámite '{id}' no encontrado." });
        }

        var quote = _fees.Calculate(detail.Summary.Id, detail.Fee, detail.Currency);
        return Ok(new TramiteDetailResponse(
            Tramite: new TramiteDetailDto(
                ToTramiteDto(detail.Summary),
                detail.Description,
                detail.EligibilityText,
                detail.Normativa,
                detail.RequiresAppointment),
            FormDefinition: new FormDefinitionDto(
                detail.FormDefinition.Fields
                    .Select(f => new FormFieldDto(f.Key, f.Label, f.Type, f.Required, f.Options))
                    .ToList()),
            Requirements: detail.Requirements,
            Fee: ToFeeDto(quote)));
    }

    // ── 3. Radicar solicitud (crea el expediente; tasa = pago opcional) ─
    // POST /api/gov/application { tramiteId, formData, citizen } → { caseId, radicado, fee? }
    [HttpPost("application")]
    public async Task<IActionResult> Application(
        [FromBody] ApplicationRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.TramiteId))
        {
            return BadRequest(new { error = "tramiteId es requerido." });
        }
        if (request.Citizen is null)
        {
            return BadRequest(new { error = "citizen es requerido." });
        }

        RadicarResult result;
        try
        {
            result = await _applications.RadicarAsync(
                request.TramiteId.Trim(),
                request.FormData ?? new Dictionary<string, string>(),
                new GovCitizen(
                    request.Citizen.Name,
                    request.Citizen.Email,
                    request.Citizen.DocumentId,
                    request.Citizen.Phone),
                cancellationToken);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        // fee solo viaja cuando el trámite cobra (tasa opcional — Fee 0 = gratis).
        var fee = result.Fee > 0m
            ? new ApplicationFeeDto(
                result.Fee,
                result.Currency,
                _priceFormatter.Format(result.Fee, result.Currency),
                result.PaymentSessionId)
            : null;

        return Ok(new ApplicationResponse(result.CaseId, result.Radicado, fee));
    }

    // ── 4. Adjuntar documentos al expediente (metadata + auditoría) ─────
    // POST /api/gov/application/{id}/documents { files:[...] } → { uploaded:[...] }
    [HttpPost("application/{id}/documents")]
    public async Task<IActionResult> Documents(
        string id,
        [FromBody] DocumentsRequest? request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return BadRequest(new { error = "El id del expediente es requerido." });
        }
        if (request?.Files is null || request.Files.Count == 0)
        {
            return BadRequest(new { error = "files es requerido (al menos un archivo)." });
        }

        DocumentUploadResult result;
        try
        {
            result = await _documents.UploadAsync(
                id.Trim(),
                request.Files
                    .Select(f => new CitizenDocumentUpload(f.FileName, f.ContentType, f.SizeBytes))
                    .ToList(),
                cancellationToken);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        return Ok(new DocumentsResponse(result.Uploaded.Select(ToDocumentDto).ToList()));
    }

    // ── 5. Seguimiento del expediente (estado + timeline) ───────────────
    // GET /api/gov/case/{id} → { case, status, timeline }
    [HttpGet("case/{id}")]
    public async Task<IActionResult> Case(string id, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return BadRequest(new { error = "El id del expediente es requerido." });
        }

        var detail = await _tracking.GetCaseAsync(id, cancellationToken);
        if (detail is null)
        {
            return NotFound(new { error = $"Expediente '{id}' no encontrado." });
        }

        return Ok(new CaseResponse(
            Case: ToCaseDto(detail),
            Status: ToStatusSlug(detail.Status),
            Timeline: detail.Timeline.Select(ToTimelineDto).ToList()));
    }

    // ── 6. Bandeja (ciudadano ve los suyos / funcionario ve la cola) ────
    // GET /api/gov/inbox?actor=&role= → { cases:[...] }
    [HttpGet("inbox")]
    public async Task<IActionResult> Inbox(
        [FromQuery] string? actor,
        [FromQuery] string? role,
        CancellationToken cancellationToken)
    {
        var items = await _tracking.GetInboxAsync(actor ?? string.Empty, role ?? "citizen", cancellationToken);
        return Ok(new InboxResponse(items.Select(ToInboxDto).ToList()));
    }

    // ── 7. Transición del expediente (máquina de estados auditada) ──────
    // POST /api/gov/case/{id}/transition { to, note } → { status }
    [HttpPost("case/{id}/transition")]
    public async Task<IActionResult> Transition(
        string id,
        [FromBody] TransitionRequest? request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return BadRequest(new { error = "El id del expediente es requerido." });
        }
        if (request is null || !TryParseStatus(request.To, out var to))
        {
            return BadRequest(new
            {
                error = "to es requerido: radicado | en-revision | subsanacion | resuelto | rechazado.",
            });
        }

        CaseTransitionResult result;
        try
        {
            result = await _workflow.TransitionAsync(id.Trim(), to, request.Note ?? string.Empty, cancellationToken);
        }
        catch (ArgumentException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }

        return Ok(new TransitionResponse(ToStatusSlug(result.Status)));
    }

    // ── Mappers a DTOs JSON estables ────────────────────────────────────

    private TramiteDto ToTramiteDto(TramiteSummary t) => new(
        Id: t.Id,
        Slug: t.Slug,
        Name: t.Name,
        Entity: t.Entity,
        Category: t.Category,
        Channel: t.Channel,
        EstimatedTime: t.EstimatedTime,
        Fee: t.Fee,
        FeeFormatted: t.Fee > 0m ? _priceFormatter.Format(t.Fee, t.Currency) : "Gratis",
        Currency: t.Currency);

    private FeeDto ToFeeDto(GovFeeQuote quote) => new(
        Amount: quote.Amount,
        Currency: quote.Currency,
        Formatted: quote.Exempt ? "Gratis" : _priceFormatter.Format(quote.Amount, quote.Currency),
        Exempt: quote.Exempt);

    private CaseDto ToCaseDto(CaseDetail c) => new(
        CaseId: c.CaseId,
        Radicado: c.Radicado,
        TramiteId: c.TramiteId,
        TramiteName: c.TramiteName,
        Citizen: new CitizenDto(c.Citizen.Name, c.Citizen.Email, c.Citizen.DocumentId, c.Citizen.Phone),
        FormData: c.FormData,
        Documents: c.Documents.Select(ToDocumentDto).ToList(),
        Fee: c.Fee,
        FeeFormatted: c.Fee > 0m ? _priceFormatter.Format(c.Fee, c.Currency) : "Gratis",
        Currency: c.Currency,
        RadicadoAt: c.RadicadoAt);

    private static DocumentDto ToDocumentDto(CitizenDocumentRef d) => new(
        Id: d.Id,
        FileName: d.FileName,
        ContentType: d.ContentType,
        SizeBytes: d.SizeBytes,
        ValidationStatus: d.ValidationStatus,
        SecureUrl: d.SecureUrl);

    private static TimelineEntryDto ToTimelineDto(CaseTimelineEntry e) => new(
        Status: ToStatusSlug(e.Status),
        OccurredAt: e.OccurredAt,
        Actor: e.Actor,
        Note: e.Note);

    private static InboxItemDto ToInboxDto(CaseInboxItem i) => new(
        CaseId: i.CaseId,
        Radicado: i.Radicado,
        TramiteName: i.TramiteName,
        CitizenName: i.CitizenName,
        Status: ToStatusSlug(i.Status),
        RadicadoAt: i.RadicadoAt);

    // Estados del expediente como slugs kebab estables para la UI (status tags).
    private static string ToStatusSlug(CaseStatus status) => status switch
    {
        CaseStatus.Radicado => "radicado",
        CaseStatus.EnRevision => "en-revision",
        CaseStatus.Subsanacion => "subsanacion",
        CaseStatus.Resuelto => "resuelto",
        CaseStatus.Rechazado => "rechazado",
        _ => status.ToString().ToLowerInvariant(),
    };

    private static bool TryParseStatus(string? value, out CaseStatus status)
    {
        switch (value?.Trim().ToLowerInvariant().Replace("-", string.Empty))
        {
            case "radicado":
                status = CaseStatus.Radicado;
                return true;
            case "enrevision":
                status = CaseStatus.EnRevision;
                return true;
            case "subsanacion":
                status = CaseStatus.Subsanacion;
                return true;
            case "resuelto":
                status = CaseStatus.Resuelto;
                return true;
            case "rechazado":
                status = CaseStatus.Rechazado;
                return true;
            default:
                status = default;
                return false;
        }
    }

    // ── Request DTOs (binding del módulo Angular) ───────────────────────

    public sealed record CitizenRequest(string Name, string Email, string? DocumentId, string? Phone);

    public sealed record ApplicationRequest(
        string TramiteId,
        Dictionary<string, string>? FormData,
        CitizenRequest Citizen);

    public sealed record FileRequest(string FileName, string ContentType, long SizeBytes);

    public sealed record DocumentsRequest(List<FileRequest> Files);

    public sealed record TransitionRequest(string To, string? Note);

    // ── Response DTOs (JSON estable para la UI) ─────────────────────────

    public sealed record TramiteDto(
        string Id,
        string Slug,
        string Name,
        string Entity,
        string Category,
        string Channel,
        string EstimatedTime,
        decimal Fee,
        string FeeFormatted,
        string Currency);

    public sealed record TramitesResponse(IReadOnlyList<TramiteDto> Tramites);

    public sealed record TramiteDetailDto(
        TramiteDto Summary,
        string Description,
        string EligibilityText,
        string Normativa,
        bool RequiresAppointment);

    public sealed record FormFieldDto(
        string Key,
        string Label,
        string Type,
        bool Required,
        IReadOnlyList<string> Options);

    public sealed record FormDefinitionDto(IReadOnlyList<FormFieldDto> Fields);

    public sealed record FeeDto(decimal Amount, string Currency, string Formatted, bool Exempt);

    public sealed record TramiteDetailResponse(
        TramiteDetailDto Tramite,
        FormDefinitionDto FormDefinition,
        IReadOnlyList<string> Requirements,
        FeeDto Fee);

    public sealed record ApplicationFeeDto(
        decimal Amount,
        string Currency,
        string Formatted,
        string? PaymentSessionId);

    public sealed record ApplicationResponse(string CaseId, string Radicado, ApplicationFeeDto? Fee);

    public sealed record DocumentDto(
        string Id,
        string FileName,
        string ContentType,
        long SizeBytes,
        string ValidationStatus,
        string SecureUrl);

    public sealed record DocumentsResponse(IReadOnlyList<DocumentDto> Uploaded);

    public sealed record CitizenDto(string Name, string Email, string? DocumentId, string? Phone);

    public sealed record CaseDto(
        string CaseId,
        string Radicado,
        string TramiteId,
        string TramiteName,
        CitizenDto Citizen,
        IReadOnlyDictionary<string, string> FormData,
        IReadOnlyList<DocumentDto> Documents,
        decimal Fee,
        string FeeFormatted,
        string Currency,
        DateTimeOffset RadicadoAt);

    public sealed record TimelineEntryDto(
        string Status,
        DateTimeOffset OccurredAt,
        string Actor,
        string Note);

    public sealed record CaseResponse(
        CaseDto Case,
        string Status,
        IReadOnlyList<TimelineEntryDto> Timeline);

    public sealed record InboxItemDto(
        string CaseId,
        string Radicado,
        string TramiteName,
        string CitizenName,
        string Status,
        DateTimeOffset RadicadoAt);

    public sealed record InboxResponse(IReadOnlyList<InboxItemDto> Cases);

    public sealed record TransitionResponse(string Status);
}
