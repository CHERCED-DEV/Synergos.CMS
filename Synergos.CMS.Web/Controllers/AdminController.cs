using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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

    private readonly ICommentRepository _comments;
    private readonly IMemberAccessGate _gate;
    private readonly IAnalyticsTracker _analytics;
    private readonly ISearchAnalyticsStore _searchAnalytics;
    private readonly IFormSubmissionReader _formReader;

    public AdminController(
        ICommentRepository comments,
        IMemberAccessGate gate,
        IAnalyticsTracker analytics,
        ISearchAnalyticsStore searchAnalytics,
        IFormSubmissionReader formReader)
    {
        _comments = comments;
        _gate = gate;
        _analytics = analytics;
        _searchAnalytics = searchAnalytics;
        _formReader = formReader;
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

        ViewData["PendingCount"] = pendingPage.TotalCount;
        ViewData["FormKeys"] = formKeys;
        ViewData["TopQueries7d"] = topQueries7d;
        ViewData["ModeratorName"] = _gate.CurrentMemberDisplayName ?? "—";
        return View();
    }

    [HttpGet("forms")]
    public IActionResult FormSubmissions(
        [FromQuery] int page = 1,
        [FromQuery(Name = "pageSize")] int pageSize = DefaultPageSize,
        [FromQuery(Name = "formKey")] string? formKeyFilter = null)
    {
        if (!_gate.HasAnyRole(ModeratorRolesCsv))
        {
            return Forbid();
        }

        var pageData = _formReader.GetRecent(page, pageSize, formKeyFilter);
        var formKeys = _formReader.ListFormKeys();

        ViewData["Page"] = pageData;
        ViewData["FormKeyFilter"] = formKeyFilter;
        ViewData["FormKeys"] = formKeys;
        ViewData["ModeratorName"] = _gate.CurrentMemberDisplayName ?? "—";
        return View();
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

        ViewData["Detail"] = detail;
        ViewData["ModeratorName"] = _gate.CurrentMemberDisplayName ?? "—";
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
        ViewData["Page"] = pageData;
        ViewData["NodeIdFilter"] = nodeIdFilter;
        ViewData["MessageCode"] = messageCode;
        ViewData["ModeratorName"] = _gate.CurrentMemberDisplayName ?? "—";
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

        ViewData["FromUtc"] = fromUtc;
        ViewData["ToUtc"] = toUtc;
        ViewData["Limit"] = clampedLimit;
        ViewData["TopQueries"] = topQueries;
        ViewData["TopNoResults"] = topNoResults;
        ViewData["ModeratorName"] = _gate.CurrentMemberDisplayName ?? "—";
        return View();
    }
}
