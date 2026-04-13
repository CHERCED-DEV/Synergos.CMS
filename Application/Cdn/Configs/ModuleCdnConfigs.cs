namespace Synergos.CMS.Application.Cdn.Configs;

// ── CDN Module Configs ────────────────────────────────────────────────────────
// Mirror of vitals/contracts/src/element-config.contract.ts — Module tier.
// All properties nullable. Serialized to camelCase via JsonNamingPolicy.CamelCase.
// Translations: assembled at render time from Umbraco dictionary; never a macro param.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Mirror of HeroElementConfig.</summary>
public sealed record HeroCdnConfig(
    string? HeadingText    = null,
    string? HeadingLevel   = null,
    string? Body           = null,
    string? ImageSrc       = null,
    string? ImageAlt       = null,
    string? CtaLabel       = null,
    string? CtaUrl         = null,
    string? CtaTarget      = null,
    string? Variant        = null,
    string? Theme          = null,
    IReadOnlyDictionary<string, string>? Translations = null);

/// <summary>Mirror of BannerElementConfig.</summary>
public sealed record BannerCdnConfig(
    string? Title              = null,
    string? Body               = null,
    string? Eyebrow            = null,
    string? ImageSrc           = null,
    string? ImageAlt           = null,
    string? CtaLabel           = null,
    string? CtaUrl             = null,
    string? CtaTarget          = null,
    string? SecondaryCtaLabel  = null,
    string? SecondaryCtaUrl    = null,
    string? SecondaryCtaTarget = null,
    string? Variant            = null,
    string? Theme              = null,
    IReadOnlyDictionary<string, string>? Translations = null);

/// <summary>Mirror of BannerSliderElementConfig — slide items.</summary>
public sealed record BannerSliderSlideCdnConfig(
    string? ImageSrc  = null,
    string? ImageAlt  = null,
    string? Title     = null,
    string? Body      = null,
    string? CtaLabel  = null,
    string? CtaUrl    = null,
    string? CtaTarget = null);

/// <summary>Mirror of BannerSliderElementConfig.</summary>
public sealed record BannerSliderCdnConfig(
    string? HeadingText = null,
    string? Body        = null,
    string? Variant     = null,
    string? Theme       = null,
    bool?   Autoplay    = null,
    bool?   Loop        = null,
    IReadOnlyList<BannerSliderSlideCdnConfig>? Slides = null,
    IReadOnlyDictionary<string, string>? Translations = null);

/// <summary>Mirror of SectionElementConfig.</summary>
public sealed record SectionCdnConfig(
    string? Variant = null,
    string? Theme   = null,
    IReadOnlyDictionary<string, string>? Translations = null);

/// <summary>Mirror of FeatureGridElementConfig — item shape.</summary>
public sealed record FeatureGridItemCdnConfig(
    string? HeadingText = null,
    string? Body        = null,
    string? Icon        = null);

/// <summary>Mirror of FeatureGridElementConfig.</summary>
public sealed record FeatureGridCdnConfig(
    string? HeadingText = null,
    string? Variant     = null,
    string? Theme       = null,
    int?    Columns     = null,
    IReadOnlyList<FeatureGridItemCdnConfig>? Items = null,
    IReadOnlyDictionary<string, string>? Translations = null);

/// <summary>Mirror of FaqSectionElementConfig — item shape.</summary>
public sealed record FaqSectionItemCdnConfig(
    string? Question          = null,
    string? Answer            = null,
    bool?   InitiallyExpanded = null);

/// <summary>Mirror of FaqSectionElementConfig.</summary>
public sealed record FaqSectionCdnConfig(
    string? HeadingText = null,
    string? Theme       = null,
    IReadOnlyList<FaqSectionItemCdnConfig>? Items = null,
    IReadOnlyDictionary<string, string>? Translations = null);

/// <summary>Mirror of TestimonialSectionElementConfig — item shape.</summary>
public sealed record TestimonialSectionItemCdnConfig(
    string? Quote     = null,
    string? Name      = null,
    string? Role      = null,
    string? AvatarSrc = null,
    string? AvatarAlt = null);

/// <summary>Mirror of TestimonialSectionElementConfig.</summary>
public sealed record TestimonialSectionCdnConfig(
    string? HeadingText = null,
    string? Theme       = null,
    IReadOnlyList<TestimonialSectionItemCdnConfig>? Items = null,
    IReadOnlyDictionary<string, string>? Translations = null);

/// <summary>Mirror of LogoCloudElementConfig — item shape.</summary>
public sealed record LogoCloudItemCdnConfig(
    string? Src    = null,
    string? Alt    = null,
    string? Label  = null,
    string? Href   = null,
    string? Target = null);

/// <summary>Mirror of LogoCloudElementConfig.</summary>
public sealed record LogoCloudCdnConfig(
    string? HeadingText = null,
    string? Body        = null,
    string? Variant     = null,
    string? Theme       = null,
    IReadOnlyList<LogoCloudItemCdnConfig>? Items = null,
    IReadOnlyDictionary<string, string>? Translations = null);

/// <summary>Mirror of TabGroupElementConfig.</summary>
public sealed record TabGroupCdnConfig(
    string? Title     = null,
    string? AriaLabel = null,
    string? Variant   = null,
    string? Theme     = null,
    IReadOnlyDictionary<string, string>? Translations = null);

/// <summary>Mirror of DataTableElementConfig.</summary>
public sealed record DataTableCdnConfig(
    string?                              Caption      = null,
    IReadOnlyList<string>?               Headers      = null,
    IReadOnlyList<IReadOnlyList<string>>? Rows        = null,
    IReadOnlyDictionary<string, string>? Translations = null);

/// <summary>Mirror of ScriptEmbedElementConfig.</summary>
public sealed record ScriptEmbedCdnConfig(
    string? ScriptType = null,
    string? Content    = null,
    IReadOnlyDictionary<string, string>? Translations = null);
