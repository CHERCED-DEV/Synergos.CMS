using Synergos.CMS.Application.Components;
using Synergos.CMS.Domain.Compositions.Content;

namespace Synergos.CMS.Application.Elements;

// ── Experience ViewModels ─────────────────────────────────────────────────────
// Rich interactive experiences served as CDN Web Components.
// Items (steps, articles, media) are populated by the CMS mapper from
// Umbraco Block Lists — the ViewModel carries only the section-level metadata.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class FeatureJourneyViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/FeatureJourney";
    public override string BlockClass => "sg-element--exp-feature-journey";
    public override string Alias      => "experienceFeatureJourney";

    /// <summary>Title and intro copy for the journey section.</summary>
    public ContentTextModel?       Text       { get; init; }
    /// <summary>Items (journey steps) resolved from Block List by the mapper.</summary>
    public ContentCollectionModel? Collection { get; init; }
}

public sealed class InsightExplorerViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/InsightExplorer";
    public override string BlockClass => "sg-element--exp-insight-explorer";
    public override string Alias      => "experienceInsightExplorer";

    /// <summary>Section heading and supporting copy.</summary>
    public ContentTextModel?       Text       { get; init; }
    /// <summary>Items (insight articles) resolved from Block List by the mapper.</summary>
    public ContentCollectionModel? Collection { get; init; }
}

public sealed class MediaExplorerViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/MediaExplorer";
    public override string BlockClass => "sg-element--exp-media-explorer";
    public override string Alias      => "experienceMediaExplorer";

    /// <summary>Section heading.</summary>
    public ContentTextModel?       Text       { get; init; }
    /// <summary>Items (media entries) resolved from Block List by the mapper.</summary>
    public ContentCollectionModel? Collection { get; init; }
    /// <summary>Default category filter applied on initial load.</summary>
    public ContentMetadataModel?   Metadata   { get; init; }
}

public sealed class ContentCarouselViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/ContentCarousel";
    public override string BlockClass => "sg-element--exp-content-carousel";
    public override string Alias      => "experienceContentCarousel";

    public ContentTextModel?       Text       { get; init; }
    public ContentCollectionModel? Collection { get; init; }
    public ContentMetadataModel?   Metadata   { get; init; }
}

public sealed class QuizFlowViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/QuizFlow";
    public override string BlockClass => "sg-element--exp-quiz-flow";
    public override string Alias      => "experienceQuizFlow";

    public ContentTextModel?       Text       { get; init; }
    public ContentCollectionModel? Collection { get; init; }
}

public sealed class FilterBoardViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/FilterBoard";
    public override string BlockClass => "sg-element--exp-filter-board";
    public override string Alias      => "experienceFilterBoard";

    public ContentTextModel?       Text       { get; init; }
    public ContentCollectionModel? Collection { get; init; }
    public ContentMetadataModel?   Metadata   { get; init; }
}

public sealed class RatingWidgetViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/RatingWidget";
    public override string BlockClass => "sg-element--exp-rating-widget";
    public override string Alias      => "experienceRatingWidget";

    public ContentTextModel?     Text     { get; init; }
    public ContentMetadataModel? Metadata { get; init; }
}

public sealed class CountdownClockViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/CountdownClock";
    public override string BlockClass => "sg-element--exp-countdown-clock";
    public override string Alias      => "experienceCountdownClock";

    public ContentTextModel?     Text     { get; init; }
    public ContentMetadataModel? Metadata { get; init; }
}

public sealed class NotificationStackViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/NotificationStack";
    public override string BlockClass => "sg-element--exp-notification-stack";
    public override string Alias      => "experienceNotificationStack";

    public ContentTextModel?       Text       { get; init; }
    public ContentCollectionModel? Collection { get; init; }
}
