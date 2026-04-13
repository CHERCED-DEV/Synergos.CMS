using Umbraco.Cms.Core.Models.PublishedContent;
using Synergos.CMS.Domain.Services;
using Synergos.CMS.Schema.Constants;

namespace Synergos.CMS.Application.Services;

/// <summary>
/// Shared content-tree helpers used by all focused layout services.
/// Centralises the SiteRoot → SiteSettings and PlatformRoot → GlobalSettings
/// traversal logic so each service does not repeat it.
/// </summary>
internal static class SiteSettingsAccessor
{
    /// <summary>
    /// Returns the SiteSettings child of the given SiteRoot node, or null when the
    /// content cache is unavailable or the node hierarchy is not yet populated.
    /// </summary>
    internal static IPublishedContent? ResolveSiteSettings(IContentContextAccessor accessor, int rootNodeId)
        => accessor.GetById(rootNodeId)
                   ?.FirstChild(ContentTypeKeys.Aliases.SiteSettingsAlias);

    /// <summary>
    /// Walks from a SiteRoot node up to its PlatformRoot parent and returns the
    /// GlobalSettings child. Returns null when the hierarchy is unavailable.
    /// </summary>
    internal static IPublishedContent? ResolveGlobalSettings(IContentContextAccessor accessor, int siteRootId)
    {
        var siteRoot = accessor.GetById(siteRootId);
        var platform = siteRoot?.Parent;
        if (platform is null || !string.Equals(
                platform.ContentType.Alias,
                ContentTypeKeys.Aliases.PlatformRoot,
                StringComparison.OrdinalIgnoreCase))
            return null;

        return platform.FirstChild(ContentTypeKeys.Aliases.GlobalSettings);
    }

    /// <summary>
    /// Builds the ordered list of social links from SiteSettings social URL properties.
    /// Entries with an empty URL are excluded.
    /// </summary>
    internal static IReadOnlyList<SocialLinkConfig> BuildSocialLinks(IPublishedContent siteSettings)
    {
        var links = new List<SocialLinkConfig>();

        void Add(string alias, string label, string platform)
        {
            var url = siteSettings.Value<string>(alias);
            if (!string.IsNullOrWhiteSpace(url))
                links.Add(new SocialLinkConfig(label, url, platform));
        }

        Add("linkedinUrl",  "LinkedIn",  "linkedin");
        Add("twitterUrl",   "Twitter/X", "twitter");
        Add("instagramUrl", "Instagram", "instagram");
        Add("facebookUrl",  "Facebook",  "facebook");
        Add("youtubeUrl",   "YouTube",   "youtube");
        Add("tiktokUrl",    "TikTok",    "tiktok");

        return links;
    }
}
