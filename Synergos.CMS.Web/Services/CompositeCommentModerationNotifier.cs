using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Default <see cref="ICommentModerationNotifier"/> que itera todos
/// los <see cref="ICommentModerationNotifierChannel"/> registrados
/// (email, webhook, custom adapters) y los notifica en paralelo.
/// </summary>
/// <remarks>
/// Try-catch por canal — un canal roto NO afecta a los demás. Logea
/// Warning con el tipo del canal que falló para debugging.
///
/// Sin orden garantizado. Para canales que requieren orden (ej.
/// notificar Slack antes que email), implementar custom composite
/// que reciba IEnumerable y los procese secuencial.
/// </remarks>
public sealed class CompositeCommentModerationNotifier : ICommentModerationNotifier
{
    private readonly IEnumerable<ICommentModerationNotifierChannel> _channels;
    private readonly ILogger<CompositeCommentModerationNotifier> _logger;

    public CompositeCommentModerationNotifier(
        IEnumerable<ICommentModerationNotifierChannel> channels,
        ILogger<CompositeCommentModerationNotifier> logger)
    {
        _channels = channels;
        _logger = logger;
    }

    public async Task NotifyPendingAsync(Comment comment, CancellationToken cancellationToken)
    {
        foreach (var channel in _channels)
        {
            try
            {
                await channel.NotifyPendingAsync(comment, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Comment moderation channel failed: channel={Channel} nodeId={NodeId} commentId={CommentId}",
                    channel.GetType().Name, comment.NodeId, comment.Id);
            }
        }
    }
}
