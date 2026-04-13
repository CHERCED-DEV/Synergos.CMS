using Synergos.CMS.Application;
using Synergos.CMS.Application.Services;
using Synergos.CMS.Domain.Services;

namespace Synergos.CMS.Infrastructure.Umbraco.Services;

/// <summary>
/// Resolves the site-wide alert bar configuration from SiteSettings.
///
/// Returns null when alertEnabled is false or when neither title nor description
/// are configured (prevents rendering an empty bar).
/// </summary>
public sealed class UmbracoAlertBarService : IAlertBarService
{
    private readonly IContentContextAccessor _accessor;

    public UmbracoAlertBarService(IContentContextAccessor accessor) => _accessor = accessor;

    public AlertBarConfig? GetAlertBarConfig(int rootNodeId)
    {
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
}
