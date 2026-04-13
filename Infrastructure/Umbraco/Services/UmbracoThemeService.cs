using Synergos.CMS.Application;
using Umbraco.Cms.Core.Models.PublishedContent;
using Synergos.CMS.Application.MultiApp;
using Synergos.CMS.Application.Theming;
using Synergos.CMS.Domain.Services;
using Synergos.CMS.Schema.Constants;

namespace Synergos.CMS.Infrastructure.Umbraco.Services;

/// <summary>
/// Resolves theme tokens and layout profile from the CMS content tree.
///
/// Responsibilities:
///   - GetLayoutConfig  — brand identity (logo, name, tagline) from SiteSettings + ThemeSettings
///   - GetThemeConfig   — CSS custom properties from ThemeSettings, merged with IdentityRegistry presets
///   - GetPageLayoutConfig — per-page header/footer/alertBar visibility from CompDomLayoutProfile composition
/// </summary>
public sealed class UmbracoThemeService : IThemeService
{
    private readonly IContentContextAccessor _accessor;

    public UmbracoThemeService(IContentContextAccessor accessor) => _accessor = accessor;

    // ── GetLayoutConfig ───────────────────────────────────────────────────────

    public LayoutConfig GetLayoutConfig(int rootNodeId)
    {
        // Prefer the explicitly requested rootNodeId so multi-site callers get the right site.
        // Fall back to context.ApplicationRoot only when the node cannot be resolved by ID
        // (e.g. during warmup before content cache is populated).
        var root = _accessor.GetById(rootNodeId)
                ?? LayoutContentResolver.Resolve(_accessor).ApplicationRoot;

        if (root is null)
        {
            return new LayoutConfig(
                ShowHeader:     true,
                ShowFooter:     true,
                NavigationType: "main",
                ThemeVariant:   "light",
                Brand: new BrandConfig("/", null, null, "Synergos", null));
        }

        var themeSettings = root.FirstChild(ContentTypeKeys.Aliases.ThemeSettings);
        var siteSettings  = root.FirstChild(ContentTypeKeys.Aliases.SiteSettingsAlias);

        // Logo cascade: siteSettings.siteLogoOverride → ThemeSettings.logoLight → ThemeSettings.logoDark
        var logoUrl = (siteSettings is not null
                          ? LayoutContentResolver.ReadMediaUrl(siteSettings, "siteLogoOverride")
                          : null)
                   ?? (themeSettings is not null
                          ? LayoutContentResolver.ReadMediaUrl(themeSettings, "logoLight", "logoDark")
                          : null);

        return new LayoutConfig(
            ShowHeader:     true,
            ShowFooter:     true,
            NavigationType: "main",
            ThemeVariant:   "light",
            Brand: new BrandConfig(
                HomeUrl:     root.Url() ?? "/",
                LogoUrl:     logoUrl,
                LogoAlt:     siteSettings?.Value<string>("siteLogoAltText"),
                SiteName:    siteSettings?.Value<string>("siteDisplayName"),
                SiteTagline: siteSettings?.Value<string>("siteTaglineExt")));
    }

    // ── GetThemeConfig ────────────────────────────────────────────────────────

    public ThemeConfig? GetThemeConfig(int rootNodeId)
    {
        var root  = _accessor.GetById(rootNodeId);
        var theme = root?.FirstChild(ContentTypeKeys.Aliases.ThemeSettings);
        if (theme is null) return null;

        string? Tv(string alias) => LayoutContentResolver.ReadText(theme, alias);

        var preset = LayoutContentResolver.ReadText(theme, "identityPreset");
        var brand  = IdentityRegistry.Get(preset);

        return new ThemeConfig(
            HeadingFontUrl:    Tv("headingFontUrl"),
            BodyFontUrl:       Tv("bodyFontUrl"),
            FaviconUrl:        Tv("faviconUrl") ?? LayoutContentResolver.ReadMediaUrl(theme, "logoIcon"),
            ColorPrimary:      Tv("colorPrimary"),
            ColorSecondary:    Tv("colorSecondary"),
            ColorAccent:       Tv("colorAccent"),
            ColorBackground:   Tv("colorBackground"),
            ColorSurface:      Tv("colorSurface"),
            ColorText:         Tv("colorText"),
            ColorTextInverse:  Tv("colorTextInverse"),
            ColorBorder:       Tv("colorBorder"),
            ColorSuccess:      Tv("colorSuccess"),
            ColorWarning:      Tv("colorWarning"),
            ColorError:        Tv("colorError"),
            FontFamilyHeading: Tv("fontFamilyHeading"),
            FontFamilyBody:    Tv("fontFamilyBody"),
            FontFamilyMono:    Tv("fontFamilyMono"),
            FontBaseSize:      Tv("fontBaseSize"),
            FontScaleRatio:    Tv("fontScaleRatio"),
            SpacingUnit:       Tv("spacingUnit"),
            ContainerMaxWidth: Tv("containerMaxWidth"),
            BorderRadius:      Tv("borderRadius"),
            SectionPaddingY:   Tv("sectionPaddingY"),
            CustomCssSnippet:  Tv("customCssSnippet"),
            IdentityPreset:           preset,
            BrandGradientHero:        Tv("brandGradientHero")        ?? brand?.BrandGradientHero,
            BrandGlowColor:           Tv("brandGlowColor")           ?? brand?.BrandGlowColor,
            BrandSurfaceGlassBg:      Tv("brandSurfaceGlassBg")      ?? brand?.BrandSurfaceGlassBg,
            BrandSurfaceBackdropBlur: Tv("brandSurfaceBackdropBlur") ?? brand?.BrandSurfaceBackdropBlur,
            BrandParticleEnabled:     Tv("brandParticleEnabled")     ?? brand?.BrandParticleEnabled,
            BrandLogoFilter:          Tv("brandLogoFilter")          ?? brand?.BrandLogoFilter,
            BrandHeroOverlay:         Tv("brandHeroOverlay")         ?? brand?.BrandHeroOverlay,
            BrandHeadingWeight:       Tv("brandHeadingWeight")       ?? brand?.BrandHeadingWeight,
            BrandHeadingLetterSpacing: Tv("brandHeadingLetterSpacing") ?? brand?.BrandHeadingLetterSpacing,
            BrandDividerGradient:     Tv("brandDividerGradient")     ?? brand?.BrandDividerGradient,
            BrandCtaGlow:             Tv("brandCtaGlow")             ?? brand?.BrandCtaGlow,
            ColorSurfaceElevated:     Tv("colorSurfaceElevated")     ?? brand?.ColorSurfaceElevated,
            ColorTextSecondary:       Tv("colorTextSecondary")       ?? brand?.ColorTextSecondary,
            ColorBorderSubtle:        Tv("colorBorderSubtle")        ?? brand?.ColorBorderSubtle,
            ColorActionPrimary:       Tv("colorActionPrimary")       ?? brand?.ColorActionPrimary,
            ColorActionPrimaryHover:  Tv("colorActionPrimaryHover")  ?? brand?.ColorActionPrimaryHover,
            ColorActionPrimaryText:   Tv("colorActionPrimaryText")   ?? brand?.ColorActionPrimaryText,
            ColorInfo:                Tv("colorInfo")                ?? brand?.ColorInfo);
    }

    // ── GetPageLayoutConfig ───────────────────────────────────────────────────

    public PageLayoutConfig GetPageLayoutConfig(int? pageId)
    {
        if (pageId is null) return PageLayoutConfig.Default;

        var page = _accessor.GetById(pageId.Value);
        if (page is null) return PageLayoutConfig.Default;

        if (!page.Properties.Any(p => p.Alias == "useDefaultLayout"))
            return PageLayoutConfig.Default;

        var useDefault = page.Value<bool>("useDefaultLayout");

        if (useDefault)
        {
            return new PageLayoutConfig(
                UseDefaultLayout:        true,
                ShowHeader:              page.Value<bool?>("showHeader") ?? true,
                ShowFooter:              page.Value<bool?>("showFooter") ?? true,
                LayoutProfileNodeId:     null,
                ShowAlertBar:            true,
                ShowBanner:              true,
                MainWrapperStyle:        null,
                OverrideHeaderNavNodeId: null,
                OverrideFooterNavNodeId: null);
        }

        var profileNode = LayoutContentResolver.ReadPickerContent(page, "layoutProfile");

        return new PageLayoutConfig(
            UseDefaultLayout:        false,
            ShowHeader:              page.Value<bool?>("showHeader") ?? true,
            ShowFooter:              page.Value<bool?>("showFooter") ?? true,
            LayoutProfileNodeId:     profileNode?.Id,
            ShowAlertBar:            profileNode?.Value<bool>("showAlertBar") ?? true,
            ShowBanner:              profileNode?.Value<bool>("showBanner")   ?? true,
            MainWrapperStyle:        profileNode?.Value<string>("mainWrapperStyle"),
            OverrideHeaderNavNodeId: profileNode is null ? null : LayoutContentResolver.ReadPickerContent(profileNode, "headerNavigation")?.Id,
            OverrideFooterNavNodeId: profileNode is null ? null : LayoutContentResolver.ReadPickerContent(profileNode, "footerNavigation")?.Id);
    }
}
