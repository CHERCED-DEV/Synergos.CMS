using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Controllers;

/// <summary>
/// Endpoints de moderación de comentarios (Ola 87). Pareja del
/// <see cref="CommentsController"/> público — éste es backoffice-side
/// y queda gated por roles "admin", "moderator" o "editor" via
/// <see cref="IMemberAccessGate"/>.
/// </summary>
/// <remarks>
/// La moderación se diseña intencionalmente sobre Members (no Users
/// del backoffice) porque el flujo natural de revisión queda en
/// front-end del sitio: el moderador se loguea en el front con su
/// member-account y aprueba desde ahí. Para sitios donde la
/// moderación es Users-only, swap por <c>[Authorize(Roles=...)]</c>
/// nativo de ASP.NET y un policy provider sobre <c>IBackOfficeSecurityAccessor</c>.
///
/// Sin antiforgery — los endpoints aceptan POST simple porque la UI
/// de moderación va a vivir en una app cliente futura (CDN-hosted o
/// SPA backoffice). El gate de roles es la defensa primaria.
/// </remarks>
[ApiController]
[Route("api/comments/moderation")]
public sealed class CommentsModerationController : ControllerBase
{
    private const string ModeratorRolesCsv = "admin,moderator,editor";

    private readonly ICommentModeration _repository;
    private readonly IMemberAccessGate _gate;
    private readonly IAnalyticsTracker _analytics;

    public CommentsModerationController(
        ICommentModeration repository,
        IMemberAccessGate gate,
        IAnalyticsTracker analytics)
    {
        _repository = repository;
        _gate = gate;
        _analytics = analytics;
    }

    /// <summary>
    /// GET /api/comments/moderation/pending?limit=50 — cola global de
    /// comentarios sin aprobar a través de todos los nodos.
    /// </summary>
    [HttpGet("pending")]
    [AllowAnonymous]
    public ActionResult<IReadOnlyList<Comment>> GetPending([FromQuery] int limit = 50)
    {
        if (!_gate.HasAnyRole(ModeratorRolesCsv))
        {
            return Forbid();
        }

        return Ok(_repository.GetAllPending(limit));
    }

    /// <summary>
    /// GET /api/comments/moderation/{nodeId}/pending — cola de comentarios
    /// pendientes para un nodo específico (vista por post).
    /// </summary>
    [HttpGet("{nodeId:int}/pending")]
    [AllowAnonymous]
    public ActionResult<IReadOnlyList<Comment>> GetPendingForNode(int nodeId)
    {
        if (!_gate.HasAnyRole(ModeratorRolesCsv))
        {
            return Forbid();
        }

        return Ok(_repository.GetPendingForNode(nodeId));
    }

    /// <summary>
    /// POST /api/comments/moderation/{nodeId}/{commentId}/approve —
    /// aprueba un comentario pendiente.
    /// </summary>
    [HttpPost("{nodeId:int}/{commentId}/approve")]
    [AllowAnonymous]
    public async Task<IActionResult> Approve(
        int nodeId,
        string commentId,
        CancellationToken cancellationToken)
    {
        if (!_gate.HasAnyRole(ModeratorRolesCsv))
        {
            return Forbid();
        }

        var ok = await _repository.ApproveAsync(nodeId, commentId, cancellationToken);
        if (!ok)
        {
            return NotFound(new { error = "comment-not-found" });
        }

        _analytics.Track("comment.moderation.approved", new Dictionary<string, object?>
        {
            ["nodeId"] = nodeId,
            ["commentId"] = commentId,
            ["moderator"] = _gate.CurrentMemberDisplayName,
        });

        return Ok(new { approved = true });
    }

    /// <summary>
    /// POST /api/comments/moderation/{nodeId}/{commentId}/reject —
    /// elimina (rechaza) un comentario pendiente o ya aprobado.
    /// </summary>
    [HttpPost("{nodeId:int}/{commentId}/reject")]
    [AllowAnonymous]
    public async Task<IActionResult> Reject(
        int nodeId,
        string commentId,
        CancellationToken cancellationToken)
    {
        if (!_gate.HasAnyRole(ModeratorRolesCsv))
        {
            return Forbid();
        }

        var ok = await _repository.RejectAsync(nodeId, commentId, cancellationToken);
        if (!ok)
        {
            return NotFound(new { error = "comment-not-found" });
        }

        _analytics.Track("comment.moderation.rejected", new Dictionary<string, object?>
        {
            ["nodeId"] = nodeId,
            ["commentId"] = commentId,
            ["moderator"] = _gate.CurrentMemberDisplayName,
        });

        return Ok(new { rejected = true });
    }
}
