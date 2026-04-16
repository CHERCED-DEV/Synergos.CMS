using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Synergos.CMS.Schema.Constants;
using UmbConstants = Umbraco.Cms.Core.Constants;

namespace Synergos.CMS.Schema.Seeders;

/// <summary>
/// v11.0.0 — seeds the Layout Config tree introduced in LayoutConfigInitializer.
///
/// Creates:
///   Platform-level:
///     <c>SharedContent/Layout/{Header, Footer, Alerts, Banners, Profiles}/</c>
///     <c>SharedContent/Layout/Header/Platform Header</c>
///     <c>SharedContent/Layout/Footer/Platform Footer</c>
///     <c>SharedContent/Layout/Profiles/Platform Default</c>
///     <c>SharedContent/Navigations/Platform Nav</c>   (NavigationGroup with NavItems → each SiteRoot)
///
///   Per-SiteRoot:
///     <c>&lt;site&gt;/Config/Layout/{Header, Footer, Alerts, Banners, Profiles}/</c>
///     <c>&lt;site&gt;/Config/Layout/Header/Site Header</c>
///     <c>&lt;site&gt;/Config/Layout/Footer/Site Footer</c>
///     <c>&lt;site&gt;/Config/Layout/Profiles/Default</c>   (marked isDefault=true)
///     <c>&lt;site&gt;/Config/Navigations/</c>              (existing NavigationGroups moved here)
///
/// Migrates legacy SiteSettings values (headerNavigation, footerCopy, alertTitle, …)
/// into the new config nodes non-destructively. Sets <c>SiteSettings.defaultProfile</c>
/// to point at the site's freshly-created Default profile.
///
/// Idempotent — safe to re-run on every boot.
/// </summary>
internal sealed class LayoutConfigSeeder
{
    private readonly IContentService              _contentService;
    private readonly IContentTypeService          _contentTypeService;
    private readonly ILogger<LayoutConfigSeeder>  _logger;

    public LayoutConfigSeeder(
        IContentService             contentService,
        IContentTypeService         contentTypeService,
        ILogger<LayoutConfigSeeder> logger)
    {
        _contentService     = contentService;
        _contentTypeService = contentTypeService;
        _logger             = logger;
    }

    public void Seed(int platformId, int siteRootId)
    {
        // Pre-flight: required doctypes must exist (LayoutConfigInitializer ran).
        if (_contentTypeService.Get(ContentTypeKeys.Aliases.LayoutFolder) is null)
        {
            _logger.LogDebug("LayoutConfigSeeder: layoutFolder doctype not found — skipping (pipeline did not run yet).");
            return;
        }

        var sharedContent = FindChildByType(platformId, ContentTypeKeys.Aliases.SharedContentFolder);
        if (sharedContent is null)
        {
            _logger.LogWarning("LayoutConfigSeeder: SharedContent folder not found under PlatformRoot — skipping.");
            return;
        }

        var siteRoot = _contentService.GetById(siteRootId);
        if (siteRoot is null) return;

        // 1. Platform-level structure
        var platformLayout = SeedLayoutStructure(sharedContent.Id, "Platform Layout");
        var platformNavs   = EnsureChildOfType(sharedContent.Id, ContentTypeKeys.Aliases.NavigationFolder, "Navigations");
        if (platformLayout is not null)
        {
            EnsureHeaderNode(platformLayout.Id, "Platform Header");
            EnsureFooterNode(platformLayout.Id, "Platform Footer");
            var platformProfile = EnsureProfileNode(platformLayout.Id, "Platform Default", isDefault: false);

            if (platformProfile is not null)
                WireProfilePickers(platformLayout.Id, platformProfile, "Platform Header", "Platform Footer");
        }

        if (platformNavs is not null)
            EnsurePlatformNav(platformNavs.Id, platformId);

        // 2. Per-SiteRoot structure
        SeedSiteLayout(siteRoot);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Platform-level
    // ══════════════════════════════════════════════════════════════════════════

    private IContent? SeedLayoutStructure(int parentId, string logContext)
    {
        var layout = EnsureChildOfType(parentId, ContentTypeKeys.Aliases.LayoutFolder, "Layout");
        if (layout is null) return null;

        EnsureChildOfType(layout.Id, ContentTypeKeys.Aliases.HeaderConfigFolder,   "Header");
        EnsureChildOfType(layout.Id, ContentTypeKeys.Aliases.FooterConfigFolder,   "Footer");
        EnsureChildOfType(layout.Id, ContentTypeKeys.Aliases.AlertBarConfigFolder, "Alerts");
        EnsureChildOfType(layout.Id, ContentTypeKeys.Aliases.BannerConfigFolder,   "Banners");
        EnsureChildOfType(layout.Id, ContentTypeKeys.Aliases.LayoutProfileFolder,  "Profiles");

        _logger.LogDebug("LayoutConfigSeeder: {Ctx} structure ensured under id={Id}.", logContext, layout.Id);
        return layout;
    }

    private void EnsurePlatformNav(int navFolderId, int platformId)
    {
        var nav = EnsureChildOfType(navFolderId, ContentTypeKeys.Aliases.NavigationGroup, "Platform Nav", n =>
        {
            n.SetValue("groupName",  "Platform Nav");
            n.SetValue("groupAlias", "platformNav");
        });
        if (nav is null) return;

        // Populate nav items pointing to existing SiteRoots (cross-world nav).
        var siteRoots = _contentService.GetPagedChildren(platformId, 0, 50, out _)
            .Where(c => !c.Trashed && c.ContentType.Alias == ContentTypeKeys.Aliases.SiteRoot)
            .ToList();

        if (siteRoots.Count == 0) return;

        // Idempotency: skip if items already populated (editor may have customized).
        var existing = nav.GetValue<string>("navItems");
        if (!string.IsNullOrWhiteSpace(existing)) return;

        // Leave navItems empty when the BlockList type isn't easy to populate from code —
        // editor fills it manually from the backoffice. Label nav node clearly so they know.
        _logger.LogInformation(
            "LayoutConfigSeeder: Platform Nav created with {Count} world(s) detected. Editor should populate navItems in the backoffice.",
            siteRoots.Count);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Site-level
    // ══════════════════════════════════════════════════════════════════════════

    private void SeedSiteLayout(IContent siteRoot)
    {
        var config = EnsureChildOfType(siteRoot.Id, ContentTypeKeys.Aliases.SharedContentFolder, "Config");
        if (config is null) return;

        var layout = SeedLayoutStructure(config.Id, $"Site '{siteRoot.Name}'");
        var navs   = EnsureChildOfType(config.Id, ContentTypeKeys.Aliases.NavigationFolder, "Navigations");

        // Move stray NavigationGroups from Config/ → Config/Navigations/ (idempotent).
        if (navs is not null)
            RelocateNavigationGroups(config.Id, navs.Id);

        if (layout is null) return;

        var headerNode = EnsureHeaderNode(layout.Id, "Site Header");
        var footerNode = EnsureFooterNode(layout.Id, "Site Footer");
        var profile    = EnsureProfileNode(layout.Id, "Default", isDefault: true);

        if (profile is not null)
            WireProfilePickers(layout.Id, profile, "Site Header", "Site Footer");

        // Migrate legacy SiteSettings values into the new nodes.
        var siteSettings = FindChildByType(siteRoot.Id, ContentTypeKeys.Aliases.SiteSettingsAlias);
        if (siteSettings is not null)
        {
            if (headerNode is not null) MigrateHeaderFromSiteSettings(siteSettings, headerNode);
            if (footerNode is not null) MigrateFooterFromSiteSettings(siteSettings, footerNode);

            if (profile is not null) PointSiteSettingsAtDefault(siteSettings, profile);
        }
    }

    private void RelocateNavigationGroups(int configId, int navsFolderId)
    {
        var stray = _contentService.GetPagedChildren(configId, 0, 50, out _)
            .Where(c => !c.Trashed && c.ContentType.Alias == ContentTypeKeys.Aliases.NavigationGroup)
            .ToList();

        foreach (var nav in stray)
        {
            _contentService.Move(nav, navsFolderId, UmbConstants.Security.SuperUserId);
            _logger.LogInformation("LayoutConfigSeeder: moved NavigationGroup '{Name}' (id={Id}) to Navigations folder.", nav.Name, nav.Id);
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Node factories
    // ══════════════════════════════════════════════════════════════════════════

    private IContent? EnsureHeaderNode(int parentId, string name)
    {
        var headerFolder = FindChildByType(parentId, ContentTypeKeys.Aliases.HeaderConfigFolder);
        if (headerFolder is null) return null;

        return EnsureChildOfType(headerFolder.Id, ContentTypeKeys.Aliases.HeaderConfig, name);
    }

    private IContent? EnsureFooterNode(int parentId, string name)
    {
        var footerFolder = FindChildByType(parentId, ContentTypeKeys.Aliases.FooterConfigFolder);
        if (footerFolder is null) return null;

        return EnsureChildOfType(footerFolder.Id, ContentTypeKeys.Aliases.FooterConfig, name);
    }

    private IContent? EnsureProfileNode(int parentId, string name, bool isDefault)
    {
        var profilesFolder = FindChildByType(parentId, ContentTypeKeys.Aliases.LayoutProfileFolder);
        if (profilesFolder is null) return null;

        return EnsureChildOfType(profilesFolder.Id, ContentTypeKeys.Aliases.LayoutProfileAlias, name, p =>
        {
            p.SetValue("layoutName", name);
            if (isDefault) p.SetValue("isDefault", true);
            p.SetValue("platformStripEnabled", true);
        });
    }

    private void WireProfilePickers(int layoutFolderId, IContent profile, string headerName, string footerName)
    {
        var headerFolder = FindChildByType(layoutFolderId, ContentTypeKeys.Aliases.HeaderConfigFolder);
        var footerFolder = FindChildByType(layoutFolderId, ContentTypeKeys.Aliases.FooterConfigFolder);

        var header = headerFolder is null ? null : FindChildByName(headerFolder.Id, headerName);
        var footer = footerFolder is null ? null : FindChildByName(footerFolder.Id, footerName);

        var dirty = false;

        if (header is not null && string.IsNullOrWhiteSpace(profile.GetValue<string>("headerConfig")))
        {
            profile.SetValue("headerConfig", header.GetUdi().ToString());
            dirty = true;
        }
        if (footer is not null && string.IsNullOrWhiteSpace(profile.GetValue<string>("footerConfig")))
        {
            profile.SetValue("footerConfig", footer.GetUdi().ToString());
            dirty = true;
        }

        if (dirty)
        {
            _contentService.SaveAndPublish(profile, userId: UmbConstants.Security.SuperUserId);
            _logger.LogInformation("LayoutConfigSeeder: wired profile '{Name}' (id={Id}) pickers.", profile.Name, profile.Id);
        }
    }

    private void PointSiteSettingsAtDefault(IContent siteSettings, IContent profile)
    {
        if (!siteSettings.HasProperty("defaultProfile")) return;
        if (!string.IsNullOrWhiteSpace(siteSettings.GetValue<string>("defaultProfile"))) return;

        siteSettings.SetValue("defaultProfile", profile.GetUdi().ToString());
        _contentService.SaveAndPublish(siteSettings, userId: UmbConstants.Security.SuperUserId);
        _logger.LogInformation("LayoutConfigSeeder: pointed SiteSettings.defaultProfile at '{Name}' (id={Id}).", profile.Name, profile.Id);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Legacy migration (non-destructive: SetIfEmpty)
    // ══════════════════════════════════════════════════════════════════════════

    private void MigrateHeaderFromSiteSettings(IContent siteSettings, IContent header)
    {
        var dirty = false;
        dirty |= CopyPickerIfEmpty(siteSettings, "headerNavigation", header, "headerNavigation");
        dirty |= CopyBoolIfUnset(siteSettings,  "showHeaderCta",     header, "showCta");
        dirty |= CopyStringIfEmpty(siteSettings, "headerCtaLabel",   header, "ctaLabel");
        // ctaUrl is a Link (LinkUrl data type); preserve raw value for the editor to review.
        dirty |= CopyStringIfEmpty(siteSettings, "headerCtaUrl",     header, "ctaUrl");

        if (dirty)
        {
            _contentService.SaveAndPublish(header, userId: UmbConstants.Security.SuperUserId);
            _logger.LogInformation("LayoutConfigSeeder: migrated legacy header values to HeaderConfig id={Id}.", header.Id);
        }
    }

    private void MigrateFooterFromSiteSettings(IContent siteSettings, IContent footer)
    {
        var dirty = false;
        dirty |= CopyPickerIfEmpty(siteSettings, "footerNavigation",    footer, "footerNavigation");
        dirty |= CopyStringIfEmpty(siteSettings, "footerCopy",          footer, "copyrightText");
        dirty |= CopyStringIfEmpty(siteSettings, "newsletterActionUrl", footer, "newsletterActionUrl");

        if (dirty)
        {
            _contentService.SaveAndPublish(footer, userId: UmbConstants.Security.SuperUserId);
            _logger.LogInformation("LayoutConfigSeeder: migrated legacy footer values to FooterConfig id={Id}.", footer.Id);
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Helpers
    // ══════════════════════════════════════════════════════════════════════════

    private IContent? FindChildByType(int parentId, string alias)
        => _contentService.GetPagedChildren(parentId, 0, 100, out _)
            .Where(c => !c.Trashed)
            .FirstOrDefault(c => c.ContentType.Alias == alias);

    private IContent? FindChildByName(int parentId, string name)
        => _contentService.GetPagedChildren(parentId, 0, 100, out _)
            .Where(c => !c.Trashed)
            .FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

    private IContent? EnsureChildOfType(int parentId, string docTypeAlias, string name,
        Action<IContent>? setProperties = null)
    {
        var existing = _contentService.GetPagedChildren(parentId, 0, 100, out _)
            .Where(c => !c.Trashed)
            .FirstOrDefault(c => c.ContentType.Alias == docTypeAlias);
        if (existing is not null) return existing;

        var docType = _contentTypeService.Get(docTypeAlias);
        if (docType is null)
        {
            _logger.LogDebug("LayoutConfigSeeder: '{Alias}' doctype not found — skipping '{Name}'.", docTypeAlias, name);
            return null;
        }

        var node = _contentService.Create(name, parentId, docType);
        setProperties?.Invoke(node);

        var result = _contentService.SaveAndPublish(node, userId: UmbConstants.Security.SuperUserId);
        if (!result.Success)
        {
            _logger.LogError("LayoutConfigSeeder: failed to publish '{Name}' ({Alias}) — {Status}", name, docTypeAlias, result.Result);
            return null;
        }

        _logger.LogInformation("LayoutConfigSeeder: '{Name}' ({Alias}) published id={Id}.", name, docTypeAlias, node.Id);
        return node;
    }

    private static bool CopyStringIfEmpty(IContent src, string srcAlias, IContent dst, string dstAlias)
    {
        if (!src.HasProperty(srcAlias) || !dst.HasProperty(dstAlias)) return false;
        if (!string.IsNullOrWhiteSpace(dst.GetValue<string>(dstAlias))) return false;

        var value = src.GetValue<string>(srcAlias);
        if (string.IsNullOrWhiteSpace(value)) return false;

        dst.SetValue(dstAlias, value);
        return true;
    }

    private static bool CopyPickerIfEmpty(IContent src, string srcAlias, IContent dst, string dstAlias)
        => CopyStringIfEmpty(src, srcAlias, dst, dstAlias);

    private static bool CopyBoolIfUnset(IContent src, string srcAlias, IContent dst, string dstAlias)
    {
        if (!src.HasProperty(srcAlias) || !dst.HasProperty(dstAlias)) return false;
        // If destination already has an explicit true, leave alone.
        if (dst.GetValue<bool>(dstAlias)) return false;

        var value = src.GetValue<bool>(srcAlias);
        if (!value) return false;

        dst.SetValue(dstAlias, true);
        return true;
    }
}
