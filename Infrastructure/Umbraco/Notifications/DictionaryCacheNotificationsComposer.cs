using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Notifications;

namespace Synergos.CMS.Infrastructure.Umbraco.Notifications;

/// <summary>
/// Wires <see cref="DictionaryCacheInvalidator"/> into Umbraco's notification pipeline.
/// Auto-discovered by <c>IUmbracoBuilder.AddComposers()</c> in <c>Program.cs</c>.
///
/// Subscribes the invalidator to both save and delete events so any backoffice
/// edit triggers immediate cache eviction.
/// </summary>
public sealed class DictionaryCacheNotificationsComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder
            .AddNotificationHandler<DictionaryItemSavedNotification,   DictionaryCacheInvalidator>()
            .AddNotificationHandler<DictionaryItemDeletedNotification, DictionaryCacheInvalidator>();
    }
}
