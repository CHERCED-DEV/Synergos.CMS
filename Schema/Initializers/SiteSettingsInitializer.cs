using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;
using Synergos.CMS.Schema.Constants;

namespace Synergos.CMS.Schema.Initializers;

/// <summary>
/// Phase 9b (Settings) — creates/patches the four per-site settings document types:
///   GlobalSettings, ThemeSettings, SiteSettings, LayoutProfile.
///
/// Extracted from PlatformInitializer to keep each file under ~600 lines.
/// Called directly from PlatformInitializer.Initialize() — not a separate pipeline phase.
/// </summary>
internal sealed class SiteSettingsInitializer : SchemaInitializerBase
{
    // Tab aliases — reused across SiteSettings / GlobalSettings property groups.
    private const string TabIdentity = "identity";
    private const string TabHeader   = "header";
    private const string TabFooter   = "footer";
    private const string TabAlertBar = "alertBar";

    // Repeated property descriptions and labels.
    private const string PickNavigationGroup = "Pick a NavigationGroup";
    private const string LabelCtaLabel       = "CTA Label";
    private const string LabelCtaUrl         = "CTA URL";

    private readonly int _settingsFolderId;

    public SiteSettingsInitializer(
        IContentTypeService cts,
        IDataTypeService    dts,
        IShortStringHelper  ssh,
        int                 settingsFolderId)
        : base(cts, dts, ssh)
    {
        _settingsFolderId = settingsFolderId;
    }

    public override void Initialize()
    {
        EnsureGlobalSettings(_settingsFolderId);
        EnsureThemeSettings(_settingsFolderId);
        EnsureSiteSettings(_settingsFolderId);
        EnsureLayoutProfile(_settingsFolderId);
    }

    // ── GlobalSettings ────────────────────────────────────────────────────────

    private void EnsureGlobalSettings(int folderId)
    {
        // GlobalSettings holds only the two platform-wide defaults actually consumed:
        //   SEO fallbacks (UmbracoSeoService) and Tracking scripts (UmbracoTrackingService).
        // Everything else that previously lived here (platform tab, feature toggles,
        // editorial defaults, email config, CDN config) was dead weight — never read
        // by any service. CDN belongs to StaticAssetsSettings (appsettings.json);
        // email to an external SMTP config; features require middleware/gates to mean
        // anything and should be added here only when a consumer exists.
        const string description = "Fallbacks SEO y scripts de tracking compartidos por toda la plataforma.";
        if (TryPatchExistingContentType(ContentTypeKeys.GlobalSettings, "Global Settings", folderId, description, "icon-wrench")) return;

        var ct = new ContentType(Ssh, folderId)
        {
            Key   = ContentTypeKeys.GlobalSettings,
            Name  = "Global Settings",
            Description = description,
            Alias = ContentTypeKeys.Aliases.GlobalSettings,
            Icon  = "icon-wrench"
        };

        var tabSeo = Tab("SEO Defaults", "seoDefaults", 0);
        tabSeo.PropertyTypes!.Add(Prop("defaultSeoDescription", "Default Description", DataTypeKeys.TextMetaDesc, 0, description: "Fallback meta description used when a site has no seoDefaultDescription override."));
        tabSeo.PropertyTypes!.Add(Prop("defaultOgImage",        "Default OG Image",    DataTypeKeys.MediaImage,  10, description: "Fallback Open Graph image used when a site has no seoDefaultOgImage override."));
        ct.PropertyGroups.Add(tabSeo);

        var tabScripts = Tab("Scripts & Analytics", "scripts", 1);
        tabScripts.PropertyTypes!.Add(Prop("gtmContainerId", "GTM Container ID", DataTypeKeys.TextIdentifier,    0,  description: "Fallback GTM container id when a site has no siteGtmId override."));
        tabScripts.PropertyTypes!.Add(Prop("headScripts",    "Head Scripts",     DataTypeKeys.TextScriptContent, 10, description: "Platform-wide scripts prepended to every site's siteHeadScripts."));
        tabScripts.PropertyTypes!.Add(Prop("bodyEndScripts", "Body End Scripts", DataTypeKeys.TextScriptContent, 20, description: "Platform-wide scripts prepended to every site's siteBodyEndScripts."));
        ct.PropertyGroups.Add(tabScripts);

        Cts.Save(ct);
    }

    // ── ThemeSettings ─────────────────────────────────────────────────────────

    private void EnsureThemeSettings(int folderId)
    {
        const string description = "Configuración visual de marca, colores, tipografía y variantes.";

        var existing = Cts.Get(ContentTypeKeys.ThemeSettings);
        if (existing is not null)
        {
            var dirty = false;
            if (!string.Equals(existing.Name, "Theme Settings", StringComparison.Ordinal))
            { existing.Name = "Theme Settings"; dirty = true; }
            if (existing.ParentId != folderId)
            { existing.ParentId = folderId; dirty = true; }
            if (!string.Equals(existing.Description, description, StringComparison.Ordinal))
            { existing.Description = description; dirty = true; }
            dirty |= PatchIcon(existing, "icon-palette");

            if (!existing.PropertyTypes.Any(p => p.Alias == "identityPreset"))
            {
                var identityTab = existing.PropertyGroups.FirstOrDefault(g => g.Alias == TabIdentity);
                if (identityTab is null)
                {
                    identityTab = Tab("Identity", TabIdentity, -1);
                    existing.PropertyGroups.Add(identityTab);
                }
                identityTab.PropertyTypes!.Add(Prop("identityPreset", "Identity Preset",
                    DataTypeKeys.SelectIdentityPreset, 0,
                    description: "Built-in brand token set. 'custom' uses the individual Color & Typography fields exclusively."));
                dirty = true;
            }

            if (dirty) Cts.Save(existing);
            return;
        }

        var ct = new ContentType(Ssh, folderId)
        {
            Key   = ContentTypeKeys.ThemeSettings,
            Name  = "Theme Settings",
            Description = description,
            Alias = ContentTypeKeys.Aliases.ThemeSettings,
            Icon  = "icon-palette"
        };

        var tabIdentity = Tab("Identity", TabIdentity, -1);
        tabIdentity.PropertyTypes!.Add(Prop("identityPreset", "Identity Preset",
            DataTypeKeys.SelectIdentityPreset, 0,
            description: "Built-in brand token set. 'custom' uses the individual Color & Typography fields exclusively."));
        ct.PropertyGroups.Add(tabIdentity);

        var tabColors = Tab("Colors", "colors", 0);
        tabColors.PropertyTypes!.Add(Prop("colorPrimary",     "Primary",       DataTypeKeys.TextHexColor,  0));
        tabColors.PropertyTypes!.Add(Prop("colorSecondary",   "Secondary",     DataTypeKeys.TextHexColor, 10));
        tabColors.PropertyTypes!.Add(Prop("colorAccent",      "Accent",        DataTypeKeys.TextHexColor, 20));
        tabColors.PropertyTypes!.Add(Prop("colorBackground",  "Background",    DataTypeKeys.TextHexColor, 30));
        tabColors.PropertyTypes!.Add(Prop("colorSurface",     "Surface",       DataTypeKeys.TextHexColor, 40));
        tabColors.PropertyTypes!.Add(Prop("colorText",        "Text",          DataTypeKeys.TextHexColor, 50));
        tabColors.PropertyTypes!.Add(Prop("colorTextInverse", "Text Inverse",  DataTypeKeys.TextHexColor, 60));
        tabColors.PropertyTypes!.Add(Prop("colorBorder",      "Border",        DataTypeKeys.TextHexColor, 70));
        tabColors.PropertyTypes!.Add(Prop("colorSuccess",     "Success",       DataTypeKeys.TextHexColor, 80));
        tabColors.PropertyTypes!.Add(Prop("colorWarning",     "Warning",       DataTypeKeys.TextHexColor, 90));
        tabColors.PropertyTypes!.Add(Prop("colorError",       "Error",         DataTypeKeys.TextHexColor, 100));
        ct.PropertyGroups.Add(tabColors);

        var tabTypo = Tab("Typography", "typography", 1);
        tabTypo.PropertyTypes!.Add(Prop("fontFamilyHeading", "Heading Font",     DataTypeKeys.TextTitle, 0));
        tabTypo.PropertyTypes!.Add(Prop("fontFamilyBody",    "Body Font",        DataTypeKeys.TextTitle, 10));
        tabTypo.PropertyTypes!.Add(Prop("fontFamilyMono",    "Monospace Font",   DataTypeKeys.TextTitle, 20));
        tabTypo.PropertyTypes!.Add(Prop("fontBaseSize",      "Base Size",        DataTypeKeys.TextTitle, 30, description: "e.g. 16px"));
        tabTypo.PropertyTypes!.Add(Prop("fontScaleRatio",    "Scale Ratio",      DataTypeKeys.TextTitle, 40, description: "e.g. 1.25"));
        tabTypo.PropertyTypes!.Add(Prop("headingFontUrl",    "Heading Font URL", DataTypeKeys.TextUrl,   50));
        tabTypo.PropertyTypes!.Add(Prop("bodyFontUrl",       "Body Font URL",    DataTypeKeys.TextUrl,   60));
        ct.PropertyGroups.Add(tabTypo);

        var tabBrand = Tab("Brand Assets", "brandAssets", 2);
        tabBrand.PropertyTypes!.Add(Prop("logoLight",    "Logo (Light)",   DataTypeKeys.MediaImage, 0));
        tabBrand.PropertyTypes!.Add(Prop("logoDark",     "Logo (Dark)",    DataTypeKeys.MediaImage, 10));
        tabBrand.PropertyTypes!.Add(Prop("logoIcon",     "Favicon / Icon", DataTypeKeys.MediaImage, 20));
        tabBrand.PropertyTypes!.Add(Prop("brandPattern", "Brand Pattern",  DataTypeKeys.MediaImage, 30));
        tabBrand.PropertyTypes!.Add(Prop("faviconUrl",   "Favicon URL",    DataTypeKeys.TextUrl,    40));
        ct.PropertyGroups.Add(tabBrand);

        var tabSpacing = Tab("Spacing & Layout", "spacing", 3);
        tabSpacing.PropertyTypes!.Add(Prop("spacingUnit",       "Spacing Unit",        DataTypeKeys.TextTitle, 0,  description: "e.g. 8px"));
        tabSpacing.PropertyTypes!.Add(Prop("containerMaxWidth", "Container Max Width", DataTypeKeys.TextTitle, 10, description: "e.g. 1280px"));
        tabSpacing.PropertyTypes!.Add(Prop("borderRadius",      "Border Radius",       DataTypeKeys.TextTitle, 20, description: "e.g. 8px"));
        tabSpacing.PropertyTypes!.Add(Prop("sectionPaddingY",   "Section Padding Y",   DataTypeKeys.TextTitle, 30, description: "e.g. 4rem"));
        ct.PropertyGroups.Add(tabSpacing);

        var tabVariants = Tab("Component Variants", "variants", 4);
        tabVariants.PropertyTypes!.Add(Prop("buttonStyle", "Button Style", DataTypeKeys.SelectButtonStyle, 0));
        tabVariants.PropertyTypes!.Add(Prop("cardStyle",   "Card Style",   DataTypeKeys.SelectCardStyle,  10));
        tabVariants.PropertyTypes!.Add(Prop("headerStyle", "Header Style", DataTypeKeys.SelectHeaderStyle, 20));
        ct.PropertyGroups.Add(tabVariants);

        var tabCss = Tab("Custom CSS", "customCss", 5);
        tabCss.PropertyTypes!.Add(Prop("customCssSnippet", "Custom CSS", DataTypeKeys.TextScriptContent, 0, description: "CSS overrides per brand"));
        ct.PropertyGroups.Add(tabCss);

        Cts.Save(ct);
    }

    // ── SiteSettings ──────────────────────────────────────────────────────────

    private void EnsureSiteSettings(int folderId)
    {
        const string description = "Configuración editorial y operativa específica de cada sitio.";

        var existing = Cts.Get(ContentTypeKeys.SiteSettings);
        if (existing is not null)
        {
            var dirty = false;
            if (existing.ParentId != folderId)
            { existing.ParentId = folderId; dirty = true; }
            if (!string.Equals(existing.Description, description, StringComparison.Ordinal))
            { existing.Description = description; dirty = true; }
            dirty |= PatchIcon(existing, "icon-settings");

            // Non-destructive rename: "Header & Footer" (headerFooter) → "Header" (header).
            var hfTab = existing.PropertyGroups.FirstOrDefault(g => g.Alias == "headerFooter");
            if (hfTab is not null)
            {
                hfTab.Name      = "Header";
                hfTab.Alias     = TabHeader;
                hfTab.SortOrder = 5;
                dirty = true;
            }

            // Remove "Blog" tab — fields remain ungrouped; services still read by alias.
            // BlogHome manages these settings in its own domain.
            var blogTab = existing.PropertyGroups.FirstOrDefault(g => g.Alias == "blog");
            if (blogTab is not null)
            {
                existing.PropertyGroups.Remove(blogTab);
                dirty = true;
            }

            // v11.0.0 — mark legacy layout tabs as deprecated. Properties remain so
            // LayoutConfigSeeder can migrate values on startup; cleanup in v11.1.0.
            dirty |= MarkLegacyTabAsDeprecated(existing, TabHeader,   "Header (Deprecated — use Config/Layout/Header)",   90);
            dirty |= MarkLegacyTabAsDeprecated(existing, TabFooter,   "Footer (Deprecated — use Config/Layout/Footer)",   91);
            dirty |= MarkLegacyTabAsDeprecated(existing, TabAlertBar, "Alert Bar (Deprecated — use Config/Layout/Alerts)", 92);
            dirty |= MarkLegacyTabAsDeprecated(existing, "banners",   "Banners (Deprecated — use Config/Layout/Banners)",  93);

            dirty |= EnsureSiteSettingsMissingProps(existing);
            if (dirty) Cts.Save(existing);
            return;
        }

        var ct = new ContentType(Ssh, folderId)
        {
            Key         = ContentTypeKeys.SiteSettings,
            Name        = "Site Settings",
            Description = description,
            Alias       = ContentTypeKeys.Aliases.SiteSettingsAlias,
            Icon        = "icon-settings"
        };

        var tabId = Tab("Identity", TabIdentity, 0);
        tabId.PropertyTypes!.Add(Prop("siteDisplayName",  "Display Name",   DataTypeKeys.TextTitle,      0,  description: "Site name shown in header, footer and browser tab."));
        tabId.PropertyTypes!.Add(Prop("siteTaglineExt",   "Tagline",        DataTypeKeys.TextSubtitle,  10));
        tabId.PropertyTypes!.Add(Prop("siteLanguage",     "Language",       DataTypeKeys.TextIdentifier, 20));
        tabId.PropertyTypes!.Add(Prop("copyrightText",    "Copyright Text", DataTypeKeys.TextTitle,      30));
        tabId.PropertyTypes!.Add(Prop("siteLogoOverride", "Logo",           DataTypeKeys.MediaImage,     5,  description: "Logo shown in header and footer. Overrides ThemeSettings logo."));
        tabId.PropertyTypes!.Add(Prop("siteLogoAltText",  "Logo Alt Text",  DataTypeKeys.TextTitle,      6,  description: "Accessible alt text for the logo image."));
        ct.PropertyGroups.Add(tabId);

        var tabContact = Tab("Contact", "contact", 1);
        tabContact.PropertyTypes!.Add(Prop("contactEmail",   "Email",   DataTypeKeys.TextTitle,     0));
        tabContact.PropertyTypes!.Add(Prop("contactPhone",   "Phone",   DataTypeKeys.TextTitle,    10));
        tabContact.PropertyTypes!.Add(Prop("contactAddress", "Address", DataTypeKeys.TextAreaNotes, 20));
        tabContact.PropertyTypes!.Add(Prop("googleMapsUrl",  "Maps URL", DataTypeKeys.TextUrl,     30));
        ct.PropertyGroups.Add(tabContact);

        var tabSocial = Tab("Social", "social", 2);
        tabSocial.PropertyTypes!.Add(Prop("facebookUrl",  "Facebook",  DataTypeKeys.TextUrl, 0));
        tabSocial.PropertyTypes!.Add(Prop("instagramUrl", "Instagram", DataTypeKeys.TextUrl, 10));
        tabSocial.PropertyTypes!.Add(Prop("linkedinUrl",  "LinkedIn",  DataTypeKeys.TextUrl, 20));
        tabSocial.PropertyTypes!.Add(Prop("twitterUrl",   "Twitter/X", DataTypeKeys.TextUrl, 30));
        tabSocial.PropertyTypes!.Add(Prop("youtubeUrl",   "YouTube",   DataTypeKeys.TextUrl, 40));
        tabSocial.PropertyTypes!.Add(Prop("tiktokUrl",    "TikTok",    DataTypeKeys.TextUrl, 50));
        ct.PropertyGroups.Add(tabSocial);

        var tabSeo = Tab("SEO", "seo", 3);
        tabSeo.PropertyTypes!.Add(Prop("seoTitleSuffix",        "Title Suffix",        DataTypeKeys.TextTitle,    0, description: "e.g. \" | Brand Name\""));
        tabSeo.PropertyTypes!.Add(Prop("seoDefaultDescription", "Default Description", DataTypeKeys.TextMetaDesc, 10));
        tabSeo.PropertyTypes!.Add(Prop("seoDefaultOgImage",     "Default OG Image",    DataTypeKeys.MediaImage,   20));
        ct.PropertyGroups.Add(tabSeo);

        var tabScripts = Tab("Scripts", "scripts", 4);
        tabScripts.PropertyTypes!.Add(Prop("siteGtmId",          "GTM ID (override)",  DataTypeKeys.TextIdentifier,    0));
        tabScripts.PropertyTypes!.Add(Prop("siteHeadScripts",    "Head Scripts",       DataTypeKeys.TextScriptContent, 10));
        tabScripts.PropertyTypes!.Add(Prop("siteBodyEndScripts", "Body End Scripts",   DataTypeKeys.TextScriptContent, 20));
        ct.PropertyGroups.Add(tabScripts);

        var tabHeader = Tab("Header", TabHeader, 5);
        tabHeader.PropertyTypes!.Add(Prop("headerNavigation", "Header Nav",      DataTypeKeys.ContentPicker, 0,  description: PickNavigationGroup));
        tabHeader.PropertyTypes!.Add(Prop("showHeaderCta",    "Show Header CTA", DataTypeKeys.ToggleBoolean, 10));
        tabHeader.PropertyTypes!.Add(Prop("headerCtaLabel",   LabelCtaLabel,       DataTypeKeys.TextTitle,     20));
        tabHeader.PropertyTypes!.Add(Prop("headerCtaUrl",     LabelCtaUrl,         DataTypeKeys.LinkUrl,       30));
        ct.PropertyGroups.Add(tabHeader);

        var tabFooter = Tab("Footer", TabFooter, 6);
        tabFooter.PropertyTypes!.Add(Prop("footerNavigation",    "Footer Nav",     DataTypeKeys.ContentPicker, 0, description: PickNavigationGroup));
        tabFooter.PropertyTypes!.Add(Prop("footerCopy",          "Footer Copy",    DataTypeKeys.TextTitle,     5, description: "Copyright or legal text shown at the bottom of the footer (e.g. \"© 2025 Synergos\")."));
        tabFooter.PropertyTypes!.Add(Prop("newsletterActionUrl", "Newsletter URL", DataTypeKeys.TextUrl,      10, description: "Endpoint URL where the newsletter form POSTs (e.g. /api/newsletter/subscribe)"));
        ct.PropertyGroups.Add(tabFooter);

        var tabAlert = Tab("Alert Bar", TabAlertBar, 7);
        tabAlert.PropertyTypes!.Add(Prop("alertEnabled",     "Enabled",      DataTypeKeys.ToggleBoolean,      0,  description: "Show the alert bar above the header."));
        tabAlert.PropertyTypes!.Add(Prop("alertTitle",       "Title",        DataTypeKeys.TextTitle,          10, description: "Bold prefix text in the alert bar."));
        tabAlert.PropertyTypes!.Add(Prop("alertDescription", "Description",  DataTypeKeys.TextAreaNotes,      20, description: "Main message shown in the alert bar."));
        tabAlert.PropertyTypes!.Add(Prop("alertCtaLabel",    LabelCtaLabel,    DataTypeKeys.TextTitle,          30, description: "Text for the optional action link."));
        tabAlert.PropertyTypes!.Add(Prop("alertCtaUrl",      LabelCtaUrl,      DataTypeKeys.TextUrl,            40, description: "URL for the optional action link."));
        tabAlert.PropertyTypes!.Add(Prop("alertVariant",     "Variant",      DataTypeKeys.SelectAlertVariant, 50, description: "Visual style: info, warning, success or error."));
        tabAlert.PropertyTypes!.Add(Prop("alertDismissible", "Dismissible",  DataTypeKeys.ToggleBoolean,      60, description: "Allow the user to dismiss the alert bar."));
        ct.PropertyGroups.Add(tabAlert);

        var tabForms = Tab("Forms", "forms", 8);
        tabForms.PropertyTypes!.Add(Prop("defaultThankYouMessage", "Default Thank You", DataTypeKeys.TextAreaNotes, 0));
        tabForms.PropertyTypes!.Add(Prop("formRecipientEmail",     "Recipient Email",   DataTypeKeys.TextTitle,    10));
        ct.PropertyGroups.Add(tabForms);

        var tabBanners = Tab("Banners & Campaigns", "banners", 9);
        tabBanners.PropertyTypes!.Add(Prop("globalBanner",        "Global Banner",  DataTypeKeys.ContentPicker,  0,  description: "Pick a ReusableBlock"));
        tabBanners.PropertyTypes!.Add(Prop("globalBannerEnabled", "Banner Enabled", DataTypeKeys.ToggleBoolean,  10));
        tabBanners.PropertyTypes!.Add(Prop("campaignStartDate",   "Campaign Start", DataTypeKeys.DateTimePicker, 20));
        tabBanners.PropertyTypes!.Add(Prop("campaignEndDate",     "Campaign End",   DataTypeKeys.DateTimePicker, 30));
        ct.PropertyGroups.Add(tabBanners);

        var tabLayout = Tab("Layout", "layout", 10);
        tabLayout.PropertyTypes!.Add(Prop("defaultProfile", "Default Layout Profile", DataTypeKeys.ContentPicker, 0,
            description: "Layout Profile usado por defecto en todas las páginas del sitio sin override. Apunta a un nodo bajo <site>/Config/Layout/Profiles/."));
        ct.PropertyGroups.Add(tabLayout);

        Cts.Save(ct);
    }

    /// <summary>
    /// v11.0.0 — marks a legacy layout tab (Header, Footer, Alert Bar, Banners &amp;
    /// Campaigns) as deprecated without removing its properties. Properties remain
    /// readable for <c>LayoutConfigSeeder</c> to migrate values into the new
    /// Config/Layout nodes. Scheduled for removal in v11.1.0.
    /// </summary>
    private static bool MarkLegacyTabAsDeprecated(IContentType ct, string tabAlias, string newName, int newSort)
    {
        var tab = ct.PropertyGroups.FirstOrDefault(g => g.Alias == tabAlias);
        if (tab is null) return false;

        var dirty = false;
        if (!string.Equals(tab.Name, newName, StringComparison.Ordinal))
        { tab.Name = newName; dirty = true; }
        if (tab.SortOrder != newSort)
        { tab.SortOrder = newSort; dirty = true; }
        return dirty;
    }

    /// <summary>
    /// Ensures properties that may be missing on an already-current SiteSettings schema
    /// (e.g. added in this phase but the CT was already at new schema).
    /// Returns true if any property was added.
    /// </summary>
    private bool EnsureSiteSettingsMissingProps(IContentType ct)
    {
        var dirty = false;

        void EnsureInTab(string tabAlias, string propAlias, string propName, Guid dataTypeKey, int sortOrder, string description = "")
        {
            if (ct.PropertyTypes.Any(p => p.Alias == propAlias)) return;
            var tab = ct.PropertyGroups.FirstOrDefault(g => g.Alias == tabAlias);
            if (tab is null)
            {
                tab = Tab(propAlias == "alertEnabled" ? "Alert Bar" : tabAlias,
                          tabAlias, tabAlias == TabAlertBar ? 7 : 99);
                ct.PropertyGroups.Add(tab);
            }
            tab.PropertyTypes!.Add(Prop(propAlias, propName, dataTypeKey, sortOrder, description: description));
            dirty = true;
        }

        EnsureInTab(TabHeader,   "headerNavigation",   "Header Nav",       DataTypeKeys.ContentPicker,      0,  PickNavigationGroup);
        EnsureInTab(TabHeader,   "showHeaderCta",       "Show Header CTA",  DataTypeKeys.ToggleBoolean,     10);
        EnsureInTab(TabHeader,   "headerCtaLabel",      LabelCtaLabel,        DataTypeKeys.TextTitle,         20);
        EnsureInTab(TabHeader,   "headerCtaUrl",        LabelCtaUrl,          DataTypeKeys.LinkUrl,           30);
        EnsureInTab(TabFooter,   "footerNavigation",    "Footer Nav",       DataTypeKeys.ContentPicker,      0,  PickNavigationGroup);
        EnsureInTab(TabFooter,   "footerCopy",          "Footer Copy",      DataTypeKeys.TextTitle,          5,  "Copyright or legal text shown at the bottom of the footer.");
        EnsureInTab(TabFooter,   "newsletterActionUrl", "Newsletter URL",   DataTypeKeys.TextUrl,           10,  "Endpoint URL where the newsletter form POSTs");
        EnsureInTab(TabAlertBar, "alertEnabled",        "Enabled",          DataTypeKeys.ToggleBoolean,      0,  "Show the alert bar above the header.");
        EnsureInTab(TabAlertBar, "alertTitle",          "Title",            DataTypeKeys.TextTitle,         10,  "Bold prefix text in the alert bar.");
        EnsureInTab(TabAlertBar, "alertDescription",    "Description",      DataTypeKeys.TextAreaNotes,     20,  "Main message shown in the alert bar.");
        EnsureInTab(TabAlertBar, "alertCtaLabel",       LabelCtaLabel,        DataTypeKeys.TextTitle,         30,  "Text for the optional action link.");
        EnsureInTab(TabAlertBar, "alertCtaUrl",         LabelCtaUrl,          DataTypeKeys.TextUrl,           40,  "URL for the optional action link.");
        EnsureInTab(TabAlertBar, "alertVariant",        "Variant",          DataTypeKeys.SelectAlertVariant, 50, "Visual style: info, warning, success or error.");
        EnsureInTab(TabAlertBar, "alertDismissible",    "Dismissible",      DataTypeKeys.ToggleBoolean,     60,  "Allow the user to dismiss the alert bar.");
        EnsureInTab(TabIdentity, "siteLogoOverride",    "Logo",             DataTypeKeys.MediaImage,         5,  "Logo shown in header and footer. Overrides ThemeSettings logo.");
        EnsureInTab(TabIdentity, "siteLogoAltText",     "Logo Alt Text",    DataTypeKeys.TextTitle,          6,  "Accessible alt text for the logo image.");

        // v11.0.0 — Layout tab with default profile picker.
        if (!ct.PropertyTypes.Any(p => p.Alias == "defaultProfile"))
        {
            var layoutTab = ct.PropertyGroups.FirstOrDefault(g => g.Alias == "layout");
            if (layoutTab is null)
            {
                layoutTab = Tab("Layout", "layout", 10);
                ct.PropertyGroups.Add(layoutTab);
            }
            layoutTab.PropertyTypes!.Add(Prop("defaultProfile", "Default Layout Profile", DataTypeKeys.ContentPicker, 0,
                description: "Layout Profile usado por defecto en todas las páginas del sitio sin override."));
            dirty = true;
        }

        return dirty;
    }

    // ── LayoutProfile ─────────────────────────────────────────────────────────

    private void EnsureLayoutProfile(int folderId)
    {
        const string description = "Perfil de layout reutilizable. Compone Header, Footer, Alert Bars y Banner referenciando config nodes bajo Config/Layout.";

        var existing = Cts.Get(ContentTypeKeys.LayoutProfile);
        if (existing is not null)
        {
            var dirty = false;
            if (!string.Equals(existing.Description, description, StringComparison.Ordinal))
            { existing.Description = description; dirty = true; }
            if (existing.ParentId != folderId)
            { existing.ParentId = folderId; dirty = true; }
            dirty |= PatchIcon(existing, "icon-layers");

            dirty |= EnsureLayoutProfileMissingProps(existing);

            if (dirty) Cts.Save(existing);
            return;
        }

        var ct = new ContentType(Ssh, folderId)
        {
            Key         = ContentTypeKeys.LayoutProfile,
            Name        = "Layout Profile",
            Alias       = ContentTypeKeys.Aliases.LayoutProfileAlias,
            Description = description,
            Icon        = "icon-layers"
        };

        var tabLayout = Tab("Layout", "layout", 0);
        tabLayout.PropertyTypes!.Add(Prop("layoutName", "Profile Name", DataTypeKeys.TextTitle, 0, mandatory: true,
            description: "Nombre descriptivo del perfil (ej. 'Default', 'Landing sin footer'). Aparece en el picker."));
        tabLayout.PropertyTypes!.Add(Prop("isDefault",  "Is Default",   DataTypeKeys.ToggleBoolean, 10,
            description: "Si está activo, este profile se usa para todas las páginas del sitio que no tengan override. Debe haber uno por sitio."));
        ct.PropertyGroups.Add(tabLayout);

        var tabComposition = Tab("Composition", "composition", 1);
        tabComposition.PropertyTypes!.Add(Prop("headerConfig",         "Header",           DataTypeKeys.ContentPicker,      0,
            description: "HeaderConfig a renderizar cuando este profile está activo."));
        tabComposition.PropertyTypes!.Add(Prop("footerConfig",         "Footer",           DataTypeKeys.ContentPicker,     10,
            description: "FooterConfig a renderizar."));
        tabComposition.PropertyTypes!.Add(Prop("alertBars",            "Alert Bars",       DataTypeKeys.MultiContentPicker, 20,
            description: "AlertBarConfigs a apilar. Orden = orden visual (primera arriba)."));
        tabComposition.PropertyTypes!.Add(Prop("banner",               "Banner",           DataTypeKeys.ContentPicker,     30,
            description: "BannerConfig a renderizar (opcional)."));
        tabComposition.PropertyTypes!.Add(Prop("platformStripEnabled", "Platform Strip",   DataTypeKeys.ToggleBoolean,     40,
            description: "Muestra la barra cross-world arriba del header (Synergos · Flow Demo · Shop · Blog)."));
        ct.PropertyGroups.Add(tabComposition);

        var tabStyle = Tab("Style", "style", 2);
        tabStyle.PropertyTypes!.Add(Prop("mainWrapperStyle", "Wrapper Style", DataTypeKeys.TextTitle, 0,
            description: "Clase CSS adicional aplicada al wrapper principal (ej. 'has-dark-hero', 'is-fullwidth')."));
        ct.PropertyGroups.Add(tabStyle);

        Cts.Save(ct);
    }

    /// <summary>
    /// v11.0.0 — ensures the LayoutProfile CT has the new composition pickers
    /// when upgrading from a prior schema (which had headerNavigation /
    /// footerNavigation / showAlertBar / showBanner instead).
    /// </summary>
    private bool EnsureLayoutProfileMissingProps(IContentType ct)
    {
        var dirty = false;

        void EnsureInTab(string tabAlias, string tabName, int tabSort,
            string propAlias, string propName, Guid dataTypeKey, int sortOrder, string description = "")
        {
            if (ct.PropertyTypes.Any(p => p.Alias == propAlias)) return;
            var tab = ct.PropertyGroups.FirstOrDefault(g => g.Alias == tabAlias);
            if (tab is null)
            {
                tab = Tab(tabName, tabAlias, tabSort);
                ct.PropertyGroups.Add(tab);
            }
            tab.PropertyTypes!.Add(Prop(propAlias, propName, dataTypeKey, sortOrder, description: description));
            dirty = true;
        }

        EnsureInTab("layout",      "Layout",      0, "isDefault",            "Is Default",     DataTypeKeys.ToggleBoolean,     10, "Si está activo, este profile se usa para todas las páginas del sitio sin override.");
        EnsureInTab("composition", "Composition", 1, "headerConfig",         "Header",         DataTypeKeys.ContentPicker,      0, "HeaderConfig a renderizar cuando este profile está activo.");
        EnsureInTab("composition", "Composition", 1, "footerConfig",         "Footer",         DataTypeKeys.ContentPicker,     10, "FooterConfig a renderizar.");
        EnsureInTab("composition", "Composition", 1, "alertBars",            "Alert Bars",     DataTypeKeys.MultiContentPicker, 20, "AlertBarConfigs a apilar.");
        EnsureInTab("composition", "Composition", 1, "banner",               "Banner",         DataTypeKeys.ContentPicker,     30, "BannerConfig a renderizar (opcional).");
        EnsureInTab("composition", "Composition", 1, "platformStripEnabled", "Platform Strip", DataTypeKeys.ToggleBoolean,     40, "Muestra la barra cross-world arriba del header.");

        return dirty;
    }
}
