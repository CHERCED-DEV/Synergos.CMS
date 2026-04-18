using Synergos.CMS.Interfaces;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;

namespace Synergos.CMS.Web.Notifications;

/// <summary>
/// Invalidates cached Dictionary items when Umbraco publishes a
/// dictionary save or delete, keeping the in-memory
/// <see cref="IDictionaryCache"/> coherent with backoffice edits.
/// </summary>
/// <remarks>
/// Per ADR 0005 and ADR 0009 the cache itself lives in
/// <c>Synergos.CMS.Application</c> as <see cref="IDictionaryCache"/>;
/// this handler is a Web-side adapter that translates Umbraco
/// notifications into invalidation calls. The handler runs synchronously
/// — invalidation is an O(keys) in-memory operation.
/// </remarks>
public sealed class DictionaryCacheInvalidator
    : INotificationHandler<DictionaryItemSavedNotification>,
      INotificationHandler<DictionaryItemDeletedNotification>
{
    private readonly IDictionaryCache _cache;

    public DictionaryCacheInvalidator(IDictionaryCache cache) => _cache = cache;

    public void Handle(DictionaryItemSavedNotification notification) =>
        InvalidateKeys(notification.SavedEntities.Select(e => e.ItemKey));

    public void Handle(DictionaryItemDeletedNotification notification) =>
        InvalidateKeys(notification.DeletedEntities.Select(e => e.ItemKey));

    /// <summary>
    /// Invalidates the supplied dictionary item keys. Exposed internally
    /// so unit tests can exercise the invalidation behaviour without
    /// constructing Umbraco notification objects.
    /// </summary>
    internal void InvalidateKeys(IEnumerable<string> keys)
    {
        foreach (var key in keys)
        {
            if (!string.IsNullOrWhiteSpace(key))
            {
                _cache.Invalidate(key);
            }
        }
    }
}
