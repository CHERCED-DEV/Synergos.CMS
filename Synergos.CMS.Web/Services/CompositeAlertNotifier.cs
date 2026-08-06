using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Default <see cref="IAlertNotifier"/> que itera todos los
/// <see cref="IAlertNotifierChannel"/> registrados con try-catch por
/// canal. Pareja conceptual de Composite Comment/Form/Cart notifiers.
/// Cap-260 Batch B (Olas 254-256).
/// </summary>
public sealed class CompositeAlertNotifier : IAlertNotifier
{
    private readonly IEnumerable<IAlertNotifierChannel> _channels;
    private readonly ILogger<CompositeAlertNotifier> _logger;

    public CompositeAlertNotifier(
        IEnumerable<IAlertNotifierChannel> channels,
        ILogger<CompositeAlertNotifier> logger)
    {
        _channels = channels;
        _logger = logger;
    }

    public async Task NotifyAlertAsync(WebhookAlertEvent alert, CancellationToken cancellationToken)
    {
        foreach (var channel in _channels)
        {
            try
            {
                await channel.NotifyAlertAsync(alert, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Alert channel failed: channel={Channel} alertChannelName={AlertChannel}",
                    channel.GetType().Name, alert.ChannelName);
            }
        }
    }

    public async Task NotifyRecoveryAsync(WebhookRecoveryEvent recovery, CancellationToken cancellationToken)
    {
        foreach (var channel in _channels)
        {
            try
            {
                await channel.NotifyRecoveryAsync(recovery, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Alert recovery channel failed: channel={Channel} alertChannelName={AlertChannel}",
                    channel.GetType().Name, recovery.ChannelName);
            }
        }
    }
}
