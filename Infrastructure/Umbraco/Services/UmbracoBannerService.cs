using Synergos.CMS.Application;
using Umbraco.Cms.Core.Models.PublishedContent;
using Synergos.CMS.Application.Services;
using Synergos.CMS.Domain.Services;

namespace Synergos.CMS.Infrastructure.Umbraco.Services;

/// <summary>
/// Resolves the global promotional banner configuration from SiteSettings.
///
/// The banner is active only when:
///   - globalBannerEnabled = true
///   - Current date is within [campaignStartDate, campaignEndDate] window (both optional)
///   - A ReusableBlock is picked in the globalBanner field
/// </summary>
public sealed class UmbracoBannerService : IBannerService
{
    private readonly IContentContextAccessor _accessor;

    public UmbracoBannerService(IContentContextAccessor accessor) => _accessor = accessor;

    public BannerConfig? GetBannerConfig(int rootNodeId)
    {
        var ss = SiteSettingsAccessor.ResolveSiteSettings(_accessor, rootNodeId);
        if (ss is null || !ss.Value<bool>("globalBannerEnabled")) return null;

        var now   = DateTime.Now;
        var start = ss.Value<DateTime?>("campaignStartDate");
        var end   = ss.Value<DateTime?>("campaignEndDate");

        if (start.HasValue && now < start.Value) return null;
        if (end.HasValue   && now > end.Value)   return null;

        var bannerNode = ss.Value<IPublishedContent>("globalBanner");
        return bannerNode is null ? null : new BannerConfig(bannerNode.Id);
    }
}
