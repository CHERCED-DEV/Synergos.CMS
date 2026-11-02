using Microsoft.AspNetCore.Mvc;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Web.Controllers;

/// <summary>
/// Comentar desde la red social, y darle «me gusta» a un comentario (012-blogs-social, I0, P4 A): en el MISMO almacén y
/// con la MISMA regla que el hilo de la página del post.
/// </summary>
/// <remarks>
/// <para><b>Por qué hacía falta.</b> La app pintaba el comentario en local y no lo mandaba a ninguna parte: al recargar,
/// no estaba. El «me gusta» de un comentario, igual. La pantalla decía que había pasado algo que no había pasado.</para>
///
/// <para><b>Por qué es un controlador aparte y no una ruta más de <see cref="BlogsController"/>.</b> Ése recibe sólo la
/// cara de LECTURA del repositorio de comentarios (<see cref="ICommentReader"/>), y un gate lo fija: el render público del
/// blog no puede ver lo pendiente ni borrar nada. Comentar es la cara de ESCRITURA (<see cref="ICommentWriter"/>), la del
/// visitante, y es la única que recibe éste. Las rutas siguen bajo <c>api/blogs</c>, que es la base de la app.</para>
///
/// <para><b>La regla es la del hilo de la página</b> (<c>CommentsController</c>): el repositorio decide si nace aprobado
/// o pendiente (<c>Synergos:Comments:RequireModeration</c>), el mismo limitador por IP, y el aviso al moderador cuando
/// queda pendiente. Lo distinto es que aquí siempre hace falta sesión, como en todo lo que se escribe en la red, y que
/// quien comenta va por su cara pública: su llave como <c>MemberKey</c> y su nombre, nunca su correo.</para>
///
/// <para><b>El «me gusta» suma uno, como en la página</b>, sin recordar quién lo dio: el almacén no lo sabe. Una
/// reacción por persona llega con Engagement (I5).</para>
/// </remarks>
[ApiController]
[Route("api/blogs")]
public sealed class BlogsCommentsController : ControllerBase
{
    private readonly IContentStream _stream;
    private readonly ICommentWriter _comments;
    private readonly ISocialProfileProjection _profiles;
    private readonly IMemberAccessGate _gate;
    private readonly IIdentidadPublica _identidad;
    private readonly InMemoryFormRateLimiter _rateLimiter;
    private readonly ICommentModerationNotifier _moderationNotifier;
    private readonly IAnalyticsTracker _analytics;

    public BlogsCommentsController(
        IContentStream stream,
        ICommentWriter comments,
        ISocialProfileProjection profiles,
        IMemberAccessGate gate,
        IIdentidadPublica identidad,
        InMemoryFormRateLimiter rateLimiter,
        ICommentModerationNotifier moderationNotifier,
        IAnalyticsTracker analytics)
    {
        _stream = stream;
        _comments = comments;
        _profiles = profiles;
        _gate = gate;
        _identidad = identidad;
        _rateLimiter = rateLimiter;
        _moderationNotifier = moderationNotifier;
        _analytics = analytics;
    }

    // POST /api/blogs/post/{id}/comments { body, parentId? } → { comment, approved }
    [HttpPost("post/{id}/comments")]
    public async Task<IActionResult> Comment(string id, [FromBody] CommentRequest? request, CancellationToken cancellationToken)
    {
        var email = _gate.CurrentMemberEmail;
        if (!_gate.IsAuthenticated || string.IsNullOrWhiteSpace(email))
        {
            return Unauthorized(new { error = "Se requiere iniciar sesión." });
        }

        if (string.IsNullOrWhiteSpace(request?.Body))
        {
            return BadRequest(new { error = "El comentario no puede ir vacío." });
        }

        var item = await _stream.GetItemAsync(id, cancellationToken);
        if (item is null)
        {
            return NotFound(new { error = $"Post '{id}' no encontrado." });
        }

        var nodo = HiloDeUnaPublicacion.Nodo(item);
        if (!_rateLimiter.TryRegister(ClientIp(), $"comments:{nodo}"))
        {
            _analytics.Track("comment.rate-limited", new Dictionary<string, object?> { ["nodeId"] = nodo });
            return StatusCode(StatusCodes.Status429TooManyRequests, new { error = "Vas muy rápido. Intenta de nuevo en un momento." });
        }

        var cara = _identidad.De(email);
        await _identidad.RecordarAsync(email, cancellationToken);

        // Parse tolerante, como el hilo de la página: "N" o "D"; lo que no es un Guid es un comentario de primer nivel.
        Guid? parent = Guid.TryParse(request.ParentId, out var pg) ? pg : null;
        var comment = await _comments.AddAsync(
            new NewComment(NodeId: nodo, MemberKey: cara.Llave, AuthorName: cara.Nombre, Body: request.Body.Trim(), ParentId: parent),
            cancellationToken);

        _analytics.Track("comment.added", new Dictionary<string, object?>
        {
            ["nodeId"] = nodo,
            ["approved"] = comment.Approved,
            ["isReply"] = comment.ParentId.HasValue,
            ["bodyLength"] = comment.Body.Length,
        });

        if (!comment.Approved)
        {
            await _moderationNotifier.NotifyPendingAsync(comment, cancellationToken);
        }

        return Ok(new CommentResponse(
            Comment: await BlogsController.ToCommentDto(comment, _profiles, cancellationToken),
            Approved: comment.Approved));
    }

    // POST /api/blogs/post/{id}/comments/{commentId}/like → { commentId, likes }
    [HttpPost("post/{id}/comments/{commentId}/like")]
    public async Task<IActionResult> Like(string id, string commentId, CancellationToken cancellationToken)
    {
        if (!_gate.IsAuthenticated || string.IsNullOrWhiteSpace(_gate.CurrentMemberEmail))
        {
            return Unauthorized(new { error = "Se requiere iniciar sesión." });
        }

        var item = await _stream.GetItemAsync(id, cancellationToken);
        if (item is null)
        {
            return NotFound(new { error = $"Post '{id}' no encontrado." });
        }

        var nodo = HiloDeUnaPublicacion.Nodo(item);
        if (!_rateLimiter.TryRegister(ClientIp(), $"comment-like:{nodo}"))
        {
            return StatusCode(StatusCodes.Status429TooManyRequests, new { error = "Vas muy rápido. Intenta de nuevo en un momento." });
        }

        var updated = await _comments.LikeAsync(nodo, commentId, cancellationToken);
        if (updated is null)
        {
            return NotFound(new { error = "Ese comentario no está en el hilo." });
        }

        _analytics.Track("comment.liked", new Dictionary<string, object?> { ["nodeId"] = nodo, ["likes"] = updated.Likes });
        return Ok(new CommentLikeResponse(CommentId: updated.Id, Likes: updated.Likes, LikeCount: updated.Likes));
    }

    private string ClientIp() => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    /// <summary>
    /// <c>POST /api/blogs/post/{id}/comments</c>. Los dos son anulables a propósito: un <c>string</c> que no lo es se vuelve
    /// obligatorio con <c>[ApiController]</c>, y el modelo rechazaría con 400 lo que la app manda (014, I4).
    /// </summary>
    public sealed record CommentRequest(string? Body, string? ParentId = null);

    /// <param name="Approved">Si el comentario ya se ve en el hilo, o espera a un moderador. La app no lo pinta como
    /// publicado si no lo está.</param>
    public sealed record CommentResponse(BlogsController.CommentDto Comment, bool Approved);

    /// <param name="LikeCount">El mismo número que <paramref name="Likes"/>: es la clave que lee la app.</param>
    public sealed record CommentLikeResponse(string CommentId, int Likes, int LikeCount);
}
