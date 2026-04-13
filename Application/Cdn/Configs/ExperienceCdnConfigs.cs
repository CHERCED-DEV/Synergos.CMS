namespace Synergos.CMS.Application.Cdn.Configs;

// ── CDN Experience Configs ────────────────────────────────────────────────────
// Mirror of vitals/contracts/src/element-config.contract.ts — Experience tier.
// Translations: assembled at render time from Umbraco dictionary; never a macro param.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Mirror of FeatureJourneyElementConfig.</summary>
public sealed record FeatureJourneyCdnConfig(
    string? Title     = null,
    string? Theme     = null,
    string? Variant   = null,
    string? ElementId = null,
    IReadOnlyDictionary<string, string>? Translations = null);

/// <summary>Mirror of InsightExplorerElementConfig.</summary>
public sealed record InsightExplorerCdnConfig(
    string? Title     = null,
    string? Theme     = null,
    string? Variant   = null,
    string? ElementId = null,
    IReadOnlyDictionary<string, string>? Translations = null);

/// <summary>Mirror of MediaExplorerElementConfig.</summary>
public sealed record MediaExplorerCdnConfig(
    string? Title           = null,
    string? Theme           = null,
    string? Variant         = null,
    string? ElementId       = null,
    string? DefaultCategory = null,
    IReadOnlyDictionary<string, string>? Translations = null);

/// <summary>Mirror of ContentCarouselElementConfig.</summary>
public sealed record ContentCarouselCdnConfig(
    string? Title     = null,
    string? Theme     = null,
    string? Variant   = null,
    string? ElementId = null,
    bool?   Autoplay  = null,
    int?    Interval  = null,
    IReadOnlyDictionary<string, string>? Translations = null);

/// <summary>Mirror of QuizFlowQuestion — item shape.</summary>
public sealed record QuizFlowQuestionCdnConfig(
    string? Text    = null,
    string? Type    = null,
    IReadOnlyList<string>? Options = null);

/// <summary>Mirror of QuizFlowElementConfig.</summary>
public sealed record QuizFlowCdnConfig(
    string? Title       = null,
    string? Theme       = null,
    string? Variant     = null,
    string? ElementId   = null,
    string? SubmitLabel = null,
    IReadOnlyList<QuizFlowQuestionCdnConfig>? Questions = null,
    IReadOnlyDictionary<string, string>? Translations = null);

/// <summary>Mirror of FilterBoardElementConfig.</summary>
public sealed record FilterBoardCdnConfig(
    string? Title         = null,
    string? Theme         = null,
    string? Variant       = null,
    string? ElementId     = null,
    string? DefaultFilter = null,
    IReadOnlyDictionary<string, string>? Translations = null);

/// <summary>Mirror of RatingWidgetElementConfig.</summary>
public sealed record RatingWidgetCdnConfig(
    string? Title     = null,
    string? Theme     = null,
    string? Variant   = null,
    string? ElementId = null,
    int?    MaxRating = null,
    IReadOnlyDictionary<string, string>? Translations = null);

/// <summary>Mirror of CountdownClockElementConfig.</summary>
public sealed record CountdownClockCdnConfig(
    string? Title       = null,
    string? Theme       = null,
    string? Variant     = null,
    string? ElementId   = null,
    string? TargetDate  = null,
    string? ExpiredText = null,
    IReadOnlyDictionary<string, string>? Translations = null);

/// <summary>Mirror of NotificationStackElementConfig.</summary>
public sealed record NotificationStackCdnConfig(
    string? Title     = null,
    string? Theme     = null,
    string? Variant   = null,
    string? ElementId = null,
    int?    MaxItems  = null,
    IReadOnlyDictionary<string, string>? Translations = null);
