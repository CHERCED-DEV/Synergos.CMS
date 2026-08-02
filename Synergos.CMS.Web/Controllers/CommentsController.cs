using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Web.Controllers;

/// <summary>
/// Endpoint POST para crear comentarios sobre nodos publicados (ADR 0038).
/// GET de lectura no se expone — el renderer SSR del block
/// elementCommentThread consume <see cref="ICommentWriter"/>
/// directamente.
/// </summary>
/// <remarks>
/// Auth gated by <see cref="CommentsSettings.RequireAuthentication"/>:
/// si true (default), requiere member autenticado vía
/// <see cref="IMemberAccessGate"/>. Si false, acepta anonimo (autor
/// del form provee Name).
///
/// Rate-limit reusa <see cref="InMemoryFormRateLimiter"/> sliding-window
/// con key compuesta "comments:" + IP. Comparte la misma defensa-en-capas
/// del módulo Forms (Ola 60).
///
/// Sin CSRF — comments son por design open posts. Honeypot opcional via
/// hidden field si el sitio lo necesita (no implementado en primer pase).
/// </remarks>
[ApiController]
[Route("api/comments")]
public sealed class CommentsController : ControllerBase
{
    private readonly ICommentWriter _repository;
    private readonly IMemberAccessGate _gate;
    private readonly InMemoryFormRateLimiter _rateLimiter;
    private readonly IOptions<CommentsSettings> _options;
    private readonly IAnalyticsTracker _analytics;
    private readonly ICommentModerationNotifier _moderationNotifier;

    public CommentsController(
        ICommentWriter repository,
        IMemberAccessGate gate,
        InMemoryFormRateLimiter rateLimiter,
        IOptions<CommentsSettings> options,
        IAnalyticsTracker analytics,
        ICommentModerationNotifier moderationNotifier)
    {
        _repository = repository;
        _gate = gate;
        _rateLimiter = rateLimiter;
        _options = options;
        _analytics = analytics;
        _moderationNotifier = moderationNotifier;
    }

    [HttpPost("{nodeId:int}")]
    [AllowAnonymous]
    [Consumes("application/x-www-form-urlencoded")]
    public async Task<IActionResult> Submit(
        int nodeId,
        [FromForm] string body,
        [FromForm] string? authorName,
        [FromForm] string? parentId,
        CancellationToken cancellationToken)
    {
        var settings = _options.Value;
        var referrer = Request.Headers.Referer.ToString();
        if (string.IsNullOrWhiteSpace(referrer))
        {
            referrer = "/";
        }

        if (settings.RequireAuthentication && !_gate.IsAuthenticated)
        {
            return Redirect($"{referrer}#comment-error-not-authenticated");
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return Redirect($"{referrer}#comment-error-empty");
        }

        var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        if (!_rateLimiter.TryRegister(clientIp, $"comments:{nodeId}"))
        {
            _analytics.Track("comment.rate-limited", new Dictionary<string, object?>
            {
                ["nodeId"] = nodeId,
            });
            return Redirect($"{referrer}#comment-error-rate-limited");
        }

        var resolvedAuthor = settings.RequireAuthentication
            ? (_gate.CurrentMemberDisplayName ?? "Anonymous")
            : (string.IsNullOrWhiteSpace(authorName) ? "Anonymous" : authorName.Trim());
        var memberKey = settings.RequireAuthentication
            ? _gate.CurrentMemberDisplayName
            : null;

        // Ola 230 (ADR 0100): parentId opcional para respuestas anidadas.
        // Parse tolerante — "N" hex (formato de Comment.Id) o "D"; valor
        // inválido/vacío ⇒ null (top-level). El repository normaliza a
        // 2 niveles y degrada parents fantasma.
        Guid? parsedParent = Guid.TryParse(parentId, out var pg) ? pg : null;

        var comment = await _repository.AddAsync(
            new NewComment(
                NodeId: nodeId,
                MemberKey: memberKey,
                AuthorName: resolvedAuthor,
                Body: body,
                ParentId: parsedParent),
            cancellationToken);

        _analytics.Track("comment.added", new Dictionary<string, object?>
        {
            ["nodeId"] = nodeId,
            ["approved"] = comment.Approved,
            ["isReply"] = comment.ParentId.HasValue,
            ["bodyLength"] = comment.Body.Length,
        });

        // Ola 89 — fire-and-forget notificación al moderador si el
        // comentario quedó pendiente. El notifier es no-op cuando
        // CommentsSettings.NotifyEmailAddress está vacío.
        if (!comment.Approved)
        {
            await _moderationNotifier.NotifyPendingAsync(comment, cancellationToken);
        }

        var anchor = comment.Approved ? $"#comment-{comment.Id}" : "#comment-pending";
        return Redirect($"{referrer}{anchor}");
    }

    /// <summary>
    /// Incrementa el contador de "me gusta" de un comentario (Ola 230,
    /// ADR 0100). POST sin body — el comentario se identifica por
    /// nodeId + commentId en la ruta. Redirige de vuelta al hilo anclado
    /// en el comentario. No requiere auth (reacción ligera, anónima);
    /// rate-limited por IP igual que el POST de creación.
    /// </summary>
    [HttpPost("{nodeId:int}/{commentId}/like")]
    [AllowAnonymous]
    public async Task<IActionResult> Like(
        int nodeId,
        string commentId,
        CancellationToken cancellationToken)
    {
        var referrer = Request.Headers.Referer.ToString();
        if (string.IsNullOrWhiteSpace(referrer))
        {
            referrer = "/";
        }

        var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        if (!_rateLimiter.TryRegister(clientIp, $"comment-like:{nodeId}"))
        {
            return Redirect($"{referrer}#comment-error-rate-limited");
        }

        var updated = await _repository.LikeAsync(nodeId, commentId, cancellationToken);
        if (updated is null)
        {
            return Redirect($"{referrer}#comment-error-not-found");
        }

        _analytics.Track("comment.liked", new Dictionary<string, object?>
        {
            ["nodeId"] = nodeId,
            ["likes"] = updated.Likes,
        });

        return Redirect($"{referrer}#comment-{updated.Id}");
    }
}
