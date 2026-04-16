using Synergos.CMS.Application;
using Synergos.CMS.Application.MultiApp;
using Synergos.CMS.Application.Services;
using Synergos.CMS.Domain.Services;
using Synergos.CMS.Schema.Constants;
using Umbraco.Cms.Core.Models.PublishedContent;

namespace Synergos.CMS.Infrastructure.Umbraco.Services;

/// <summary>
/// Resolves the site's active alert bar.
///
/// v11.0.0 — reads from the active Layout Profile's <c>alertBars</c>
/// MultiContentPicker, selecting the first item whose schedule window
/// (startDate..endDate) is currently open. Falls back to legacy SiteSettings
/// Alert Bar tab when no profile is configured.
///
/// Returns null when no bar is active, title and description are both blank,
/// or the schedule excludes the current moment.
/// </summary>
public sealed class UmbracoAlertBarService : IAlertBarService
{
    private readonly IContentContextAccessor _accessor;

    public UmbracoAlertBarService(IContentContextAccessor accessor) => _accessor = accessor;

    public AlertBarConfig? GetAlertBarConfig(int rootNodeId)
    {
        // Primary path — profile.alertBars collection.
        var profile = LayoutContentResolver.ResolveLayoutProfile(_accessor, rootNodeId);
        if (profile is not null)
        {
            var bars = profile.Value<IEnumerable<IPublishedContent>>("alertBars")
                       ?? Enumerable.Empty<IPublishedContent>();

            var now = DateTime.Now;
            var firstActive = bars
                .Where(b => b.ContentType.Alias == ContentTypeKeys.Aliases.AlertBarConfig)
                .FirstOrDefault(b => IsScheduleActive(b, now));

            if (firstActive is not null)
                return BuildFromConfig(firstActive);
        }

        // Legacy fallback — SiteSettings Alert Bar tab.
        var ss = SiteSettingsAccessor.ResolveSiteSettings(_accessor, rootNodeId);
        if (ss is null || !ss.Value<bool>("alertEnabled")) return null;

        var title       = ss.Value<string>("alertTitle");
        var description = ss.Value<string>("alertDescription");

        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(description)) return null;

        return new AlertBarConfig(
            Title:       title,
            Description: description,
            CtaLabel:    ss.Value<string>("alertCtaLabel"),
            CtaUrl:      ss.Value<string>("alertCtaUrl"),
            Variant:     ss.Value<string>("alertVariant"),
            Dismissible: ss.Value<bool>("alertDismissible"));
    }

    private static bool IsScheduleActive(IPublishedContent bar, DateTime now)
    {
        var start = bar.Value<DateTime?>("startDate");
        var end   = bar.Value<DateTime?>("endDate");

        if (start.HasValue && now < start.Value) return false;
        if (end.HasValue   && now > end.Value)   return false;

        var title       = bar.Value<string>("alertTitle");
        var description = bar.Value<string>("alertDescription");
        return !string.IsNullOrWhiteSpace(title) || !string.IsNullOrWhiteSpace(description);
    }

    private static AlertBarConfig BuildFromConfig(IPublishedContent bar) => new(
        Title:       bar.Value<string>("alertTitle"),
        Description: bar.Value<string>("alertDescription"),
        CtaLabel:    bar.Value<string>("alertCtaLabel"),
        CtaUrl:      bar.Value<string>("alertCtaUrl"),
        Variant:     bar.Value<string>("variant"),
        Dismissible: bar.Value<bool>("dismissible"));
}
