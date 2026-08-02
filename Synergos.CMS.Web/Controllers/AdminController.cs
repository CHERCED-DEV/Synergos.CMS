using System.Buffers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Filters;

namespace Synergos.CMS.Web.Controllers;

/// <summary>
/// Dashboard de operaciones SSR para los miembros con role
/// <c>admin</c> / <c>moderator</c> / <c>editor</c>. Consume los seams
/// de runtime (`ICommentRepository` + futuros) directamente — alternativa
/// al backoffice section AngularJS deferido (Ola 78).
/// </summary>
/// <remarks>
/// Layout=null para evitar dependencia de PublishedRequest (AdminController
/// es MVC puro, no template Umbraco). Cada view carga el bundle propio
/// via partial <c>_AdminHead</c>.
///
/// <b>Auth gating: DECLARATIVO</b> vía <see cref="RequireRolesAttribute"/> con CSV
/// <c>"admin,moderator,editor"</c> (<see cref="ModeratorRolesCsv"/>). Antes cada una de las 29
/// actions repetía el mismo <c>if (!_gate.HasAnyRole(...)) return Forbid();</c>. Ninguna lo
/// olvidaba, pero el default era abierto: una action nueva nacía pública si el autor no copiaba
/// cuatro líneas. Ahora nace protegida, y abrirla exige un <c>[AllowAnonymous]</c> explícito
/// EN EL MÉTODO — visible en el diff (auditoría F2).
///
/// <para>El <c>[AllowAnonymous]</c> de la CLASE se conserva y no contradice lo anterior: sirve
/// para saltarse el pipeline de auth de Umbraco y que este controller resuelva el permiso él
/// mismo. Por eso el filtro mira solo los atributos del método.</para>
///
/// <para>Sin antiforgery — los forms POST son member-authenticated y el riesgo de CSRF se
/// consideró bajo en este flujo editorial. Es una decisión aparte, anotada en el backlog de la
/// auditoría.</para>
/// </remarks>
[Route("admin")]
[AllowAnonymous]
[RequireRoles(ModeratorRolesCsv)]
public sealed class AdminController : Controller
{
    private const string ModeratorRolesCsv = "admin,moderator,editor";
    private const string PendingCountCacheKey = "admin.pending-comments-count";
    private const string BulkUndoCacheKeyPrefix = "admin.bulk-undo:";

    private static readonly SearchValues<char> CsvSpecialChars =
        SearchValues.Create(",\"\n\r");

    private readonly ICommentRepository _comments;
    private readonly IMemberAccessGate _gate;
    private readonly IAnalyticsTracker _analytics;
    private readonly ISearchAnalyticsStore _searchAnalytics;
    private readonly IFormSubmissionReader _formReader;
    private readonly IMemberRosterReader _memberRoster;
    private readonly IMemberRosterWriter _memberRosterWriter;
    private readonly IMemberTwoFactorService _memberTwoFactor;
    private readonly IGdprRtbfCoordinator _gdpr;
    private readonly IAuditTrailWriter _audit;
    private readonly IMemoryCache _cache;
    private readonly IOptionsMonitor<AdminSettings> _adminSettings;

    public AdminController(
        ICommentRepository comments,
        IMemberAccessGate gate,
        IAnalyticsTracker analytics,
        ISearchAnalyticsStore searchAnalytics,
        IFormSubmissionReader formReader,
        IMemberRosterReader memberRoster,
        IMemberRosterWriter memberRosterWriter,
        IMemberTwoFactorService memberTwoFactor,
        IGdprRtbfCoordinator gdpr,
        IAuditTrailWriter audit,
        IMemoryCache cache,
        IOptionsMonitor<AdminSettings> adminSettings)
    {
        _comments = comments;
        _gate = gate;
        _analytics = analytics;
        _searchAnalytics = searchAnalytics;
        _formReader = formReader;
        _memberRoster = memberRoster;
        _memberRosterWriter = memberRosterWriter;
        _memberTwoFactor = memberTwoFactor;
        _gdpr = gdpr;
        _audit = audit;
        _cache = cache;
        _adminSettings = adminSettings;
    }

    private async Task EmitAuditAsync(
        string action,
        string resource,
        string outcome,
        string detail = "",
        CancellationToken cancellationToken = default)
    {
        var actorEmail = _gate.CurrentMemberEmail ?? string.Empty;
        var actorName = _gate.CurrentMemberDisplayName ?? actorEmail;
        var evt = new AuditEvent(
            Id: Guid.NewGuid().ToString("N"),
            OccurredAtUtc: DateTime.UtcNow,
            ActorEmail: actorEmail,
            ActorName: actorName,
            Action: action,
            Resource: resource,
            Outcome: outcome,
            Detail: detail);
        await _audit.WriteAsync(evt, cancellationToken);
    }

    private int DefaultPageSize => _adminSettings.CurrentValue.DefaultPageSize;
    private TimeSpan PendingCountCacheTtl => _adminSettings.CurrentValue.PendingCountCacheTtl;
    private TimeSpan BulkUndoWindow => _adminSettings.CurrentValue.BulkUndoWindow;
    private int CsvExportHardCap => _adminSettings.CurrentValue.CsvExportHardCap;

    [HttpGet("")]
    public IActionResult Index()
    {
        var pendingPage = _comments.GetPendingPage(page: 1, pageSize: 1);
        var formKeys = _formReader.ListFormKeys();
        var topQueries7d = _searchAnalytics.GetTopQueries(
            DateTime.UtcNow.AddDays(-7), DateTime.UtcNow, 5);

        SetTopbar("home", pendingPage.TotalCount);
        ViewData["PendingCount"] = pendingPage.TotalCount;
        ViewData["FormKeys"] = formKeys;
        ViewData["TopQueries7d"] = topQueries7d;
        return View();
    }

    /// <summary>
    /// Helper que setea las 3 viewdata keys que el partial _AdminTopbar
    /// necesita: section slug, moderator display name, pending counter.
    ///
    /// El pending counter:
    /// - Si caller pasa <paramref name="pendingCountOverride"/> con un
    ///   valor recién leído (Moderation list, Index landing), se usa
    ///   directo y se REFRESCA el cache.
    /// - Si no, se sirve del IMemoryCache con TTL 30s para evitar que
    ///   cada page hit en el admin haga un filesystem enumeration
    ///   (Ola 122).
    /// </summary>
    private void SetTopbar(string sectionSlug, int? pendingCountOverride = null)
    {
        ViewData["AdminCurrentSection"] = sectionSlug;
        ViewData["ModeratorName"] = _gate.CurrentMemberDisplayName ?? "—";

        int pendingCount;
        if (pendingCountOverride.HasValue)
        {
            pendingCount = pendingCountOverride.Value;
            _cache.Set(PendingCountCacheKey, pendingCount, PendingCountCacheTtl);
        }
        else
        {
            pendingCount = _cache.GetOrCreate(PendingCountCacheKey, entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = PendingCountCacheTtl;
                return _comments.GetPendingPage(1, 1).TotalCount;
            });
        }
        ViewData["AdminPendingCount"] = pendingCount;
    }

    [HttpGet("forms")]
    public IActionResult FormSubmissions(
        [FromQuery] int page = 1,
        [FromQuery(Name = "pageSize")] int pageSize = 0,
        [FromQuery(Name = "formKey")] string? formKeyFilter = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null)
    {
        if (pageSize <= 0) pageSize = DefaultPageSize;
        var fromUtc = from?.ToUniversalTime();
        var toUtc = to?.ToUniversalTime();
        var pageData = _formReader.GetRecent(page, pageSize, formKeyFilter, fromUtc, toUtc);
        var formKeys = _formReader.ListFormKeys();

        SetTopbar("forms");
        ViewData["Page"] = pageData;
        ViewData["FormKeyFilter"] = formKeyFilter;
        ViewData["FromUtc"] = fromUtc;
        ViewData["ToUtc"] = toUtc;
        ViewData["FormKeys"] = formKeys;
        return View();
    }

    /// <summary>
    /// GET /admin/forms/export?formKey=X&amp;from=2026-04-01&amp;to=2026-04-30&amp;limit=500
    /// — descarga las submissions del scope como CSV. Default limit 500,
    /// hard cap 5000 para evitar timeout/OOM. <c>from</c>/<c>to</c>
    /// opcionales filtran por <c>ReceivedAtUtc</c> en la ventana
    /// indicada (ambos inclusive).
    /// </summary>
    [HttpGet("forms/export")]
    public async Task<IActionResult> ExportFormSubmissions(
        [FromQuery(Name = "formKey")] string? formKeyFilter = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] int limit = 500,
        CancellationToken cancellationToken = default)
    {
        var clamped = Math.Clamp(limit, 1, CsvExportHardCap);
        var fromUtc = from?.ToUniversalTime();
        var toUtc = to?.ToUniversalTime();
        var listingPage = _formReader.GetRecent(
            page: 1,
            pageSize: clamped,
            formKeyFilter,
            fromUtc,
            toUtc);

        // Pre-pass: recolecta union de columnas para el header. No carga
        // los Fields completos en memoria — solo los keys.
        var unionKeys = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in listingPage.Items)
        {
            var detail = _formReader.GetSubmission(item.FormKey, item.StorageId);
            if (detail is null) continue;
            foreach (var k in detail.Fields.Keys) unionKeys.Add(k);
        }

        var fileName = string.IsNullOrWhiteSpace(formKeyFilter)
            ? $"submissions_{DateTime.UtcNow:yyyyMMdd_HHmm}.csv"
            : $"submissions_{formKeyFilter}_{DateTime.UtcNow:yyyyMMdd_HHmm}.csv";

        // Streaming response: BOM + header + 1 row at a time, flush
        // tras cada batch para que el browser empiece a recibir bytes
        // inmediatamente (memoria O(1) en lugar de O(N) del StringBuilder).
        Response.Headers.ContentDisposition = $"attachment; filename=\"{fileName}\"";
        Response.ContentType = "text/csv; charset=utf-8";

        var bom = new byte[] { 0xEF, 0xBB, 0xBF };
        await Response.Body.WriteAsync(bom, cancellationToken);

        var metaCols = new[] { "formKey", "storageId", "receivedAtUtc", "clientIp", "userAgent", "referrer" };
        var headerLine = string.Join(",", metaCols.Concat(unionKeys).Select(EscapeCsvField)) + "\n";
        await Response.Body.WriteAsync(System.Text.Encoding.UTF8.GetBytes(headerLine), cancellationToken);

        foreach (var item in listingPage.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var d = _formReader.GetSubmission(item.FormKey, item.StorageId);
            if (d is null) continue;

            var row = new List<string>(metaCols.Length + unionKeys.Count)
            {
                d.FormKey,
                d.StorageId,
                d.ReceivedAtUtc.ToString("O"),
                d.ClientIp ?? string.Empty,
                d.UserAgent ?? string.Empty,
                d.Referrer ?? string.Empty,
            };
            foreach (var k in unionKeys)
            {
                row.Add(d.Fields.TryGetValue(k, out var v) ? v : string.Empty);
            }
            var line = string.Join(",", row.Select(EscapeCsvField)) + "\n";
            await Response.Body.WriteAsync(System.Text.Encoding.UTF8.GetBytes(line), cancellationToken);
        }

        await Response.Body.FlushAsync(cancellationToken);
        return new EmptyResult();
    }

    /// <summary>
    /// CSV field escape: si contiene comma, comillas o newline, wrap
    /// en comillas dobles y escape comillas internas duplicándolas.
    /// </summary>
    private static string EscapeCsvField(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        bool needsQuotes = value.AsSpan().IndexOfAny(CsvSpecialChars) >= 0;
        if (!needsQuotes) return value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    /// <summary>
    /// GET /admin/webhooks/test — preview de los channels configurados
    /// + botones para disparar payload de test marcado como test
    /// (no-real data) a cada dominio.
    /// </summary>
    [HttpGet("webhooks/test")]
    public IActionResult WebhookTestHarness([FromQuery(Name = "msg")] string? messageCode = null)
    {
        SetTopbar("webhooks");
        ViewData["MessageCode"] = messageCode;
        return View();
    }

    [HttpPost("webhooks/test/comment")]
    public async Task<IActionResult> TestCommentWebhook(
        [FromServices] ICommentModerationNotifier notifier,
        CancellationToken cancellationToken)
    {
        var fakeComment = new Comment(
            Id: $"test-{Guid.NewGuid().ToString("N")[..8]}",
            NodeId: 0,
            MemberKey: null,
            AuthorName: "[TEST] Synergos webhook test",
            Body: "Este es un payload de prueba disparado desde /admin/webhooks/test. NO es un comentario real.",
            CreatedAtUtc: DateTime.UtcNow,
            Approved: false);

        await notifier.NotifyPendingAsync(fakeComment, cancellationToken);

        _analytics.Track("webhooks.test.comment", new Dictionary<string, object?>
        {
            ["moderator"] = _gate.CurrentMemberDisplayName,
        });
        return RedirectToAction(nameof(WebhookTestHarness), new { msg = "comment-fired" });
    }

    [HttpPost("webhooks/test/form")]
    public async Task<IActionResult> TestFormWebhook(
        [FromServices] IFormSubmissionNotifier notifier,
        CancellationToken cancellationToken)
    {
        var fakeRequest = new FormSubmissionRequest(
            FormKey: "synergos-webhook-test",
            Fields: new Dictionary<string, string>
            {
                ["test"] = "true",
                ["from"] = "/admin/webhooks/test",
                ["note"] = "NO es una submission real",
            },
            ClientIp: HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            UserAgent: Request.Headers.UserAgent.ToString(),
            Referrer: Request.Headers.Referer.ToString(),
            ReceivedAtUtc: DateTime.UtcNow);
        var fakeResult = FormSubmissionResult.Ok("test-storage-ref");

        await notifier.NotifySubmittedAsync(fakeRequest, fakeResult, cancellationToken);

        _analytics.Track("webhooks.test.form", new Dictionary<string, object?>
        {
            ["moderator"] = _gate.CurrentMemberDisplayName,
        });
        return RedirectToAction(nameof(WebhookTestHarness), new { msg = "form-fired" });
    }

    [HttpPost("webhooks/test/cart")]
    public async Task<IActionResult> TestCartWebhook(
        [FromServices] ICartAbandonmentNotifier notifier,
        CancellationToken cancellationToken)
    {
        var fakeCart = new AbandonedCart(
            CartId: $"test-{Guid.NewGuid().ToString("N")[..8]}",
            ItemCount: 3,
            Subtotal: 187_500m,
            Currency: "COP",
            LastActivityUtc: DateTime.UtcNow.AddMinutes(-90));

        await notifier.NotifyAbandonedAsync(fakeCart, cancellationToken);

        _analytics.Track("webhooks.test.cart", new Dictionary<string, object?>
        {
            ["moderator"] = _gate.CurrentMemberDisplayName,
        });
        return RedirectToAction(nameof(WebhookTestHarness), new { msg = "cart-fired" });
    }

    [HttpGet("health")]
    public IActionResult Health([FromServices] IWebhookTelemetryStore telemetryStore)
    {
        var process = System.Diagnostics.Process.GetCurrentProcess();
        var pendingPage = _comments.GetPendingPage(1, 1);
        var formKeys = _formReader.ListFormKeys();

        // Lecturas baratas — filesystem enumeration que ya hicimos para
        // las otras vistas del admin no se repite gracias al cache.
        var report = new HealthReport(
            UptimeSeconds: (int)(DateTime.UtcNow - process.StartTime.ToUniversalTime()).TotalSeconds,
            ProcessMemoryMb: (int)(process.WorkingSet64 / (1024 * 1024)),
            DotnetVersion: Environment.Version.ToString(),
            HostName: Environment.MachineName,
            PendingComments: pendingPage.TotalCount,
            FormsWithSubmissions: formKeys.Count,
            ResilienceMaxRetries: _adminSettings.CurrentValue.WebhookResilience.MaxRetryAttempts,
            ResilienceTimeoutTotal: _adminSettings.CurrentValue.WebhookResilience.TotalRequestTimeoutSeconds,
            CacheTtlSeconds: (int)_adminSettings.CurrentValue.PendingCountCacheTtl.TotalSeconds);

        SetTopbar("health", pendingPage.TotalCount);
        ViewData["Report"] = report;
        ViewData["WebhookTelemetry"] = telemetryStore.GetChannelStats();
        return View();
    }

    public sealed record HealthReport(
        int UptimeSeconds,
        int ProcessMemoryMb,
        string DotnetVersion,
        string HostName,
        int PendingComments,
        int FormsWithSubmissions,
        int ResilienceMaxRetries,
        int ResilienceTimeoutTotal,
        int CacheTtlSeconds);

    [HttpPost("forms/{formKey}/{storageId}/delete")]
    public async Task<IActionResult> DeleteFormSubmission(
        string formKey,
        string storageId,
        CancellationToken cancellationToken)
    {
        var ok = await _formReader.DeleteAsync(formKey, storageId, cancellationToken);
        if (ok)
        {
            _analytics.Track("form.submission.deleted", new Dictionary<string, object?>
            {
                ["formKey"] = formKey,
                ["storageId"] = storageId,
                ["moderator"] = _gate.CurrentMemberDisplayName,
                ["source"] = "admin-dashboard",
            });
        }
        await EmitAuditAsync(
            "form.delete",
            $"formKey={formKey} storageId={storageId}",
            ok ? "success" : "failure",
            cancellationToken: cancellationToken);

        return RedirectToAction(nameof(FormSubmissions));
    }

    [HttpGet("forms/{formKey}/{storageId}")]
    public IActionResult FormSubmissionDetail(string formKey, string storageId)
    {
        var detail = _formReader.GetSubmission(formKey, storageId);
        if (detail is null)
        {
            return NotFound();
        }

        SetTopbar("forms");
        ViewData["Detail"] = detail;
        return View();
    }

    [HttpGet("moderation/comments")]
    public IActionResult ModerationComments(
        [FromQuery] int page = 1,
        [FromQuery(Name = "pageSize")] int pageSize = 0,
        [FromQuery(Name = "nodeId")] int? nodeIdFilter = null,
        [FromQuery(Name = "msg")] string? messageCode = null)
    {
        if (pageSize <= 0) pageSize = DefaultPageSize;
        var pageData = _comments.GetPendingPage(page, pageSize, nodeIdFilter);
        SetTopbar("moderation", pageData.TotalCount);
        ViewData["Page"] = pageData;
        ViewData["NodeIdFilter"] = nodeIdFilter;
        ViewData["MessageCode"] = messageCode;
        return View();
    }

    [HttpPost("moderation/comments/{nodeId:int}/{commentId}/approve")]
    public async Task<IActionResult> ApproveComment(
        int nodeId,
        string commentId,
        CancellationToken cancellationToken)
    {
        var ok = await _comments.ApproveAsync(nodeId, commentId, cancellationToken);
        if (ok)
        {
            _analytics.Track("comment.moderation.approved", new Dictionary<string, object?>
            {
                ["nodeId"] = nodeId,
                ["commentId"] = commentId,
                ["moderator"] = _gate.CurrentMemberDisplayName,
                ["source"] = "admin-dashboard",
            });
        }
        await EmitAuditAsync(
            "comment.approve",
            $"node={nodeId} commentId={commentId}",
            ok ? "success" : "failure",
            cancellationToken: cancellationToken);

        _cache.Remove(PendingCountCacheKey);
        return RedirectToAction(nameof(ModerationComments));
    }

    [HttpPost("moderation/comments/{nodeId:int}/{commentId}/reject")]
    public async Task<IActionResult> RejectComment(
        int nodeId,
        string commentId,
        CancellationToken cancellationToken)
    {
        var ok = await _comments.RejectAsync(nodeId, commentId, cancellationToken);
        if (ok)
        {
            _analytics.Track("comment.moderation.rejected", new Dictionary<string, object?>
            {
                ["nodeId"] = nodeId,
                ["commentId"] = commentId,
                ["moderator"] = _gate.CurrentMemberDisplayName,
                ["source"] = "admin-dashboard",
            });
        }
        await EmitAuditAsync(
            "comment.reject",
            $"node={nodeId} commentId={commentId}",
            ok ? "success" : "failure",
            cancellationToken: cancellationToken);

        _cache.Remove(PendingCountCacheKey);
        return RedirectToAction(nameof(ModerationComments));
    }

    /// <summary>
    /// Mark-as-spam — variant del reject pero emite analytics event
    /// distinto (<c>comment.moderation.spam-reported</c>) para que un
    /// futuro spam filter (Akismet, custom ML) entrene sobre estos.
    /// El backend de delete es exactamente el mismo que reject — el
    /// comentario se elimina del store. La diferencia es semántica:
    /// reject = "no me gustó", spam = "esto es spam confirmado".
    /// </summary>
    [HttpPost("moderation/comments/{nodeId:int}/{commentId}/spam")]
    public async Task<IActionResult> MarkCommentAsSpam(
        int nodeId,
        string commentId,
        CancellationToken cancellationToken)
    {
        var ok = await _comments.RejectAsync(nodeId, commentId, cancellationToken);
        if (ok)
        {
            _analytics.Track("comment.moderation.spam-reported", new Dictionary<string, object?>
            {
                ["nodeId"] = nodeId,
                ["commentId"] = commentId,
                ["moderator"] = _gate.CurrentMemberDisplayName,
                ["source"] = "admin-dashboard",
            });
        }
        await EmitAuditAsync(
            "comment.spam",
            $"node={nodeId} commentId={commentId}",
            ok ? "success" : "failure",
            cancellationToken: cancellationToken);

        _cache.Remove(PendingCountCacheKey);
        return RedirectToAction(nameof(ModerationComments), new { msg = "spam-1" });
    }

    [HttpPost("moderation/comments/bulk-approve")]
    [Consumes("application/x-www-form-urlencoded")]
    public async Task<IActionResult> BulkApproveComments(
        [FromForm] string[] targets,
        CancellationToken cancellationToken)
    {
        var refs = ParseTargets(targets);
        var changed = await _comments.BulkApproveAsync(refs, cancellationToken);

        if (changed > 0)
        {
            _analytics.Track("comment.moderation.bulk-approved", new Dictionary<string, object?>
            {
                ["count"] = changed,
                ["moderator"] = _gate.CurrentMemberDisplayName,
                ["source"] = "admin-dashboard",
            });
        }
        await EmitAuditAsync(
            "comment.bulk-approve",
            $"refsRequested={refs.Count} changed={changed}",
            changed > 0 ? "success" : "partial",
            cancellationToken: cancellationToken);

        _cache.Remove(PendingCountCacheKey);
        return RedirectToAction(nameof(ModerationComments), new { msg = $"approved-{changed}" });
    }

    [HttpPost("moderation/comments/bulk-reject")]
    [Consumes("application/x-www-form-urlencoded")]
    public async Task<IActionResult> BulkRejectComments(
        [FromForm] string[] targets,
        CancellationToken cancellationToken)
    {
        var refs = ParseTargets(targets);

        // Ola 126 — snapshot ANTES del delete para soporte undo.
        // El cache key contiene un token random; el flash message
        // incluye el token para que el moderator pueda revertir
        // dentro de BulkUndoWindow (default 30s).
        var snapshot = _comments.ReadByRefs(refs);
        var changed = await _comments.BulkRejectAsync(refs, cancellationToken);

        string? undoToken = null;
        if (changed > 0 && snapshot.Count > 0)
        {
            undoToken = Guid.NewGuid().ToString("N")[..12];
            _cache.Set(BulkUndoCacheKeyPrefix + undoToken, snapshot, BulkUndoWindow);

            _analytics.Track("comment.moderation.bulk-rejected", new Dictionary<string, object?>
            {
                ["count"] = changed,
                ["moderator"] = _gate.CurrentMemberDisplayName,
                ["source"] = "admin-dashboard",
                ["undoToken"] = undoToken,
            });
        }
        await EmitAuditAsync(
            "comment.bulk-reject",
            $"refsRequested={refs.Count} changed={changed}",
            changed > 0 ? "success" : "partial",
            detail: undoToken is not null ? $"undoToken={undoToken}" : "",
            cancellationToken: cancellationToken);

        _cache.Remove(PendingCountCacheKey);

        var msg = undoToken is not null
            ? $"rejected-{changed}-undo:{undoToken}"
            : $"rejected-{changed}";
        return RedirectToAction(nameof(ModerationComments), new { msg });
    }

    [HttpPost("moderation/comments/bulk-undo-reject/{token}")]
    public async Task<IActionResult> BulkUndoReject(
        string token,
        CancellationToken cancellationToken)
    {
        if (!_cache.TryGetValue(BulkUndoCacheKeyPrefix + token, out IReadOnlyList<Comment>? snapshot)
            || snapshot is null)
        {
            // Ventana expiró o token inválido — flash message friendly.
            return RedirectToAction(nameof(ModerationComments), new { msg = "undo-expired" });
        }

        var restored = await _comments.RestoreAsync(snapshot, cancellationToken);
        _cache.Remove(BulkUndoCacheKeyPrefix + token);
        _cache.Remove(PendingCountCacheKey);

        if (restored > 0)
        {
            _analytics.Track("comment.moderation.bulk-undo-rejected", new Dictionary<string, object?>
            {
                ["count"] = restored,
                ["moderator"] = _gate.CurrentMemberDisplayName,
                ["source"] = "admin-dashboard",
            });
        }

        return RedirectToAction(nameof(ModerationComments), new { msg = $"undone-{restored}" });
    }

    /// <summary>
    /// Parse "targets" form values con shape "{nodeId}|{commentId}".
    /// Filtra entradas mal formadas — defensivo contra forms corruptos.
    /// </summary>
    private static IReadOnlyList<CommentRef> ParseTargets(string[] targets)
    {
        if (targets.Length == 0) return Array.Empty<CommentRef>();
        var refs = new List<CommentRef>(targets.Length);
        foreach (var t in targets)
        {
            if (string.IsNullOrWhiteSpace(t)) continue;
            var parts = t.Split('|', 2);
            if (parts.Length != 2) continue;
            if (!int.TryParse(parts[0], out var nodeId)) continue;
            if (string.IsNullOrWhiteSpace(parts[1])) continue;
            refs.Add(new CommentRef(nodeId, parts[1]));
        }
        return refs;
    }

    /// <summary>
    /// GET /admin/analytics/search?from=2026-04-01&amp;to=2026-04-30&amp;limit=20
    /// — top queries + no-result queries en la ventana indicada.
    /// </summary>
    [HttpGet("analytics/search")]
    public IActionResult AnalyticsSearch(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int limit = 20)
    {
        var fromUtc = (from ?? DateTime.UtcNow.AddDays(-30)).ToUniversalTime();
        var toUtc = (to ?? DateTime.UtcNow).ToUniversalTime();
        var clampedLimit = Math.Clamp(limit, 1, 100);

        var topQueries = _searchAnalytics.GetTopQueries(fromUtc, toUtc, clampedLimit);
        var topNoResults = _searchAnalytics.GetTopNoResultQueries(fromUtc, toUtc, clampedLimit);

        SetTopbar("search");
        ViewData["FromUtc"] = fromUtc;
        ViewData["ToUtc"] = toUtc;
        ViewData["Limit"] = clampedLimit;
        ViewData["TopQueries"] = topQueries;
        ViewData["TopNoResults"] = topNoResults;
        return View();
    }

    /// <summary>
    /// GET /admin/audit — listing read-only del audit trail (Olas 153-154).
    /// Muestra los últimos N eventos con filter opcional por actorEmail
    /// y action. Read-only — el audit trail es append-only.
    /// Ola 163 agrega date range opcional que cae a GetByDateRange en
    /// lugar de GetRecent cuando from o to están seteados.
    /// </summary>
    [HttpGet("audit")]
    public IActionResult Audit(
        [FromQuery(Name = "actor")] string? actorEmailFilter = null,
        [FromQuery(Name = "action")] string? actionFilter = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] int limit = 100)
    {
        var clampedLimit = Math.Clamp(limit, 1, 500);
        IReadOnlyList<AuditEvent> events;
        if (from.HasValue || to.HasValue)
        {
            var fromUtc = (from ?? DateTime.UtcNow.AddDays(-7)).ToUniversalTime();
            var toUtc = (to ?? DateTime.UtcNow).ToUniversalTime();
            events = _audit.GetByDateRange(fromUtc, toUtc, clampedLimit, actorEmailFilter, actionFilter);
            ViewData["FromUtc"] = fromUtc;
            ViewData["ToUtc"] = toUtc;
        }
        else
        {
            events = _audit.GetRecent(clampedLimit, actorEmailFilter, actionFilter);
        }

        SetTopbar("audit");
        ViewData["Events"] = events;
        ViewData["ActorFilter"] = actorEmailFilter;
        ViewData["ActionFilter"] = actionFilter;
        ViewData["Limit"] = clampedLimit;
        return View();
    }

    /// <summary>
    /// GET /admin/audit/{id} — drill-down detail de un evento específico.
    /// Búsqueda hasta los últimos 30 días. (Ola 193)
    /// </summary>
    [HttpGet("audit/{id:length(8,64)}")]
    public IActionResult AuditDetail(string id)
    {
        var evt = _audit.GetById(id);
        if (evt is null)
        {
            return NotFound();
        }

        // Eventos cercanos del mismo actor (±5 min ventana) para context.
        var window = TimeSpan.FromMinutes(5);
        var nearby = _audit.GetByDateRange(
            evt.OccurredAtUtc - window,
            evt.OccurredAtUtc + window,
            maxItems: 50,
            actorEmailFilter: evt.ActorEmail,
            actionFilter: null);

        SetTopbar("audit");
        ViewData["Event"] = evt;
        ViewData["Nearby"] = nearby.Where(e => e.Id != evt.Id).ToList();
        return View();
    }

    /// <summary>
    /// GET /admin/audit/export — CSV streaming export con los mismos
    /// filtros que el listing. Streamed via Response.Body.WriteAsync
    /// para memoria O(1). Hard cap de CsvExportHardCap. (Ola 164)
    /// </summary>
    [HttpGet("audit/export")]
    public async Task AuditExportCsv(
        [FromQuery(Name = "actor")] string? actorEmailFilter,
        [FromQuery(Name = "action")] string? actionFilter,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken cancellationToken)
    {
        var fromUtc = (from ?? DateTime.UtcNow.AddDays(-30)).ToUniversalTime();
        var toUtc = (to ?? DateTime.UtcNow).ToUniversalTime();
        var events = _audit.GetByDateRange(fromUtc, toUtc, CsvExportHardCap, actorEmailFilter, actionFilter);

        Response.ContentType = "text/csv; charset=utf-8";
        Response.Headers["Content-Disposition"] =
            $"attachment; filename=\"synergos-audit-{fromUtc:yyyyMMdd}-{toUtc:yyyyMMdd}.csv\"";

        var bom = new byte[] { 0xEF, 0xBB, 0xBF };
        await Response.Body.WriteAsync(bom, cancellationToken);

        var headerLine = "OccurredAtUtc,ActorEmail,ActorName,Action,Resource,Outcome,Detail,Id\n";
        await Response.Body.WriteAsync(System.Text.Encoding.UTF8.GetBytes(headerLine), cancellationToken);

        foreach (var e in events)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = string.Join(',',
                e.OccurredAtUtc.ToString("O"),
                EscapeCsvField(e.ActorEmail),
                EscapeCsvField(e.ActorName),
                EscapeCsvField(e.Action),
                EscapeCsvField(e.Resource),
                EscapeCsvField(e.Outcome),
                EscapeCsvField(e.Detail),
                EscapeCsvField(e.Id)) + "\n";
            await Response.Body.WriteAsync(System.Text.Encoding.UTF8.GetBytes(line), cancellationToken);
        }

        await EmitAuditAsync(
            "audit.export",
            $"from={fromUtc:O} to={toUtc:O} count={events.Count}",
            "success",
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// GET /admin/members — listing del Member roster con paginación +
    /// filter por role. Read del IMemberRosterReader; lock/unlock via
    /// IMemberRosterWriter en POST actions hermanas. Olas 144-145 + 155-156.
    /// </summary>
    [HttpGet("members")]
    public async Task<IActionResult> Members(
        [FromQuery] int page = 1,
        [FromQuery(Name = "role")] string? roleFilter = null,
        CancellationToken cancellationToken = default)
    {
        var roster = _memberRoster.GetRosterPage(page, DefaultPageSize, roleFilter);
        var allRoles = _memberRoster.ListAllRoles();

        // Olas 178-180 — read 2FA status per member para el panel admin.
        var twoFactorStatus = new Dictionary<Guid, bool>();
        foreach (var m in roster.Items)
        {
            twoFactorStatus[m.Key] = await _memberTwoFactor.IsEnabledAsync(m.Key, cancellationToken);
        }

        SetTopbar("members");
        ViewData["Roster"] = roster;
        ViewData["AllRoles"] = allRoles;
        ViewData["RoleFilter"] = roleFilter;
        ViewData["TwoFactorStatus"] = twoFactorStatus;
        return View();
    }

    /// <summary>
    /// POST /admin/members/{key}/lock — bloquea Member. Idempotent. Olas 155-156.
    /// </summary>
    [HttpPost("members/{memberKey:guid}/lock")]
    public async Task<IActionResult> LockMember(Guid memberKey, CancellationToken cancellationToken)
    {
        var ok = await _memberRosterWriter.LockAsync(memberKey, cancellationToken);
        await EmitAuditAsync(
            "member.lock",
            $"memberKey={memberKey:N}",
            ok ? "success" : "failure",
            cancellationToken: cancellationToken);

        return RedirectToAction(nameof(Members));
    }

    /// <summary>
    /// POST /admin/members/{key}/unlock — desbloquea Member. Idempotent. Olas 155-156.
    /// </summary>
    [HttpPost("members/{memberKey:guid}/unlock")]
    public async Task<IActionResult> UnlockMember(Guid memberKey, CancellationToken cancellationToken)
    {
        var ok = await _memberRosterWriter.UnlockAsync(memberKey, cancellationToken);
        await EmitAuditAsync(
            "member.unlock",
            $"memberKey={memberKey:N}",
            ok ? "success" : "failure",
            cancellationToken: cancellationToken);

        return RedirectToAction(nameof(Members));
    }

    /// <summary>
    /// POST /admin/members/{key}/2fa-reset — disable 2FA del Member.
    /// Útil cuando el Member perdió el dispositivo + recovery codes.
    /// El Member tendrá que re-enroll en su próximo login. Olas 178-180.
    /// </summary>
    [HttpPost("members/{memberKey:guid}/2fa-reset")]
    public async Task<IActionResult> ResetMemberTwoFactor(Guid memberKey, CancellationToken cancellationToken)
    {
        var ok = await _memberTwoFactor.DisableAsync(memberKey, cancellationToken);
        await EmitAuditAsync(
            "member.2fa-reset",
            $"memberKey={memberKey:N}",
            ok ? "success" : "failure",
            cancellationToken: cancellationToken);

        return RedirectToAction(nameof(Members));
    }

    /// <summary>
    /// POST /admin/members/{key}/delete — hard delete <b>irreversible</b>.
    /// Threat model en ADR 0078. Requiere confirm dialog en UI. No
    /// permite delete del propio Member del actor para evitar
    /// accidental self-locking. Olas 181-184.
    /// </summary>
    [HttpPost("members/{memberKey:guid}/delete")]
    public async Task<IActionResult> DeleteMember(Guid memberKey, CancellationToken cancellationToken)
    {
        // Self-delete guard: aunque la UI no exponga el botón sobre el
        // current member, validamos por defensa en depth.
        var currentEmail = _gate.CurrentMemberEmail;
        if (!string.IsNullOrWhiteSpace(currentEmail))
        {
            var roster = _memberRoster.GetRosterPage(1, 1000, null);
            var match = roster.Items.FirstOrDefault(m => m.Key == memberKey);
            if (match is not null && string.Equals(match.Email, currentEmail, StringComparison.OrdinalIgnoreCase))
            {
                await EmitAuditAsync("member.delete", $"memberKey={memberKey:N}", "failure",
                    detail: "self-delete blocked", cancellationToken: cancellationToken);
                return RedirectToAction(nameof(Members));
            }
        }

        var ok = await _memberRosterWriter.DeleteAsync(memberKey, cancellationToken);
        // Auto-reset 2FA state si existe — el record file persiste sino.
        if (ok) await _memberTwoFactor.DisableAsync(memberKey, cancellationToken);

        await EmitAuditAsync(
            "member.delete",
            $"memberKey={memberKey:N}",
            ok ? "success" : "failure",
            cancellationToken: cancellationToken);

        return RedirectToAction(nameof(Members));
    }

    /// <summary>
    /// POST /admin/members/{key}/password-reset — admin-triggered.
    /// Genera token + envía email al Member con link a /account/reset-password.
    /// Idempotent — no leak si Member no existe (audit registra failure).
    /// Olas 181-184.
    /// </summary>
    [HttpPost("members/{memberKey:guid}/password-reset")]
    public async Task<IActionResult> SendMemberPasswordReset(Guid memberKey, CancellationToken cancellationToken)
    {
        var ok = await _memberRosterWriter.SendPasswordResetAsync(memberKey, cancellationToken);
        await EmitAuditAsync(
            "member.password-reset-sent",
            $"memberKey={memberKey:N}",
            ok ? "success" : "failure",
            cancellationToken: cancellationToken);

        return RedirectToAction(nameof(Members));
    }

    /// <summary>
    /// POST /admin/members/{key}/roles — replace roles set. Form binds
    /// CSV de role names. Idempotent. Olas 181-184.
    /// </summary>
    [HttpPost("members/{memberKey:guid}/roles")]
    public async Task<IActionResult> SetMemberRoles(
        Guid memberKey,
        [FromForm] string[] roles,
        CancellationToken cancellationToken)
    {
        var clean = (roles ?? Array.Empty<string>())
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => r.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var ok = await _memberRosterWriter.SetRolesAsync(memberKey, clean, cancellationToken);
        await EmitAuditAsync(
            "member.set-roles",
            $"memberKey={memberKey:N}",
            ok ? "success" : "failure",
            detail: $"roles=[{string.Join(',', clean)}]",
            cancellationToken: cancellationToken);

        return RedirectToAction(nameof(Members));
    }

    /// <summary>
    /// POST /admin/members/{key}/gdpr-erase — flujo "Right to be
    /// Forgotten" (GDPR Art. 17). Olas 233-235 (Cap-240 Batch B).
    /// Hard-deletes el Member + anonimiza comments y form submissions
    /// linkeadas + audita "gdpr.rtbf-processed". <b>Irreversible</b>:
    /// la UI debe mostrar confirm dialog antes de POST. Self-erase
    /// blocked por defensa en depth.
    /// </summary>
    [HttpPost("members/{memberKey:guid}/gdpr-erase")]
    public async Task<IActionResult> GdprEraseMember(Guid memberKey, CancellationToken cancellationToken)
    {
        var actorEmail = _gate.CurrentMemberEmail ?? string.Empty;

        // Self-erase guard — mismo pattern que DeleteMember.
        if (!string.IsNullOrWhiteSpace(actorEmail))
        {
            var roster = _memberRoster.GetRosterPage(1, 1000, null);
            var match = roster.Items.FirstOrDefault(m => m.Key == memberKey);
            if (match is not null && string.Equals(match.Email, actorEmail, StringComparison.OrdinalIgnoreCase))
            {
                await EmitAuditAsync("gdpr.rtbf-processed", $"memberKey={memberKey:N}", "failure",
                    detail: "self-erase blocked", cancellationToken: cancellationToken);
                return RedirectToAction(nameof(Members));
            }
        }

        var result = await _gdpr.ProcessRequestAsync(memberKey, actorEmail, cancellationToken);

        // El coordinator ya emitió su propio audit terminal. Cascada de 2FA
        // queda alineada con DeleteMember para no dejar el secret huérfano.
        if (result.MemberDeleted)
        {
            await _memberTwoFactor.DisableAsync(memberKey, cancellationToken);
        }

        return RedirectToAction(nameof(Members));
    }
}
