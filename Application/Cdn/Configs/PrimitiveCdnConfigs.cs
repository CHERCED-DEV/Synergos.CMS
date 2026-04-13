namespace Synergos.CMS.Application.Cdn.Configs;

// ── CDN Primitive Configs ─────────────────────────────────────────────────────
// Mirror of vitals/contracts/src/element-config.contract.ts — Primitive tier.
// Translations: assembled at render time from Umbraco dictionary; never a macro param.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Mirror of BadgeElementConfig.</summary>
public sealed record BadgeCdnConfig(
    string? Text      = null,
    string? Tone      = null,
    string? AriaLabel = null,
    IReadOnlyDictionary<string, string>? Translations = null);

/// <summary>Mirror of ButtonContainerElementConfig.</summary>
public sealed record ButtonContainerCdnConfig(
    string? Label  = null,
    string? Href   = null,
    string? Target = null,
    IReadOnlyDictionary<string, string>? Translations = null);

/// <summary>Mirror of ColumnElementConfig.</summary>
public sealed record ColumnCdnConfig(
    string? Variant = null,
    string? Theme   = null,
    IReadOnlyDictionary<string, string>? Translations = null);

/// <summary>Mirror of ContainerBlockElementConfig.</summary>
public sealed record ContainerBlockCdnConfig(
    string? ElementId = null,
    string? AriaLabel = null,
    string? Variant   = null,
    string? Theme     = null,
    IReadOnlyDictionary<string, string>? Translations = null);

/// <summary>Mirror of DividerElementConfig.</summary>
public sealed record DividerCdnConfig(
    string? Variant = null,
    string? Theme   = null,
    IReadOnlyDictionary<string, string>? Translations = null);

/// <summary>Mirror of GridElementConfig.</summary>
public sealed record GridCdnConfig(
    string? Variant = null,
    string? Theme   = null,
    IReadOnlyDictionary<string, string>? Translations = null);

/// <summary>Mirror of IconBlockElementConfig.</summary>
public sealed record IconBlockCdnConfig(
    string? AriaLabel = null,
    IReadOnlyDictionary<string, string>? Translations = null);

/// <summary>Mirror of ImageBlockElementConfig.</summary>
public sealed record ImageBlockCdnConfig(
    string? Src = null,
    string? Alt = null,
    IReadOnlyDictionary<string, string>? Translations = null);

/// <summary>Mirror of LinkBlockElementConfig.</summary>
public sealed record LinkBlockCdnConfig(
    string? Href      = null,
    string? Label     = null,
    string? Target    = null,
    string? AriaLabel = null,
    IReadOnlyDictionary<string, string>? Translations = null);

/// <summary>Mirror of SpacerElementConfig (empty config).</summary>
public sealed record SpacerCdnConfig(
    IReadOnlyDictionary<string, string>? Translations = null);

/// <summary>Mirror of StackElementConfig.</summary>
public sealed record StackCdnConfig(
    string? Variant = null,
    string? Theme   = null,
    IReadOnlyDictionary<string, string>? Translations = null);

/// <summary>Mirror of TextBlockElementConfig.</summary>
public sealed record TextBlockCdnConfig(
    string? HeadingText  = null,
    string? HeadingLevel = null,
    string? Variant      = null,
    string? Theme        = null,
    IReadOnlyDictionary<string, string>? Translations = null);

/// <summary>Mirror of VideoBlockElementConfig.</summary>
public sealed record VideoBlockCdnConfig(
    string? Src   = null,
    string? Title = null,
    IReadOnlyDictionary<string, string>? Translations = null);

/// <summary>Mirror of AvatarElementConfig.</summary>
public sealed record AvatarCdnConfig(
    string? Src     = null,
    string? Alt     = null,
    string? Name    = null,
    string? Size    = null,
    string? Variant = null,
    IReadOnlyDictionary<string, string>? Translations = null);
