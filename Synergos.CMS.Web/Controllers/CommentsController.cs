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
/// elementCommentThread consume <see cref="ICommentRepository"/>
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
    private readonly ICommentRepository _repository;
    private readonly IMemberAccessGate _gate;
    private readonly InMemoryFormRateLimiter _rateLimiter;
    private readonly IOptions<CommentsSettings> _options;
    private readonly IAnalyticsTracker _analytics;

    public CommentsController(
        ICommentRepository repository,
        IMemberAccessGate gate,
        InMemoryFormRateLimiter rateLimiter,
        IOptions<CommentsSettings> options,
        IAnalyticsTracker analytics)
    {
        _repository = repository;
        _gate = gate;
        _rateLimiter = rateLimiter;
        _options = options;
        _analytics = analytics;
    }

    [HttpPost("{nodeId:int}")]
    [AllowAnonymous]
    [Consumes("application/x-www-form-urlencoded")]
    public async Task<IActionResult> Submit(
        int nodeId,
        string body,
        string? authorName,
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

        var comment = await _repository.AddAsync(
            new NewComment(
                NodeId: nodeId,
                MemberKey: memberKey,
                AuthorName: resolvedAuthor,
                Body: body),
            cancellationToken);

        _analytics.Track("comment.added", new Dictionary<string, object?>
        {
            ["nodeId"] = nodeId,
            ["approved"] = comment.Approved,
            ["bodyLength"] = comment.Body.Length,
        });

        var anchor = comment.Approved ? $"#comment-{comment.Id}" : "#comment-pending";
        return Redirect($"{referrer}{anchor}");
    }
}
