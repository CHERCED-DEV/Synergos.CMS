using Microsoft.Extensions.Logging;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Default <see cref="IFormSubmissionNotifier"/> que itera todos los
/// <see cref="IFormSubmissionNotifierChannel"/> registrados (email,
/// webhook, custom adapters). Pareja del CompositeCommentModerationNotifier.
/// </summary>
/// <remarks>
/// Try-catch por canal — un canal roto NO afecta a los demás. Logea
/// Warning con el tipo del canal para debugging.
/// </remarks>
public sealed class CompositeFormSubmissionNotifier : IFormSubmissionNotifier
{
    private readonly IEnumerable<IFormSubmissionNotifierChannel> _channels;
    private readonly ILogger<CompositeFormSubmissionNotifier> _logger;

    public CompositeFormSubmissionNotifier(
        IEnumerable<IFormSubmissionNotifierChannel> channels,
        ILogger<CompositeFormSubmissionNotifier> logger)
    {
        _channels = channels;
        _logger = logger;
    }

    public async Task NotifySubmittedAsync(
        FormSubmissionRequest request,
        FormSubmissionResult result,
        CancellationToken cancellationToken)
    {
        foreach (var channel in _channels)
        {
            try
            {
                await channel.NotifySubmittedAsync(request, result, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Form submission channel failed: channel={Channel} formKey={FormKey}",
                    channel.GetType().Name, request.FormKey);
            }
        }
    }
}
