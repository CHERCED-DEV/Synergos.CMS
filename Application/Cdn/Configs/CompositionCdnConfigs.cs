namespace Synergos.CMS.Application.Cdn.Configs;

// ── CDN Composition Configs ───────────────────────────────────────────────────
// Mirror of vitals/contracts/src/element-config.contract.ts — Composition tier.
// Translations: assembled at render time from Umbraco dictionary; never a macro param.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Mirror of CardElementConfig.</summary>
public sealed record CardCdnConfig(
    string? Title     = null,
    string? Subtitle  = null,
    string? Body      = null,
    string? ImageSrc  = null,
    string? ImageAlt  = null,
    string? CtaLabel  = null,
    string? CtaUrl    = null,
    string? BadgeText = null,
    string? BadgeType = null,
    string? Variant   = null,
    string? Theme     = null,
    IReadOnlyDictionary<string, string>? Translations = null);

/// <summary>Mirror of MediaTextElementConfig.</summary>
public sealed record MediaTextCdnConfig(
    string? ImageSrc      = null,
    string? ImageAlt      = null,
    string? HeadingText   = null,
    string? Body          = null,
    string? CtaLabel      = null,
    string? CtaUrl        = null,
    string? CtaTarget     = null,
    string? MediaPosition = null,
    string? Variant       = null,
    string? Theme         = null,
    IReadOnlyDictionary<string, string>? Translations = null);

/// <summary>Mirror of InfoBlockElementConfig.</summary>
public sealed record InfoBlockCdnConfig(
    string? Title    = null,
    string? Body     = null,
    string? CtaLabel = null,
    string? CtaUrl   = null,
    string? Variant  = null,
    string? Theme    = null,
    IReadOnlyDictionary<string, string>? Translations = null);

/// <summary>Mirror of CtaGroupElementConfig.</summary>
public sealed record CtaGroupCdnConfig(
    string? PrimaryLabel    = null,
    string? PrimaryUrl      = null,
    string? PrimaryTarget   = null,
    string? PrimaryVariant  = null,
    string? SecondaryLabel  = null,
    string? SecondaryUrl    = null,
    string? SecondaryTarget = null,
    string? SecondaryVariant = null,
    string? Alignment       = null,
    IReadOnlyDictionary<string, string>? Translations = null);

/// <summary>Mirror of ButtonGroupElementConfig — item shape.</summary>
public sealed record ButtonGroupItemCdnConfig(
    string? Label   = null,
    string? Href    = null,
    string? Target  = null,
    string? Variant = null,
    string? Size    = null,
    bool?   Disabled = null);

/// <summary>Mirror of ButtonGroupElementConfig.</summary>
public sealed record ButtonGroupCdnConfig(
    string? Alignment = null,
    string? Direction = null,
    string? Gap       = null,
    IReadOnlyList<ButtonGroupItemCdnConfig>? Items = null,
    IReadOnlyDictionary<string, string>? Translations = null);

/// <summary>Mirror of FeatureItemElementConfig.</summary>
public sealed record FeatureItemCdnConfig(
    string? HeadingText = null,
    string? Body        = null,
    string? Icon        = null,
    string? Variant     = null,
    string? Theme       = null,
    IReadOnlyDictionary<string, string>? Translations = null);

/// <summary>Mirror of KeyValueElementConfig.</summary>
public sealed record KeyValueCdnConfig(
    string? Label    = null,
    string? Value    = null,
    string? HelpText = null,
    string? Theme    = null,
    IReadOnlyDictionary<string, string>? Translations = null);

/// <summary>Mirror of TimelineItemElementConfig.</summary>
public sealed record TimelineItemCdnConfig(
    string? HeadingText = null,
    string? Body        = null,
    string? Date        = null,
    string? Variant     = null,
    string? Theme       = null,
    IReadOnlyDictionary<string, string>? Translations = null);

/// <summary>Mirror of FaqItemElementConfig.</summary>
public sealed record FaqItemCdnConfig(
    string? Question = null,
    string? Answer   = null,
    string? Theme    = null,
    IReadOnlyDictionary<string, string>? Translations = null);

/// <summary>Mirror of TestimonialItemElementConfig.</summary>
public sealed record TestimonialItemCdnConfig(
    string? Quote     = null,
    string? Name      = null,
    string? Role      = null,
    string? AvatarSrc = null,
    string? AvatarAlt = null,
    string? Theme     = null,
    IReadOnlyDictionary<string, string>? Translations = null);

/// <summary>Mirror of GalleryItemElementConfig.</summary>
public sealed record GalleryItemCdnConfig(
    string? Src     = null,
    string? Alt     = null,
    string? Caption = null,
    IReadOnlyDictionary<string, string>? Translations = null);

/// <summary>Mirror of LogoItemElementConfig.</summary>
public sealed record LogoItemCdnConfig(
    string? Src    = null,
    string? Alt    = null,
    string? Href   = null,
    string? Label  = null,
    string? Target = null,
    IReadOnlyDictionary<string, string>? Translations = null);

/// <summary>Mirror of AlertBarElementConfig.</summary>
public sealed record AlertBarCdnConfig(
    string? Title       = null,
    string? Description = null,
    string? CtaLabel    = null,
    string? CtaUrl      = null,
    string? Variant     = null,
    string? Theme       = null,
    bool?   Dismissible = null,
    IReadOnlyDictionary<string, string>? Translations = null);

/// <summary>Mirror of NewsletterFormElementConfig.</summary>
public sealed record NewsletterFormCdnConfig(
    string? Title          = null,
    string? Intro          = null,
    string? Placeholder    = null,
    string? SubmitLabel    = null,
    string? ConsentText    = null,
    string? SuccessMessage = null,
    string? ErrorMessage   = null,
    string? ActionUrl      = null,
    string? Method         = null,
    string? Theme          = null,
    IReadOnlyDictionary<string, string>? Translations = null);

/// <summary>Mirror of SocialShareElementConfig — link shape.</summary>
public sealed record SocialShareLinkCdnConfig(
    string? Label      = null,
    string? Href       = null,
    string? IconSymbol = null,
    string? Target     = null);

/// <summary>Mirror of SocialShareElementConfig.</summary>
public sealed record SocialShareCdnConfig(
    string? Title   = null,
    string? PageUrl = null,
    string? Layout  = null,
    IReadOnlyList<SocialShareLinkCdnConfig>? Links = null,
    IReadOnlyDictionary<string, string>? Translations = null);

/// <summary>Mirror of IframeEmbedElementConfig.</summary>
public sealed record IframeEmbedCdnConfig(
    string? Src             = null,
    string? Title           = null,
    string? Height          = null,
    bool?   AllowFullscreen = null,
    IReadOnlyDictionary<string, string>? Translations = null);

/// <summary>Mirror of ExternalWidgetElementConfig.</summary>
public sealed record ExternalWidgetCdnConfig(
    string? Src      = null,
    string? Type     = null,
    string? Title    = null,
    string? Endpoint = null,
    IReadOnlyDictionary<string, string>? Translations = null);

/// <summary>Mirror of StatElementConfig.</summary>
public sealed record StatCdnConfig(
    string? Value   = null,
    string? Label   = null,
    string? Prefix  = null,
    string? Suffix  = null,
    string? Variant = null,
    string? Theme   = null,
    IReadOnlyDictionary<string, string>? Translations = null);

/// <summary>Mirror of PricingCardElementConfig.</summary>
public sealed record PricingCardCdnConfig(
    string? Title       = null,
    string? Price       = null,
    string? Period      = null,
    string? Description = null,
    string? CtaLabel    = null,
    string? CtaUrl      = null,
    string? BadgeText   = null,
    string? BadgeTone   = null,
    string? Variant     = null,
    string? Theme       = null,
    bool?   Featured    = null,
    IReadOnlyDictionary<string, string>? Translations = null);
