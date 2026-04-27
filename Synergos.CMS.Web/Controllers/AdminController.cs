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
    private const int PendingCommentsLimit = 50;

    private readonly ICommentRepository _comments;
    private readonly IMemberAccessGate _gate;
    private readonly IAnalyticsTracker _analytics;
    private readonly ISearchAnalyticsStore _searchAnalytics;

    public AdminController(
        ICommentRepository comments,
        IMemberAccessGate gate,
        IAnalyticsTracker analytics,
        ISearchAnalyticsStore searchAnalytics)
    {
        _comments = comments;
        _gate = gate;
        _analytics = analytics;
        _searchAnalytics = searchAnalytics;
    }

    [HttpGet("moderation/comments")]
    public IActionResult ModerationComments()
    {
        if (!_gate.HasAnyRole(ModeratorRolesCsv))
        {
            return Forbid();
        }

        var pending = _comments.GetAllPending(PendingCommentsLimit);
        ViewData["Pending"] = pending;
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
