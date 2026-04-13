using Synergos.CMS.Application;
using Synergos.CMS.Application.Services;
using Synergos.CMS.Domain.Services;

namespace Synergos.CMS.Infrastructure.Umbraco.Services;

/// <summary>
/// Resolves tracking and analytics script configuration.
///
/// Script cascade (platform-wide scripts prepend site-specific ones):
///   GlobalSettings.headScripts    → SiteSettings.siteHeadScripts
///   GlobalSettings.bodyEndScripts → SiteSettings.siteBodyEndScripts
///
/// GTM cascade:
///   SiteSettings.siteGtmId → GlobalSettings.gtmContainerId
///
/// When a GTM container ID is present, the standard GTM head/body snippets are
/// automatically injected before any custom scripts.
/// </summary>
public sealed class UmbracoTrackingService : ITrackingService
{
    private readonly IContentContextAccessor _accessor;

    public UmbracoTrackingService(IContentContextAccessor accessor) => _accessor = accessor;

    public TrackingScriptsConfig GetTrackingScripts(int rootNodeId)
    {
        var ss = SiteSettingsAccessor.ResolveSiteSettings(_accessor, rootNodeId);
        if (ss is null) return new TrackingScriptsConfig(null, null);

        var gs = SiteSettingsAccessor.ResolveGlobalSettings(_accessor, rootNodeId);

        // GTM cascade: site-level wins; global is fallback for multi-site shared container.
        var gtmId = ss.Value<string>("siteGtmId");
        if (string.IsNullOrWhiteSpace(gtmId))
            gtmId = gs?.Value<string>("gtmContainerId");

        var headScripts = ss.Value<string>("siteHeadScripts");
        var bodyScripts = ss.Value<string>("siteBodyEndScripts");

        // Platform-wide scripts are prepended so they execute before site-specific ones.
        var platformHead = gs?.Value<string>("headScripts");
        var platformBody = gs?.Value<string>("bodyEndScripts");

        if (!string.IsNullOrWhiteSpace(platformHead))
            headScripts = string.IsNullOrWhiteSpace(headScripts)
                ? platformHead
                : platformHead + "\n" + headScripts;
        if (!string.IsNullOrWhiteSpace(platformBody))
            bodyScripts = string.IsNullOrWhiteSpace(bodyScripts)
                ? platformBody
                : platformBody + "\n" + bodyScripts;

        if (!string.IsNullOrWhiteSpace(gtmId))
        {
            var gtmHead =
                $"<!-- Google Tag Manager -->" +
                $"<script>(function(w,d,s,l,i){{w[l]=w[l]||[];w[l].push({{'gtm.start':new Date().getTime(),event:'gtm.js'}});" +
                $"var f=d.getElementsByTagName(s)[0],j=d.createElement(s),dl=l!='dataLayer'?'&l='+l:'';" +
                $"j.async=true;j.src='https://www.googletagmanager.com/gtm.js?id='+i+dl;" +
                $"f.parentNode.insertBefore(j,f)}})(window,document,'script','dataLayer','{gtmId}');</script>" +
                $"<!-- End Google Tag Manager -->";

            var gtmBody =
                $"<!-- Google Tag Manager (noscript) -->" +
                $"<noscript><iframe src=\"https://www.googletagmanager.com/ns.html?id={gtmId}\"" +
                $" height=\"0\" width=\"0\" style=\"display:none;visibility:hidden\"></iframe></noscript>" +
                $"<!-- End Google Tag Manager (noscript) -->";

            headScripts = string.IsNullOrWhiteSpace(headScripts) ? gtmHead : gtmHead + "\n" + headScripts;
            bodyScripts = string.IsNullOrWhiteSpace(bodyScripts) ? gtmBody : gtmBody + "\n" + bodyScripts;
        }

        return new TrackingScriptsConfig(headScripts, bodyScripts);
    }
}
