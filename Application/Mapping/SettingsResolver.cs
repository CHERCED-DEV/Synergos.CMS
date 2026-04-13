using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Extensions;
using Synergos.CMS.Schema.Constants;

namespace Synergos.CMS.Application.Mapping;

/// <summary>
/// Resolves settings with inheritance: SiteSettings → GlobalSettings → fallback.
///
/// Usage from any page:
///   var resolver = new SettingsResolver(page);
///   var gtm = resolver.Site("siteGtmId") ?? resolver.Global("gtmContainerId");
///
/// The resolver walks up the content tree once and caches the settings nodes.
/// </summary>
public sealed class SettingsResolver
{
    public IPublishedContent? SiteRoot { get; }
    public IPublishedContent? SiteSettingsNode { get; }
    public IPublishedContent? ThemeSettingsNode { get; }
    public IPublishedContent? PlatformRoot { get; }
    public IPublishedContent? GlobalSettingsNode { get; }

    public SettingsResolver(IPublishedContent page)
    {
        SiteRoot = page.AncestorOrSelf(ContentTypeKeys.Aliases.SiteRoot);
        SiteSettingsNode = SiteRoot?.FirstChild(ContentTypeKeys.Aliases.SiteSettingsAlias);
        ThemeSettingsNode = SiteRoot?.FirstChild(ContentTypeKeys.Aliases.ThemeSettings);

        PlatformRoot = page.AncestorOrSelf(ContentTypeKeys.Aliases.PlatformRoot) ?? page.Root();
        GlobalSettingsNode = PlatformRoot?.FirstChild(ContentTypeKeys.Aliases.GlobalSettings);
    }

    /// <summary>Read a value from SiteSettings.</summary>
    public T? Site<T>(string alias) => SiteSettingsNode is not null
        ? SiteSettingsNode.Value<T>(alias)
        : default;

    /// <summary>Read a string from SiteSettings.</summary>
    public string? Site(string alias) => Site<string>(alias);

    /// <summary>Read a value from GlobalSettings.</summary>
    public T? Global<T>(string alias) => GlobalSettingsNode is not null
        ? GlobalSettingsNode.Value<T>(alias)
        : default;

    /// <summary>Read a string from GlobalSettings.</summary>
    public string? Global(string alias) => Global<string>(alias);

    /// <summary>Read a value from ThemeSettings.</summary>
    public T? Theme<T>(string alias) => ThemeSettingsNode is not null
        ? ThemeSettingsNode.Value<T>(alias)
        : default;

    /// <summary>Read a string from ThemeSettings.</summary>
    public string? Theme(string alias) => Theme<string>(alias);

    /// <summary>
    /// Resolve with fallback chain: SiteSettings → GlobalSettings → default.
    /// </summary>
    public string? Resolve(string siteAlias, string globalAlias, string? fallback = null)
        => Site(siteAlias) ?? Global(globalAlias) ?? fallback;

    /// <summary>
    /// Read a SiteRoot-level property (siteName, siteTagline, siteLogo, footerCopy).
    /// </summary>
    public T? Root<T>(string alias) => SiteRoot is not null
        ? SiteRoot.Value<T>(alias)
        : default;

    public string? Root(string alias) => Root<string>(alias);
}
