using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;
using uSync.BackOffice;

namespace Synergos.CMS.Infrastructure.USync;

/// <summary>
/// Wires the uSync file-cleaning infrastructure into Umbraco startup.
/// Discovered automatically by Umbraco's composer scan (.AddComposers()).
///
/// Three protection layers:
///   1. UmbracoApplicationStarting → deduplicates before ImportOnStartup.
///   2. uSyncImportStartingNotification → deduplicates before any manual
///      Import triggered from the backoffice.
///   3. uSyncExportCompletedNotification → deduplicates immediately after
///      Export so the folder is clean before a subsequent Import.
///   4. ContentDeleted / MediaDeleted → removes the orphaned config file
///      when an item is deleted, preventing "delete → re-upload" clashes.
/// </summary>
public sealed class USyncComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services.AddSingleton<USyncFileCleanerService>();

        // Layer 1 — before uSync ImportOnStartup (earliest possible hook)
        builder.AddNotificationAsyncHandler<
            UmbracoApplicationStartingNotification,
            USyncStartupCleanerHandler>();

        // Layer 2 — before every manual Import from the backoffice
        builder.AddNotificationHandler<
            uSyncImportStartingNotification,
            USyncImportStartingHandler>();

        // Layer 3 — immediately after every Export
        builder.AddNotificationHandler<
            uSyncExportCompletedNotification,
            USyncExportCompletedHandler>();

        // Layer 4 — when content/media is deleted in the backoffice
        builder.AddNotificationHandler<ContentDeletedNotification, USyncDeletionHandler>();
        builder.AddNotificationHandler<MediaDeletedNotification,   USyncDeletionHandler>();
    }
}

// ── Layer 1: Startup ─────────────────────────────────────────────────────────

internal sealed class USyncStartupCleanerHandler
    : INotificationAsyncHandler<UmbracoApplicationStartingNotification>
{
    private readonly USyncFileCleanerService _cleaner;
    private readonly ILogger<USyncStartupCleanerHandler> _logger;

    public USyncStartupCleanerHandler(USyncFileCleanerService cleaner, ILogger<USyncStartupCleanerHandler> logger)
    {
        _cleaner = cleaner;
        _logger  = logger;
    }

    public Task HandleAsync(UmbracoApplicationStartingNotification notification, CancellationToken cancellationToken)
    {
        try   { _cleaner.DeduplicateAll(); }
        catch (Exception ex) { _logger.LogError(ex, "[uSync cleaner] Startup deduplication failed."); }
        return Task.CompletedTask;
    }
}

// ── Layer 2: Before manual Import ────────────────────────────────────────────

/// <summary>
/// Fires before every Import triggered from the uSync backoffice UI or API.
/// Ensures the folder is duplicate-free regardless of whether the CMS was
/// restarted since the last Export.
/// </summary>
internal sealed class USyncImportStartingHandler
    : INotificationHandler<uSyncImportStartingNotification>
{
    private readonly USyncFileCleanerService _cleaner;
    private readonly ILogger<USyncImportStartingHandler> _logger;

    public USyncImportStartingHandler(USyncFileCleanerService cleaner, ILogger<USyncImportStartingHandler> logger)
    {
        _cleaner = cleaner;
        _logger  = logger;
    }

    public void Handle(uSyncImportStartingNotification notification)
    {
        try   { _cleaner.DeduplicateAll(); }
        catch (Exception ex) { _logger.LogError(ex, "[uSync cleaner] Pre-import deduplication failed."); }
    }
}

// ── Layer 3: After Export ─────────────────────────────────────────────────────

/// <summary>
/// Fires after every Export from the uSync backoffice UI or API.
/// Export writes new files but leaves old files for renamed items in place.
/// Running deduplication here ensures the folder is clean before the next
/// Import, even if the user does Export → Import in the same session.
/// </summary>
internal sealed class USyncExportCompletedHandler
    : INotificationHandler<uSyncExportCompletedNotification>
{
    private readonly USyncFileCleanerService _cleaner;
    private readonly ILogger<USyncExportCompletedHandler> _logger;

    public USyncExportCompletedHandler(USyncFileCleanerService cleaner, ILogger<USyncExportCompletedHandler> logger)
    {
        _cleaner = cleaner;
        _logger  = logger;
    }

    public void Handle(uSyncExportCompletedNotification notification)
    {
        try   { _cleaner.DeduplicateAll(); }
        catch (Exception ex) { _logger.LogError(ex, "[uSync cleaner] Post-export deduplication failed."); }
    }
}

// ── Layer 4: Content/Media deletion ──────────────────────────────────────────

/// <summary>
/// When content or media is permanently deleted from the backoffice,
/// removes its uSync config file so a re-upload with the same name
/// does not create a GUID collision on the next Import.
/// </summary>
internal sealed class USyncDeletionHandler
    : INotificationHandler<ContentDeletedNotification>,
      INotificationHandler<MediaDeletedNotification>
{
    private readonly USyncFileCleanerService _cleaner;

    public USyncDeletionHandler(USyncFileCleanerService cleaner) => _cleaner = cleaner;

    public void Handle(ContentDeletedNotification notification)
    {
        foreach (var item in notification.DeletedEntities)
            _cleaner.DeleteOrphanedFile(item.Key, isMedia: false);
    }

    public void Handle(MediaDeletedNotification notification)
    {
        foreach (var item in notification.DeletedEntities)
            _cleaner.DeleteOrphanedFile(item.Key, isMedia: true);
    }
}
