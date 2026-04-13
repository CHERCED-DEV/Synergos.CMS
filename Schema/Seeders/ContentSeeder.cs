using System.Text.Json;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Synergos.CMS.Configuration;
using Synergos.CMS.Schema.Constants;
using UmbConstants = Umbraco.Cms.Core.Constants;

namespace Synergos.CMS.Schema.Seeders;

/// <summary>
/// Creates the minimal content tree for a fresh install.
/// Idempotent: checks existence before creating each node.
/// Runs on EVERY startup (not just schema changes).
///
/// Target tree:
///   PlatformRoot (platformRoot)
///   ├── GlobalSettings (globalSettings)
///   ├── SharedContent  (sharedContentFolder)
///   └── SiteRoot "Synergos" (siteRoot)
///       ├── SiteSettings   (siteSettings)
///       ├── ThemeSettings  (themeSettings)
///       ├── Home           (pageBase)
///       ├── Nosotros       (pageBase)
///       ├── Servicios      (pageBase)
///       └── Contacto       (pageBase)
/// </summary>
internal sealed class ContentSeeder
{
    private readonly IContentService             _contentService;
    private readonly IContentTypeService         _contentTypeService;
    private readonly IFileService                _fileService;
    private readonly ILogger<ContentSeeder>      _logger;
    private readonly SeedConfig                  _config;

    // Culture used for culture-variant properties during seeding.
    // Aliased from SeedConfig.SiteCulture so call sites stay short.
    private string SeedCulture => _config.SiteCulture;

    public ContentSeeder(
        IContentService        contentService,
        IContentTypeService    contentTypeService,
        IFileService           fileService,
        ILogger<ContentSeeder> logger,
        SeedConfig?            config = null)
    {
        _contentService     = contentService;
        _contentTypeService = contentTypeService;
        _fileService        = fileService;
        _logger             = logger;
        _config             = config ?? new SeedConfig();
    }

    public void Seed()
    {
        _logger.LogInformation("ContentSeeder: verifying content tree...");
        CleanOrphanedContent();

        // Phase 1 — PlatformRoot (new structure)
        var platform = EnsurePlatformRoot();

        // Phase 1b — Legacy: if SiteRoot exists at root level (pre-platform), keep working
        if (platform is null)
        {
            var legacyRoot = EnsureLegacySiteRoot();
            if (legacyRoot is not null) EnsureBusinessPages(legacyRoot.Id);
            PatchExistingTemplates();
            _logger.LogInformation("ContentSeeder: seed complete (legacy mode).");
            return;
        }

        // Phase 2 — GlobalSettings + SharedContent under PlatformRoot
        EnsureChildByType(platform.Id, ContentTypeKeys.Aliases.GlobalSettings, "Global Settings");
        var sharedContent = EnsureChildByType(platform.Id, ContentTypeKeys.Aliases.SharedContentFolder, "Shared Content");
        if (sharedContent is not null)
            EnsureChildByType(sharedContent.Id, ContentTypeKeys.Aliases.PageTagsFolder, "Tags");

        // Phase 3 — SiteRoot under PlatformRoot
        var siteRoot = EnsureSiteRoot(platform.Id);
        if (siteRoot is null)
        {
            _logger.LogInformation("ContentSeeder: seed complete (no SiteRoot).");
            return;
        }

        // Phase 4 — Settings nodes under SiteRoot
        EnsureChildByType(siteRoot.Id, ContentTypeKeys.Aliases.SiteSettingsAlias, "Site Settings");
        EnsureChildByType(siteRoot.Id, ContentTypeKeys.Aliases.ThemeSettings, "Theme Settings");

        // Phase 5 — Business pages under SiteRoot
        EnsureBusinessPages(siteRoot.Id);

        // Phase 6 — Template patching
        PatchExistingTemplates();

        // Phase 7 — Patch settings nodes with default values
        PatchSettings(platform.Id, siteRoot.Id);

        _logger.LogInformation("ContentSeeder: seed complete.");
    }

    // ── Phase 1: PlatformRoot ────────────────────────────────────────────

    private IContent? EnsurePlatformRoot()
    {
        var platformType = _contentTypeService.Get(ContentTypeKeys.Aliases.PlatformRoot);
        if (platformType is null)
        {
            _logger.LogDebug("ContentSeeder: 'platformRoot' Document Type not found — falling back to legacy.");
            return null;
        }

        // GetPagedOfType is more resilient than GetRootContent() during content-store warm-up.
        // GetRootContent() can return an empty set in the first milliseconds of startup,
        // causing a duplicate PlatformRoot to be created on every cold start.
        // IMPORTANT: filter out trashed nodes — GetPagedOfType returns Recycle Bin items too,
        // which would cause all child publishes to fail with FailedPublishPathNotPublished.
        var existing = _contentService.GetPagedOfType(platformType.Id, 0, 5, out _, null!)
            .FirstOrDefault(c => !c.Trashed);

        if (existing is not null) return existing;

        var root = _contentService.Create(_config.PlatformName, UmbConstants.System.Root, platformType);
        root.SetValue("platformName", _config.PlatformName);

        var result = _contentService.SaveAndPublish(root, userId: UmbConstants.Security.SuperUserId);
        if (!result.Success)
        {
            _logger.LogError("ContentSeeder: failed to publish PlatformRoot — {Status}", result.Result);
            // A concurrent startup may have won the race — return whatever exists now
            return _contentService.GetPagedOfType(platformType.Id, 0, 5, out _, null!).FirstOrDefault();
        }

        _logger.LogInformation("ContentSeeder: PlatformRoot published (id={Id}).", root.Id);
        return root;
    }

    // ── Phase 1b: Legacy SiteRoot (backward compat) ─────────────────────

    private IContent? EnsureLegacySiteRoot()
    {
        var existing = _contentService.GetRootContent()
            .FirstOrDefault(c => c.ContentType.Alias == ContentTypeKeys.Aliases.SiteRoot);

        if (existing is not null)
        {
            PatchSiteRootDefaults(existing);
            return existing;
        }

        var rootType = _contentTypeService.Get(ContentTypeKeys.Aliases.SiteRoot);
        if (rootType is null) return null;

        var root = _contentService.Create(_config.SiteName, UmbConstants.System.Root, rootType);
        root.SetValue("siteName", _config.SiteName);
        root.SetValue("siteTagline", _config.SiteTagline);

        var template = _fileService.GetTemplate("SiteRoot");
        if (template is not null) root.TemplateId = template.Id;

        var result = _contentService.SaveAndPublish(root, userId: UmbConstants.Security.SuperUserId);
        if (!result.Success) return null;

        return root;
    }

    // ── Orphan cleanup ────────────────────────────────────────────────────
    // Deletes content nodes whose parent no longer exists in umbracoNode.
    // This happens when a parent is permanently deleted but Umbraco leaves
    // children behind (Umbraco bug with cascade delete under certain flows).

    private void CleanOrphanedContent()
    {
        var aliasesToCheck = new[]
        {
            ContentTypeKeys.Aliases.PageBase,
            ContentTypeKeys.Aliases.SiteRoot,
            ContentTypeKeys.Aliases.SiteSettingsAlias,
            ContentTypeKeys.Aliases.ThemeSettings,
            ContentTypeKeys.Aliases.GlobalSettings,
        };

        var rootIds = _contentService.GetRootContent()
            .Select(c => c.Id)
            .ToHashSet();
        rootIds.Add(UmbConstants.System.Root);

        foreach (var alias in aliasesToCheck)
        {
            var ct = _contentTypeService.Get(alias);
            if (ct is null) continue;

            var nodes = _contentService.GetPagedOfType(ct.Id, 0, 500, out _, null!);
            foreach (var node in nodes)
            {
                if (node.Trashed) continue;
                if (node.ParentId == UmbConstants.System.Root) continue;

                var parent = _contentService.GetById(node.ParentId);
                if (parent is not null) continue;

                // Parent is gone — permanently delete this orphaned node
                _logger.LogWarning(
                    "ContentSeeder: deleting orphaned '{Name}' (id={Id}, missingParent={ParentId}).",
                    node.Name, node.Id, node.ParentId);
                _contentService.Delete(node, UmbConstants.Security.SuperUserId);
            }
        }
    }

    // ── Phase 3: SiteRoot under PlatformRoot ─────────────────────────────

    private IContent? EnsureSiteRoot(int platformId)
    {
        // Check if a SiteRoot already exists under PlatformRoot
        var children = _contentService.GetPagedChildren(platformId, 0, 100, out _)
            .Where(c => !c.Trashed);
        var existing = children.FirstOrDefault(c => c.ContentType.Alias == ContentTypeKeys.Aliases.SiteRoot);
        if (existing is not null)
        {
            PatchSiteRootDefaults(existing);
            return existing;
        }

        var rootType = _contentTypeService.Get(ContentTypeKeys.Aliases.SiteRoot);
        if (rootType is null)
        {
            _logger.LogWarning("ContentSeeder: 'siteRoot' Document Type not found — skipping.");
            return null;
        }

        var root = _contentService.Create(_config.SiteName, platformId, rootType);
        root.SetValue("siteName", _config.SiteName);
        root.SetValue("siteTagline", _config.SiteTagline);

        var template = _fileService.GetTemplate("SiteRoot");
        if (template is not null) root.TemplateId = template.Id;

        var result = _contentService.SaveAndPublish(root, userId: UmbConstants.Security.SuperUserId);
        if (!result.Success)
        {
            _logger.LogError("ContentSeeder: failed to publish SiteRoot — {Status}", result.Result);
            return null;
        }

        _logger.LogInformation("ContentSeeder: SiteRoot published under PlatformRoot (id={Id}).", root.Id);
        return root;
    }

    private void PatchSiteRootDefaults(IContent root)
    {
        var dirty = false;
        if (string.IsNullOrWhiteSpace(root.GetValue<string>("siteTagline")))
        {
            root.SetValue("siteTagline", _config.SiteTagline);
            dirty = true;
        }
        if (NormalizeSingleSelectDropdown(root, "defaultCulture"))
        {
            dirty = true;
        }
        if (dirty)
        {
            _contentService.SaveAndPublish(root, userId: UmbConstants.Security.SuperUserId);
            _logger.LogInformation("ContentSeeder: patched defaults on SiteRoot (id={Id}).", root.Id);
        }
    }

    // ── Generic child-by-type creator ─────────────────────────────────────

    private IContent? EnsureChildByType(int parentId, string docTypeAlias, string name,
        Action<IContent>? setProperties = null)
    {
        var children = _contentService.GetPagedChildren(parentId, 0, 100, out _)
            .Where(c => !c.Trashed);
        var existing = children.FirstOrDefault(c => c.ContentType.Alias == docTypeAlias);
        if (existing is not null) return existing;

        var docType = _contentTypeService.Get(docTypeAlias);
        if (docType is null)
        {
            _logger.LogDebug("ContentSeeder: '{Alias}' Document Type not found — skipping.", docTypeAlias);
            return null;
        }

        var node = _contentService.Create(name, parentId, docType);
        setProperties?.Invoke(node);

        var result = _contentService.SaveAndPublish(node, userId: UmbConstants.Security.SuperUserId);
        if (result.Success)
            _logger.LogInformation("ContentSeeder: '{Name}' ({Alias}) published (id={Id}).", name, docTypeAlias, node.Id);
        else
            _logger.LogError("ContentSeeder: failed to publish '{Name}' ({Alias}) — {Status}", name, docTypeAlias, result.Result);

        return result.Success ? node : null;
    }

    // ── Phase 5: Business Pages ──────────────────────────────────────────

    private void EnsureBusinessPages(int rootId)
    {
        if (_config.Pages.Count == 0) return;

        var existingNames = _contentService.GetPagedChildren(rootId, 0, 100, out _)
            .Where(c => !c.Trashed)
            .Select(c => c.Name ?? string.Empty)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < _config.Pages.Count; i++)
        {
            var spec = _config.Pages[i];
            if (string.IsNullOrWhiteSpace(spec.Name)) continue;

            EnsurePage(rootId, existingNames, spec.Name, ContentTypeKeys.Aliases.PageBase, "PageBase", i,
                page =>
                {
                    if (!string.IsNullOrWhiteSpace(spec.Subtitle))
                        page.SetValue("pageSubtitle", spec.Subtitle, culture: SeedCulture);
                    if (!string.IsNullOrWhiteSpace(spec.SeoTitle))
                        page.SetValue("seoTitle", spec.SeoTitle);
                    if (!string.IsNullOrWhiteSpace(spec.SeoDescription))
                        page.SetValue("seoDescription", spec.SeoDescription);
                });
        }
    }

    private void EnsurePage(
        int parentId, HashSet<string> existingNames,
        string name, string docTypeAlias, string templateAlias,
        int sortOrder, Action<IContent>? setProperties = null)
    {
        if (existingNames.Contains(name)) return;

        var pageType = _contentTypeService.Get(docTypeAlias);
        if (pageType is null) return;

        var page = _contentService.Create(name, parentId, pageType);
        page.SetCultureName(name, SeedCulture);
        page.SetValue("pageTitle", name, culture: SeedCulture);
        page.SortOrder = sortOrder;
        setProperties?.Invoke(page);

        var template = _fileService.GetTemplate(templateAlias);
        if (template is not null) page.TemplateId = template.Id;

        var result = _contentService.SaveAndPublish(page, userId: UmbConstants.Security.SuperUserId);
        if (result.Success)
            _logger.LogInformation("ContentSeeder: '{Name}' published (id={Id}).", name, page.Id);
        else
            _logger.LogError("ContentSeeder: failed to publish '{Name}' — {Status}", name, result.Result);
    }

    // ── Phase 6: Template Patching ───────────────────────────────────────
    //
    // Ensures every content node has the correct template assigned.
    // Consolidation: all composable page types (homePage, aboutPage,
    // servicesPage, contactPage, landingPage, pageBase) use "PageBase".

    /// <summary>
    /// Retired business-page aliases whose templates should be consolidated to PageBase.
    /// </summary>
    private static readonly HashSet<string> ConsolidatedPageAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        "homePage", "aboutPage", "servicesPage", "contactPage", "landingPage", ContentTypeKeys.Aliases.PageBase
    };

    private void PatchExistingTemplates()
    {
        var platformTemplate = _fileService.GetTemplate("PlatformRoot");
        var siteRootTemplate = _fileService.GetTemplate("SiteRoot");
        var pageBaseTemplate = _fileService.GetTemplate("PageBase");

        foreach (var root in _contentService.GetRootContent())
        {
            // PlatformRoot → PlatformRoot template
            if (root.ContentType.Alias == ContentTypeKeys.Aliases.PlatformRoot)
            {
                PatchTemplate(root, platformTemplate);
                PatchChildrenRecursive(root.Id, siteRootTemplate, pageBaseTemplate, platformTemplate, depth: 0);
                continue;
            }

            // Legacy: SiteRoot at root level
            if (root.ContentType.Alias == ContentTypeKeys.Aliases.SiteRoot)
            {
                PatchTemplate(root, siteRootTemplate);
                PatchChildrenRecursive(root.Id, siteRootTemplate, pageBaseTemplate, platformTemplate, depth: 1);
            }
        }
    }

    private void PatchChildrenRecursive(int parentId, ITemplate? siteRootTemplate,
        ITemplate? pageBaseTemplate, ITemplate? platformTemplate, int depth)
    {
        if (depth > 5) return; // safety guard

        foreach (var child in _contentService.GetPagedChildren(parentId, 0, 100, out _))
        {
            var alias = child.ContentType.Alias;
            var contentType = _contentTypeService.Get(alias);

            if (alias == ContentTypeKeys.Aliases.SiteRoot)
            {
                PatchTemplate(child, siteRootTemplate);
                PatchChildrenRecursive(child.Id, siteRootTemplate, pageBaseTemplate, platformTemplate, depth + 1);
            }
            else if (ConsolidatedPageAliases.Contains(alias))
            {
                PatchTemplate(child, pageBaseTemplate);
                // Pages can have child pages (pageBase allows nesting)
                PatchChildrenRecursive(child.Id, siteRootTemplate, pageBaseTemplate, platformTemplate, depth + 1);
            }
            else if (contentType?.AllowedTemplates?.Any() == true)
            {
                // Other routable types: prefer an alias-matching template and fall
                // back to the first template declared on the content type.
                var template = _fileService.GetTemplate(ToPascalCase(alias))
                    ?? contentType.AllowedTemplates.FirstOrDefault();
                PatchTemplate(child, template);
            }
            else
            {
                ClearTemplate(child);
            }
        }
    }

    private void PatchTemplate(IContent node, ITemplate? template)
    {
        if (template is null) return;
        if (node.TemplateId == template.Id) return;

        node.TemplateId = template.Id;
        _contentService.SaveAndPublish(node, userId: UmbConstants.Security.SuperUserId);
        _logger.LogInformation("ContentSeeder: patched template on '{Name}' (id={Id}) → {Template}.",
            node.Name, node.Id, template.Alias);
    }

    private void ClearTemplate(IContent node)
    {
        if (node.TemplateId is null or 0) return;

        node.TemplateId = null;
        _contentService.SaveAndPublish(node, userId: UmbConstants.Security.SuperUserId);
        _logger.LogInformation("ContentSeeder: cleared template on '{Name}' (id={Id}) because its content type is not routable.",
            node.Name, node.Id);
    }

    private bool NormalizeSingleSelectDropdown(IContent content, string alias)
    {
        var rawValue = content.GetValue<string>(alias);
        if (string.IsNullOrWhiteSpace(rawValue))
            return false;

        var trimmedValue = rawValue.Trim();
        if (trimmedValue.StartsWith("[", StringComparison.Ordinal))
            return false;

        content.SetValue(alias, JsonSerializer.Serialize(new[] { trimmedValue }));
        _logger.LogInformation(
            "ContentSeeder: normalized legacy dropdown value on '{Name}' (id={Id}) for '{Alias}'.",
            content.Name,
            content.Id,
            alias);

        return true;
    }

    private static string ToPascalCase(string alias)
        => string.IsNullOrEmpty(alias) ? alias : char.ToUpperInvariant(alias[0]) + alias[1..];

    // ── Phase 7: Settings Patching ───────────────────────────────────────────
    //
    // Fills default values for GlobalSettings, SiteSettings, ThemeSettings and
    // page SEO fields. Only sets values that are currently empty — never
    // overwrites values already set in the backoffice.

    private void PatchSettings(int platformId, int siteRootId)
    {
        var globalSettings = FindChildByType(platformId, ContentTypeKeys.Aliases.GlobalSettings);
        if (globalSettings is not null) PatchGlobalSettings(globalSettings);

        var siteSettings = FindChildByType(siteRootId, ContentTypeKeys.Aliases.SiteSettingsAlias);
        if (siteSettings is not null) PatchSiteSettings(siteSettings);

        var themeSettings = FindChildByType(siteRootId, ContentTypeKeys.Aliases.ThemeSettings);
        if (themeSettings is not null) PatchThemeSettings(themeSettings);

        PatchPagesSeo(siteRootId);
    }

    private IContent? FindChildByType(int parentId, string alias)
        => _contentService.GetPagedChildren(parentId, 0, 100, out _)
            .Where(c => !c.Trashed)
            .FirstOrDefault(c => c.ContentType.Alias == alias);

    private void PatchGlobalSettings(IContent node)
    {
        var dirty = SetIfEmpty(node, "defaultSeoDescription", _config.GlobalSeoDescription);

        if (dirty)
        {
            _contentService.SaveAndPublish(node, userId: UmbConstants.Security.SuperUserId);
            _logger.LogInformation("ContentSeeder: patched GlobalSettings (id={Id}).", node.Id);
        }
    }

    private void PatchSiteSettings(IContent node)
    {
        var dirty = false;
        dirty |= SetIfEmpty(node, "siteDisplayName",       _config.SiteDisplayName);
        dirty |= SetIfEmpty(node, "siteTaglineExt",        _config.SiteTaglineExt);
        dirty |= SetIfEmpty(node, "footerCopy",            $"© {DateTime.Now.Year} {_config.SiteDisplayName}. Todos los derechos reservados.");
        dirty |= SetIfEmpty(node, "contactEmail",          _config.ContactEmail);
        dirty |= SetIfEmpty(node, "contactPhone",          _config.ContactPhone);
        dirty |= SetIfEmpty(node, "contactAddress",        _config.ContactAddress);
        dirty |= SetIfEmpty(node, "facebookUrl",           _config.FacebookUrl);
        dirty |= SetIfEmpty(node, "linkedinUrl",           _config.LinkedInUrl);
        dirty |= SetIfEmpty(node, "twitterUrl",            _config.TwitterUrl);
        dirty |= SetIfEmpty(node, "youtubeUrl",            _config.YouTubeUrl);
        dirty |= SetIfEmpty(node, "seoTitleSuffix",        _config.SeoTitleSuffix);
        dirty |= SetIfEmpty(node, "seoDefaultDescription", _config.SeoDefaultDescription);
        dirty |= SetIfEmpty(node, "headerCtaLabel",        _config.HeaderCtaLabel);
        dirty |= SetIfEmpty(node, "formRecipientEmail",    _config.FormRecipientEmail);

        if (node.HasProperty("showHeaderCta") && !node.GetValue<bool>("showHeaderCta"))
        { node.SetValue("showHeaderCta", true); dirty = true; }

        if (dirty)
        {
            _contentService.SaveAndPublish(node, userId: UmbConstants.Security.SuperUserId);
            _logger.LogInformation("ContentSeeder: patched SiteSettings (id={Id}).", node.Id);
        }
    }

    private void PatchThemeSettings(IContent node)
    {
        var t     = _config.Theme;
        var dirty = false;

        // Colors (TextHexColor = plain TextBox)
        dirty |= SetIfEmpty(node, "colorPrimary",     t.ColorPrimary);
        dirty |= SetIfEmpty(node, "colorSecondary",   t.ColorSecondary);
        dirty |= SetIfEmpty(node, "colorAccent",      t.ColorAccent);
        dirty |= SetIfEmpty(node, "colorBackground",  t.ColorBackground);
        dirty |= SetIfEmpty(node, "colorSurface",     t.ColorSurface);
        dirty |= SetIfEmpty(node, "colorText",        t.ColorText);
        dirty |= SetIfEmpty(node, "colorTextInverse", t.ColorTextInverse);
        dirty |= SetIfEmpty(node, "colorBorder",      t.ColorBorder);
        dirty |= SetIfEmpty(node, "colorSuccess",     t.ColorSuccess);
        dirty |= SetIfEmpty(node, "colorWarning",     t.ColorWarning);
        dirty |= SetIfEmpty(node, "colorError",       t.ColorError);

        // Typography
        dirty |= SetIfEmpty(node, "fontFamilyHeading", t.FontFamilyHeading);
        dirty |= SetIfEmpty(node, "fontFamilyBody",    t.FontFamilyBody);
        dirty |= SetIfEmpty(node, "fontBaseSize",      t.FontBaseSize);

        // Spacing & Layout
        dirty |= SetIfEmpty(node, "containerMaxWidth", t.ContainerMaxWidth);
        dirty |= SetIfEmpty(node, "borderRadius",      t.BorderRadius);
        dirty |= SetIfEmpty(node, "sectionPaddingY",   t.SectionPaddingY);

        // Dropdowns (DropDown.Flexible → stored as JSON array)
        dirty |= SetDropdownIfEmpty(node, "buttonStyle", t.ButtonStyle);
        dirty |= SetDropdownIfEmpty(node, "cardStyle",   t.CardStyle);
        dirty |= SetDropdownIfEmpty(node, "headerStyle", t.HeaderStyle);

        if (dirty)
        {
            _contentService.SaveAndPublish(node, userId: UmbConstants.Security.SuperUserId);
            _logger.LogInformation("ContentSeeder: patched ThemeSettings (id={Id}).", node.Id);
        }
    }

    private void PatchPagesSeo(int siteRootId)
    {
        if (_config.Pages.Count == 0) return;

        var seoByName = _config.Pages
            .Where(p => !string.IsNullOrWhiteSpace(p.Name))
            .ToDictionary(p => p.Name, p => p, StringComparer.OrdinalIgnoreCase);

        var pages = _contentService.GetPagedChildren(siteRootId, 0, 100, out _)
            .Where(c => !c.Trashed && c.ContentType.Alias == ContentTypeKeys.Aliases.PageBase)
            .ToList();

        foreach (var page in pages)
        {
            if (!seoByName.TryGetValue(page.Name ?? string.Empty, out var spec)) continue;

            var dirty = false;
            // OG fallbacks to SEO fields when not explicitly set in config.
            var ogTitle = string.IsNullOrWhiteSpace(spec.OgTitle) ? spec.SeoTitle : spec.OgTitle;
            var ogDesc  = string.IsNullOrWhiteSpace(spec.OgDescription) ? spec.SeoDescription : spec.OgDescription;

            if (!string.IsNullOrWhiteSpace(ogTitle))
                dirty |= SetIfEmpty(page, "ogTitle", ogTitle);
            if (!string.IsNullOrWhiteSpace(ogDesc))
                dirty |= SetIfEmpty(page, "ogDescription", ogDesc);
            dirty |= SetDropdownIfEmpty(page, "robots", "index");

            if (dirty)
            {
                _contentService.SaveAndPublish(page, userId: UmbConstants.Security.SuperUserId);
                _logger.LogInformation("ContentSeeder: patched SEO on '{Name}' (id={Id}).", page.Name, page.Id);
            }
        }
    }

    private bool SetIfEmpty(IContent node, string alias, string value)
    {
        if (!node.HasProperty(alias)) return false;
        if (!string.IsNullOrWhiteSpace(node.GetValue<string>(alias))) return false;
        node.SetValue(alias, value);
        return true;
    }

    private bool SetDropdownIfEmpty(IContent node, string alias, string value)
    {
        if (!node.HasProperty(alias)) return false;
        if (!string.IsNullOrWhiteSpace(node.GetValue<string>(alias))) return false;
        node.SetValue(alias, JsonSerializer.Serialize(new[] { value }));
        return true;
    }
}
