namespace Synergos.CMS.Domain.Services;

/// <summary>
/// Unified layout service — aggregates all focused ISP-compliant layout interfaces.
/// Callers that only need one concern (e.g. header, SEO) should inject the specific
/// focused interface instead; this aggregate remains for ViewComponents and controllers
/// that need multiple concerns in a single dependency.
/// </summary>
public interface ILayoutService
    : IThemeService,
      IHeaderService,
      IFooterService,
      IAlertBarService,
      IBannerService,
      ITrackingService,
      ISeoService
{
}

/// <summary>Site-level layout configuration.</summary>
public sealed record LayoutConfig(
    bool ShowHeader,
    bool ShowFooter,
    string NavigationType,
    string ThemeVariant,
    BrandConfig Brand);

/// <summary>Branding properties read from the CMS site root.</summary>
public sealed record BrandConfig(
    string  HomeUrl,
    string? LogoUrl,
    string? LogoAlt,
    string? SiteName,
    string? SiteTagline);

/// <summary>Alert bar configuration read from SiteSettings.</summary>
public sealed record AlertBarConfig(
    string? Title,
    string? Description,
    string? CtaLabel,
    string? CtaUrl,
    string? Variant,
    bool    Dismissible);

/// <summary>
/// Global promotional banner config. Non-null only when the banner is enabled,
/// within its campaign date window, and a ReusableBlock node is configured.
/// </summary>
public sealed record BannerConfig(int BannerNodeId);

/// <summary>Social link entry for footer rendering.</summary>
public sealed record SocialLinkConfig(string Label, string Url, string Platform);

/// <summary>Header-specific settings read from SiteSettings.</summary>
public sealed record SiteHeaderConfig(
    int?    NavigationNodeId,
    bool    ShowCta,
    string? CtaLabel,
    string? CtaUrl);

/// <summary>Footer-specific settings read from SiteSettings.</summary>
public sealed record SiteFooterConfig(
    int?                         FooterNavigationNodeId,
    string?                      CopyrightText,
    string?                      NewsletterActionUrl,
    IReadOnlyList<SocialLinkConfig> SocialLinks);

/// <summary>Raw HTML for analytics/tracking script injection.</summary>
public sealed record TrackingScriptsConfig(
    string? HeadScripts,
    string? BodyScripts);

/// <summary>
/// Site-level SEO fallback values. Applied when a page has no explicit meta values.
/// TitleSuffix is appended to the page title (e.g. " | Brand Name").
/// </summary>
public sealed record SeoDefaultsConfig(
    string? TitleSuffix,
    string? DefaultDescription,
    string? DefaultOgImageUrl);

/// <summary>Contact information from SiteSettings — email, phone, address, maps URL.</summary>
public sealed record ContactInfoConfig(
    string? Email,
    string? Phone,
    string? Address,
    string? GoogleMapsUrl);

/// <summary>
/// All CSS custom properties resolved from the ThemeSettings child node.
/// Brand token fields (Brand*, ColorSurfaceElevated, etc.) are populated from
/// IdentityRegistry when identityPreset != "custom", with CMS field values as overrides.
/// </summary>
public sealed record ThemeConfig(
    string? HeadingFontUrl,
    string? BodyFontUrl,
    string? FaviconUrl,
    string? ColorPrimary,
    string? ColorSecondary,
    string? ColorAccent,
    string? ColorBackground,
    string? ColorSurface,
    string? ColorText,
    string? ColorTextInverse,
    string? ColorBorder,
    string? ColorSuccess,
    string? ColorWarning,
    string? ColorError,
    string? FontFamilyHeading,
    string? FontFamilyBody,
    string? FontFamilyMono,
    string? FontBaseSize,
    string? FontScaleRatio,
    string? SpacingUnit,
    string? ContainerMaxWidth,
    string? BorderRadius,
    string? SectionPaddingY,
    string? CustomCssSnippet,
    // ── Multi-identity brand tokens ──────────────────────────────────────────
    string? IdentityPreset           = null,
    string? BrandGradientHero        = null,
    string? BrandGlowColor           = null,
    string? BrandSurfaceGlassBg      = null,
    string? BrandSurfaceBackdropBlur = null,
    string? BrandParticleEnabled     = null,
    string? BrandLogoFilter          = null,
    string? BrandHeroOverlay         = null,
    string? BrandHeadingWeight       = null,
    string? BrandHeadingLetterSpacing = null,
    string? BrandDividerGradient     = null,
    string? BrandCtaGlow             = null,
    string? ColorSurfaceElevated     = null,
    string? ColorTextSecondary       = null,
    string? ColorBorderSubtle        = null,
    string? ColorActionPrimary       = null,
    string? ColorActionPrimaryHover  = null,
    string? ColorActionPrimaryText   = null,
    string? ColorInfo                = null);

/// <summary>
/// Per-page layout configuration resolved from CompDomLayoutProfile composition.
/// When UseDefaultLayout is true the site-level header/footer settings apply.
/// When false, ShowHeader/ShowFooter and the optional LayoutProfileNodeId govern rendering.
/// </summary>
public sealed record PageLayoutConfig(
    bool  UseDefaultLayout,
    bool  ShowHeader,
    bool  ShowFooter,
    int?  LayoutProfileNodeId,
    bool  ShowAlertBar,
    bool  ShowBanner,
    string? MainWrapperStyle,
    int?  OverrideHeaderNavNodeId,
    int?  OverrideFooterNavNodeId)
{
    /// <summary>Returns a safe default: use site defaults, show header and footer.</summary>
    public static PageLayoutConfig Default { get; } = new(
        UseDefaultLayout:        true,
        ShowHeader:              true,
        ShowFooter:              true,
        LayoutProfileNodeId:     null,
        ShowAlertBar:            true,
        ShowBanner:              true,
        MainWrapperStyle:        null,
        OverrideHeaderNavNodeId: null,
        OverrideFooterNavNodeId: null);
}
