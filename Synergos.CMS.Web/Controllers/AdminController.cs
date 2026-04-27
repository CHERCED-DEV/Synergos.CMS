using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
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
    private const int DefaultPageSize = 25;
    private const string PendingCountCacheKey = "admin.pending-comments-count";
    private static readonly TimeSpan PendingCountCacheTtl = TimeSpan.FromSeconds(30);

    private readonly ICommentRepository _comments;
    private readonly IMemberAccessGate _gate;
    private readonly IAnalyticsTracker _analytics;
    private readonly ISearchAnalyticsStore _searchAnalytics;
    private readonly IFormSubmissionReader _formReader;
    private readonly IMemoryCache _cache;

    public AdminController(
        ICommentRepository comments,
        IMemberAccessGate gate,
        IAnalyticsTracker analytics,
        ISearchAnalyticsStore searchAnalytics,
        IFormSubmissionReader formReader,
        IMemoryCache cache)
    {
        _comments = comments;
        _gate = gate;
        _analytics = analytics;
        _searchAnalytics = searchAnalytics;
        _formReader = formReader;
        _cache = cache;
    }

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
        [FromQuery(Name = "pageSize")] int pageSize = DefaultPageSize,
        [FromQuery(Name = "formKey")] string? formKeyFilter = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null)
    {
        if (!_gate.HasAnyRole(ModeratorRolesCsv))
        {
            return Forbid();
        }

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
    public IActionResult ExportFormSubmissions(
        [FromQuery(Name = "formKey")] string? formKeyFilter = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] int limit = 500)
    {
        if (!_gate.HasAnyRole(ModeratorRolesCsv))
        {
            return Forbid();
        }

        var clamped = Math.Clamp(limit, 1, 5000);
        var fromUtc = from?.ToUniversalTime();
        var toUtc = to?.ToUniversalTime();
        var listingPage = _formReader.GetRecent(
            page: 1,
            pageSize: clamped,
            formKeyFilter,
            fromUtc,
            toUtc);

        // Recolectamos todos los keys de columna posibles a través de
        // las submissions del scope. Cada submission puede tener fields
        // distintos — el CSV los unifica con valores vacíos donde falte.
        var unionKeys = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var details = new List<FormSubmissionDetail>(listingPage.Items.Count);
        foreach (var item in listingPage.Items)
        {
            var detail = _formReader.GetSubmission(item.FormKey, item.StorageId);
            if (detail is null) continue;
            details.Add(detail);
            foreach (var k in detail.Fields.Keys) unionKeys.Add(k);
        }

        var sb = new System.Text.StringBuilder();
        // Header: meta cols + field cols sorted.
        var metaCols = new[] { "formKey", "storageId", "receivedAtUtc", "clientIp", "userAgent", "referrer" };
        sb.Append(string.Join(",", metaCols.Concat(unionKeys).Select(EscapeCsvField)));
        sb.Append('\n');

        foreach (var d in details)
        {
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
            sb.Append(string.Join(",", row.Select(EscapeCsvField)));
            sb.Append('\n');
        }

        var fileName = string.IsNullOrWhiteSpace(formKeyFilter)
            ? $"submissions_{DateTime.UtcNow:yyyyMMdd_HHmm}.csv"
            : $"submissions_{formKeyFilter}_{DateTime.UtcNow:yyyyMMdd_HHmm}.csv";

        // BOM UTF-8 para que Excel no malinterprete encoding.
        var utf8Bom = new byte[] { 0xEF, 0xBB, 0xBF };
        var content = utf8Bom.Concat(System.Text.Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        return File(content, "text/csv; charset=utf-8", fileName);
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
        [FromQuery(Name = "pageSize")] int pageSize = DefaultPageSize,
        [FromQuery(Name = "nodeId")] int? nodeIdFilter = null,
        [FromQuery(Name = "msg")] string? messageCode = null)
    {
        if (!_gate.HasAnyRole(ModeratorRolesCsv))
        {
            return Forbid();
        }

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

        return RedirectToAction(nameof(ModerationComments));
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
        var changed = await _comments.BulkRejectAsync(refs, cancellationToken);

        if (changed > 0)
        {
            _analytics.Track("comment.moderation.bulk-rejected", new Dictionary<string, object?>
            {
                ["count"] = changed,
                ["moderator"] = _gate.CurrentMemberDisplayName,
                ["source"] = "admin-dashboard",
            });
        }

        return RedirectToAction(nameof(ModerationComments), new { msg = $"rejected-{changed}" });
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
