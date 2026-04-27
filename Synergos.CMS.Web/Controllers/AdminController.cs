using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;

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
/// Auth gating: cada action verifica <see cref="IMemberAccessGate.HasAnyRole"/>
/// con CSV <c>"admin,moderator,editor"</c> y devuelve <c>Forbid()</c> si
/// falla. El visitante anónimo recibe 401 → redirige a login según
/// pipeline Umbraco. Sin antiforgery — los forms POST son
/// member-authenticated y el risk de CSRF es bajo en este flow
/// editorial.
/// </remarks>
[Route("admin")]
[AllowAnonymous]
public sealed class AdminController : Controller
{
    private const string ModeratorRolesCsv = "admin,moderator,editor";
    private const string PendingCountCacheKey = "admin.pending-comments-count";
    private const string BulkUndoCacheKeyPrefix = "admin.bulk-undo:";

    private readonly ICommentRepository _comments;
    private readonly IMemberAccessGate _gate;
    private readonly IAnalyticsTracker _analytics;
    private readonly ISearchAnalyticsStore _searchAnalytics;
    private readonly IFormSubmissionReader _formReader;
    private readonly IMemoryCache _cache;
    private readonly IOptionsMonitor<AdminSettings> _adminSettings;

    public AdminController(
        ICommentRepository comments,
        IMemberAccessGate gate,
        IAnalyticsTracker analytics,
        ISearchAnalyticsStore searchAnalytics,
        IFormSubmissionReader formReader,
        IMemoryCache cache,
        IOptionsMonitor<AdminSettings> adminSettings)
    {
        _comments = comments;
        _gate = gate;
        _analytics = analytics;
        _searchAnalytics = searchAnalytics;
        _formReader = formReader;
        _cache = cache;
        _adminSettings = adminSettings;
    }

    private int DefaultPageSize => _adminSettings.CurrentValue.DefaultPageSize;
    private TimeSpan PendingCountCacheTtl => _adminSettings.CurrentValue.PendingCountCacheTtl;
    private TimeSpan BulkUndoWindow => _adminSettings.CurrentValue.BulkUndoWindow;
    private int CsvExportHardCap => _adminSettings.CurrentValue.CsvExportHardCap;

    [HttpGet("")]
    public IActionResult Index()
    {
        if (!_gate.HasAnyRole(ModeratorRolesCsv))
        {
            return Forbid();
        }

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
        if (!_gate.HasAnyRole(ModeratorRolesCsv))
        {
            return Forbid();
        }

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
        if (!_gate.HasAnyRole(ModeratorRolesCsv))
        {
            return Forbid();
        }

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
        bool needsQuotes = value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0;
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
        if (!_gate.HasAnyRole(ModeratorRolesCsv))
        {
            return Forbid();
        }

        SetTopbar("webhooks");
        ViewData["MessageCode"] = messageCode;
        return View();
    }

    [HttpPost("webhooks/test/comment")]
    public async Task<IActionResult> TestCommentWebhook(
        [FromServices] ICommentModerationNotifier notifier,
        CancellationToken cancellationToken)
    {
        if (!_gate.HasAnyRole(ModeratorRolesCsv))
        {
            return Forbid();
        }

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
        if (!_gate.HasAnyRole(ModeratorRolesCsv))
        {
            return Forbid();
        }

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
        if (!_gate.HasAnyRole(ModeratorRolesCsv))
        {
            return Forbid();
        }

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
    public IActionResult Health()
    {
        if (!_gate.HasAnyRole(ModeratorRolesCsv))
        {
            return Forbid();
        }

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
        if (!_gate.HasAnyRole(ModeratorRolesCsv))
        {
            return Forbid();
        }

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

        return RedirectToAction(nameof(FormSubmissions));
    }

    [HttpGet("forms/{formKey}/{storageId}")]
    public IActionResult FormSubmissionDetail(string formKey, string storageId)
    {
        if (!_gate.HasAnyRole(ModeratorRolesCsv))
        {
            return Forbid();
        }

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
        if (!_gate.HasAnyRole(ModeratorRolesCsv))
        {
            return Forbid();
        }

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
        if (!_gate.HasAnyRole(ModeratorRolesCsv))
        {
            return Forbid();
        }

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

        _cache.Remove(PendingCountCacheKey);
        return RedirectToAction(nameof(ModerationComments));
    }

    [HttpPost("moderation/comments/{nodeId:int}/{commentId}/reject")]
    public async Task<IActionResult> RejectComment(
        int nodeId,
        string commentId,
        CancellationToken cancellationToken)
    {
        if (!_gate.HasAnyRole(ModeratorRolesCsv))
        {
            return Forbid();
        }

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
        if (!_gate.HasAnyRole(ModeratorRolesCsv))
        {
            return Forbid();
        }

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

        _cache.Remove(PendingCountCacheKey);
        return RedirectToAction(nameof(ModerationComments), new { msg = "spam-1" });
    }

    [HttpPost("moderation/comments/bulk-approve")]
    [Consumes("application/x-www-form-urlencoded")]
    public async Task<IActionResult> BulkApproveComments(
        [FromForm] string[] targets,
        CancellationToken cancellationToken)
    {
        if (!_gate.HasAnyRole(ModeratorRolesCsv))
        {
            return Forbid();
        }

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

        _cache.Remove(PendingCountCacheKey);
        return RedirectToAction(nameof(ModerationComments), new { msg = $"approved-{changed}" });
    }

    [HttpPost("moderation/comments/bulk-reject")]
    [Consumes("application/x-www-form-urlencoded")]
    public async Task<IActionResult> BulkRejectComments(
        [FromForm] string[] targets,
        CancellationToken cancellationToken)
    {
        if (!_gate.HasAnyRole(ModeratorRolesCsv))
        {
            return Forbid();
        }

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
        if (!_gate.HasAnyRole(ModeratorRolesCsv))
        {
            return Forbid();
        }

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
        if (!_gate.HasAnyRole(ModeratorRolesCsv))
        {
            return Forbid();
        }

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
}
