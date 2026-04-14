using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;
using Synergos.CMS.Domain.Services;

namespace Synergos.CMS.Infrastructure.Umbraco.Notifications;

/// <summary>
/// Invalidates <see cref="IDictionaryCache"/> when an editor saves or deletes a
/// dictionary item in the Umbraco backoffice.
///
/// Without this handler, dictionary edits would only become visible after the
/// configured TTL expires (Synergos:Cache:DictionaryMinutes). With this handler,
/// changes propagate immediately on save.
///
/// Registered in Program.cs via:
///     builder.AddNotificationHandler&lt;DictionaryItemSavedNotification, DictionaryCacheInvalidator&gt;();
///     builder.AddNotificationHandler&lt;DictionaryItemDeletedNotification, DictionaryCacheInvalidator&gt;();
/// </summary>
public sealed class DictionaryCacheInvalidator
    : INotificationHandler<DictionaryItemSavedNotification>,
      INotificationHandler<DictionaryItemDeletedNotification>
{
    private readonly IDictionaryCache _cache;
    private readonly ILogger<DictionaryCacheInvalidator> _logger;

    public DictionaryCacheInvalidator(
        IDictionaryCache cache,
        ILogger<DictionaryCacheInvalidator> logger)
    {
        _cache  = cache;
        _logger = logger;
    }

    public void Handle(DictionaryItemSavedNotification notification)
    {
        _cache.Invalidate();
        _logger.LogDebug(
            "DictionaryCacheInvalidator: invalidated cache after save of {Count} item(s).",
            notification.SavedEntities?.Count() ?? 0);
    }

    public void Handle(DictionaryItemDeletedNotification notification)
    {
        _cache.Invalidate();
        _logger.LogDebug(
            "DictionaryCacheInvalidator: invalidated cache after delete of {Count} item(s).",
            notification.DeletedEntities?.Count() ?? 0);
    }
}
