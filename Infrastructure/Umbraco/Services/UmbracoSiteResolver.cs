using Synergos.CMS.Application;
using System.Globalization;
using Umbraco.Extensions;
using Synergos.CMS.Application.MultiApp;
using Synergos.CMS.Domain.Services;
using Synergos.CMS.Schema.Constants;

namespace Synergos.CMS.Infrastructure.Umbraco.Services;

/// <summary>
/// Resolves the current site context using Umbraco's content tree.
/// Detects the application root via the <c>layoutIsApplicationRoot</c> flag,
/// falling back to the topmost root node.
/// </summary>
public sealed class UmbracoSiteResolver : ISiteResolver
{
    private readonly IContentContextAccessor _accessor;

    public UmbracoSiteResolver(IContentContextAccessor accessor) => _accessor = accessor;

    public SiteContext Resolve()
    {
        var layoutContext = LayoutContentResolver.Resolve(_accessor);
        var root = layoutContext.ApplicationRoot;

        if (root is null)
            return ResolveFallback();

        // Culture cascade: siteSettings.siteLanguage → siteRoot.defaultCulture → domain assignment.
        var siteSettings = root.FirstChild(ContentTypeKeys.Aliases.SiteSettingsAlias);
        var culture = (siteSettings is not null ? LayoutContentResolver.ReadText(siteSettings, "siteLanguage") : null)
            ?? LayoutContentResolver.ReadText(root, LayoutAliases.DefaultCulture)
            ?? root.GetCultureFromDomains()
            ?? "en-US";

        // Apply to thread so Umbraco Dictionary lookups and Razor @Umbraco.GetDictionaryValue() work.
        try
        {
            var ci = new CultureInfo(culture);
            Thread.CurrentThread.CurrentCulture   = ci;
            Thread.CurrentThread.CurrentUICulture = ci;
        }
        catch (CultureNotFoundException)
        {
            // Ignore invalid culture codes configured by editors
        }

        return new SiteContext(
            RootNodeId: root.Id,
            SiteName: LayoutContentResolver.ReadText(root, "siteName") ?? root.Name,
            Domain: root.Url() ?? "/",
            Culture: culture);
    }

    public SiteContext ResolveFallback()
        => new(RootNodeId: 0, SiteName: "Synergos", Domain: "/", Culture: "es");

    public IReadOnlyList<CultureOption> GetCultureOptions(string currentCulture)
    {
        var siteRoots = _accessor.GetAtRoot()
            .Where(n => n.ContentType.Alias == ContentTypeKeys.Aliases.SiteRoot)
            .ToList();

        if (siteRoots.Count < 2)
            return [];   // no switcher when only one site

        return siteRoots
            .Select(r =>
            {
                var culture = LayoutContentResolver.ReadText(r, LayoutAliases.DefaultCulture)
                              ?? r.GetCultureFromDomains()
                              ?? "en-US";
                var label   = culture.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? "EN" : "ES";
                return new CultureOption(
                    Label:    label,
                    Culture:  culture,
                    Url:      r.Url() ?? "/",
                    IsActive: string.Equals(culture, currentCulture, StringComparison.OrdinalIgnoreCase));
            })
            .ToList();
    }
}
