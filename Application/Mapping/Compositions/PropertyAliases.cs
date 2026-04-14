namespace Synergos.CMS.Application.Mapping.Compositions;

/// <summary>
/// Compile-time constants for Umbraco composition property aliases.
/// Grouped by composition name — one nested class per composition.
/// </summary>
public static class PropertyAliases
{
    // ── compContentText ───────────────────────────────────────────────────
    public static class Text
    {
        public const string Title    = "title";
        public const string Subtitle = "subtitle";
        public const string Body     = "body";
        public const string Summary  = "summary";
        public const string Caption  = "caption";
    }

    // ── compContentBadge ──────────────────────────────────────────────────
    public static class Badge
    {
        public const string BadgeText = "badgeText";
        public const string BadgeType = "badgeType";
    }

    // ── compContentCollection ─────────────────────────────────────────────
    public static class Collection
    {
        public const string CollectionTitle       = "collectionTitle";
        public const string CollectionDescription = "collectionDescription";
        public const string Items                 = "items";
    }

    // ── compContentCta ────────────────────────────────────────────────────
    public static class Cta
    {
        public const string CtaLink     = "ctaLink";
        public const string CtaLabel    = "ctaLabel";
        public const string CtaTarget   = "ctaTarget";
        public const string CtaAriaLabel = "ctaAriaLabel";
    }

    // ── compContentDate ───────────────────────────────────────────────────
    public static class Date
    {
        public const string PublishDate = "publishDate";
        public const string UpdateDate  = "updateDate";
    }

    // ── compContentEmbed ──────────────────────────────────────────────────
    public static class Embed
    {
        public const string EmbedUrl   = "embedUrl";
        public const string EmbedType  = "embedType";
        public const string EmbedTitle = "embedTitle";
    }

    // ── compContentHeading ────────────────────────────────────────────────
    public static class Heading
    {
        public const string HeadingText        = "headingText";
        public const string HeadingLevel       = "headingLevel";
        public const string HeadingTagOverride = "headingTagOverride";
    }

    // ── compContentMedia ──────────────────────────────────────────────────
    public static class Media
    {
        public const string Picker           = "media";
        public const string AltText          = "altText";
        public const string MediaTitle       = "mediaTitle";
        public const string MediaDescription = "mediaDescription";
    }

    // ── compContentAuthor ─────────────────────────────────────────────────
    public static class Author
    {
        public const string AuthorImage = "authorImage";
        public const string AuthorName  = "authorName";
        public const string AuthorRole  = "authorRole";
    }

    // ── compContentMetadata ───────────────────────────────────────────────
    public static class Metadata
    {
        public const string Tags     = "tags";
        public const string Category = "category";
        public const string Keywords = "keywords";
    }

    // ── compDomClass ──────────────────────────────────────────────────────
    public static class DomClass
    {
        public const string ClassList = "classList";
    }

    // ── compDomVariant ────────────────────────────────────────────────────
    public static class DomVariant
    {
        public const string Variant = "variant";
        public const string Theme   = "theme";
    }

    // ── compDomLayout ─────────────────────────────────────────────────────
    public static class DomLayout
    {
        public const string ContainerType = "containerType";
        public const string Alignment     = "alignment";
        public const string Direction     = "direction";
    }

    // ── compDomSpacing ────────────────────────────────────────────────────
    public static class DomSpacing
    {
        public const string Margin  = "margin";
        public const string Padding = "padding";
        public const string Gap     = "gap";
    }

    // ── compDomAttributes ────────────────────────────────────────────────
    public static class DomAttributes
    {
        public const string ElementId      = "elementId";
        public const string DataAttributes = "dataAttributes";
        public const string AriaLabel      = "ariaLabel";
        public const string Role           = "role";
    }

    // ── compDomVisibility ────────────────────────────────────────────────
    public static class DomVisibility
    {
        public const string HideOnMobile  = "hideOnMobile";
        public const string HideOnDesktop = "hideOnDesktop";
    }

    // ── compBehaviorInteraction ───────────────────────────────────────────
    public static class Interaction
    {
        public const string InteractionType   = "interactionType";
        public const string InteractionAction = "interactionAction";
    }

    // ── compBehaviorNavigation ────────────────────────────────────────────
    public static class Navigation
    {
        public const string NavigateTo    = "navigateTo";
        public const string NavigationType = "navigationType";
    }

    // ── compBehaviorScript ────────────────────────────────────────────────
    public static class Script
    {
        public const string ScriptType    = "scriptType";
        public const string ScriptContent = "scriptContent";
    }

    // ── compBehaviorAsync ─────────────────────────────────────────────────
    public static class Async
    {
        public const string ApiEndpoint = "apiEndpoint";
        public const string Method      = "method";
    }

    // ── compBehaviorTracking ──────────────────────────────────────────────
    public static class Tracking
    {
        public const string EventName     = "eventName";
        public const string EventCategory = "eventCategory";
        public const string EventLabel    = "eventLabel";
    }

    // ── compBehaviorFeatureFlag ───────────────────────────────────────────
    public static class FeatureFlag
    {
        public const string FeatureKey = "featureKey";
        public const string IsEnabled  = "isEnabled";
    }

    // ── compIntegration ───────────────────────────────────────────────────
    public static class Integration
    {
        public const string IntegrationProvider = "integrationProvider";
        public const string IntegrationId       = "integrationId";
        public const string ApiKey              = "apiKey";
        public const string WebhookUrl          = "webhookUrl";
        public const string ApiEndpoint         = "apiEndpoint";
    }

    // ── compMount (Angular/MF host) ───────────────────────────────────────
    public static class Mount
    {
        public const string AngularElement = "angularElement";
        public const string RemoteEntry    = "remoteEntry";
        public const string ExposedModule  = "exposedModule";
        public const string MountParams    = "mountParams";
        public const string ParamKey       = "paramKey";
        public const string ParamValue     = "paramValue";
    }

    // ── compVisibility (scheduling + conditional show/hide) ───────────────
    public static class Visibility
    {
        public const string IsHidden            = "isHidden";
        public const string VisibilityStart     = "visibilityStart";
        public const string VisibilityEnd       = "visibilityEnd";
        public const string VisibilityCondition = "visibilityCondition";
    }

    // ── compSeo ───────────────────────────────────────────────────────────
    public static class Seo
    {
        public const string SeoTitle       = "seoTitle";
        public const string SeoDescription = "seoDescription";
        public const string OgTitle        = "ogTitle";
        public const string OgDescription  = "ogDescription";
        public const string OgImage        = "ogImage";
        public const string Canonical      = "canonical";
        public const string Robots         = "robots";

        // Used on referenced media items (Image media type).
        public const string AltText        = "altText";
    }
}
