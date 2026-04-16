using Synergos.CMS.Application;
using Umbraco.Cms.Core.Models.PublishedContent;
using Synergos.CMS.Application.MultiApp;
using Synergos.CMS.Application.Services;
using Synergos.CMS.Domain.Services;
using Synergos.CMS.Schema.Constants;

namespace Synergos.CMS.Infrastructure.Umbraco.Services;

/// <summary>
/// Resolves the active promotional banner.
///
/// v11.0.0 — reads from the active Layout Profile's <c>banner</c> picker
/// (BannerConfig node with embedded schedule + ReusableBlock picker). Falls
/// back to legacy SiteSettings "Banners & Campaigns" tab when no profile is
/// configured.
///
/// Returns null when no banner is configured or the schedule excludes now.
/// </summary>
public sealed class UmbracoBannerService : IBannerService
{
    private readonly IContentContextAccessor _accessor;

    public UmbracoBannerService(IContentContextAccessor accessor) => _accessor = accessor;

    public BannerConfig? GetBannerConfig(int rootNodeId)
    {
        var now = DateTime.Now;

        // Primary path — profile.banner → BannerConfig node.
        var viaProfile = TryResolveViaProfile(rootNodeId, now);
        if (viaProfile is not null) return viaProfile;

        // Legacy fallback — SiteSettings "Banners & Campaigns" tab.
        return TryResolveLegacy(rootNodeId, now);
    }

    private BannerConfig? TryResolveViaProfile(int rootNodeId, DateTime now)
    {
        var profile      = LayoutContentResolver.ResolveLayoutProfile(_accessor, rootNodeId);
        var bannerConfig = profile is null
            ? null
            : LayoutContentResolver.ReadPickerContent(profile, "banner");

        if (bannerConfig is null ||
            bannerConfig.ContentType.Alias != ContentTypeKeys.Aliases.BannerConfig)
            return null;

        if (!IsWithinWindow(bannerConfig.Value<DateTime?>("startDate"),
                            bannerConfig.Value<DateTime?>("endDate"), now))
            return null;

        var block = LayoutContentResolver.ReadPickerContent(bannerConfig, "bannerBlock");
        return block is null ? null : new BannerConfig(block.Id);
    }

    private BannerConfig? TryResolveLegacy(int rootNodeId, DateTime now)
    {
        var ss = SiteSettingsAccessor.ResolveSiteSettings(_accessor, rootNodeId);
        if (ss is null || !ss.Value<bool>("globalBannerEnabled")) return null;

        if (!IsWithinWindow(ss.Value<DateTime?>("campaignStartDate"),
                            ss.Value<DateTime?>("campaignEndDate"), now))
            return null;

        var node = ss.Value<IPublishedContent>("globalBanner");
        return node is null ? null : new BannerConfig(node.Id);
    }

    private static bool IsWithinWindow(DateTime? start, DateTime? end, DateTime now)
    {
        if (start.HasValue && now < start.Value) return false;
        if (end.HasValue   && now > end.Value)   return false;
        return true;
    }
}
