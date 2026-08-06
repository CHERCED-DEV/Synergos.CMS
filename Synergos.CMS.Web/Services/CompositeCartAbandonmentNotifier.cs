using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Default <see cref="ICartAbandonmentNotifier"/> que itera todos los
/// <see cref="ICartAbandonmentNotifierChannel"/> registrados con
/// try-catch por canal. Pareja del CompositeComment/Form notifiers.
/// </summary>
public sealed class CompositeCartAbandonmentNotifier : ICartAbandonmentNotifier
{
    private readonly IEnumerable<ICartAbandonmentNotifierChannel> _channels;
    private readonly ILogger<CompositeCartAbandonmentNotifier> _logger;

    public CompositeCartAbandonmentNotifier(
        IEnumerable<ICartAbandonmentNotifierChannel> channels,
        ILogger<CompositeCartAbandonmentNotifier> logger)
    {
        _channels = channels;
        _logger = logger;
    }

    public async Task NotifyAbandonedAsync(AbandonedCart cart, CancellationToken cancellationToken)
    {
        foreach (var channel in _channels)
        {
            try
            {
                await channel.NotifyAbandonedAsync(cart, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Cart abandonment channel failed: channel={Channel} cartId={CartId}",
                    channel.GetType().Name, cart.CartId);
            }
        }
    }
}
