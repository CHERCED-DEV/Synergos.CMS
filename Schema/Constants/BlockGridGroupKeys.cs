namespace Synergos.CMS.Schema.Constants;

/// <summary>
/// Stable GUIDs identifying the named groups (categories) that the Block Grid
/// editor uses to organise blocks visually in the backoffice picker. Each
/// element type registered as a Block Grid block is tagged with one of these
/// GUIDs via <c>BlockGridConfiguration.GroupKey</c>.
///
/// Picker UI shows blocks grouped by these keys with the matching colour and
/// label set in <see cref="Initializers.DocumentTypeInitializer"/>.
///
/// GUID range reservation: <c>b9000***</c> — Block Grid groups.
/// </summary>
public static class BlockGridGroupKeys
{
    /// <summary>Layout & structure (Section, Container, Grid, Column, Stack, Spacer, Divider, presets).</summary>
    public static readonly Guid Layout       = new("b9000001-0000-0000-0000-000000000000");

    /// <summary>Text blocks (Heading, Paragraph, RichText, Eyebrow, Quote, Label, TextBlock, CodeBlock).</summary>
    public static readonly Guid Text         = new("b9000002-0000-0000-0000-000000000000");

    /// <summary>Action blocks (Button, Link, CtaGroup, ButtonGroup).</summary>
    public static readonly Guid Action       = new("b9000003-0000-0000-0000-000000000000");

    /// <summary>Media blocks (Image, Video, Icon, Gallery, LogoItem, Avatar).</summary>
    public static readonly Guid Media        = new("b9000004-0000-0000-0000-000000000000");

    /// <summary>Informational blocks (Badge, Stat, KeyValue, Timeline, FeatureItem, FaqItem, TestimonialItem, PricingCard).</summary>
    public static readonly Guid Info         = new("b9000005-0000-0000-0000-000000000000");

    /// <summary>Component blocks (Card, Hero, Banner, FeatureGrid, FaqList, TestimonialList, LogoCloud, InfoBlock, MediaText, Accordion).</summary>
    public static readonly Guid Components   = new("b9000006-0000-0000-0000-000000000000");

    /// <summary>Integration blocks (ScriptEmbed, IframeEmbed, ExternalWidget, MacroHost).</summary>
    public static readonly Guid Integration  = new("b9000007-0000-0000-0000-000000000000");

    /// <summary>Corporate blocks (TabGroup, AlertBar, AlertBox, BannerSlider, Newsletter, SocialShare, DataTable, ContactInfo, MapEmbed, MissionBlock).</summary>
    public static readonly Guid Corporate    = new("b9000008-0000-0000-0000-000000000000");

    /// <summary>Blog blocks (BlogHighlight, ArticleList).</summary>
    public static readonly Guid Blog         = new("b9000009-0000-0000-0000-000000000000");

    /// <summary>Experience blocks (FeatureJourney, InsightExplorer, MediaExplorer, ContentCarousel, QuizFlow, FilterBoard, RatingWidget, CountdownClock, NotificationStack).</summary>
    public static readonly Guid Experiences  = new("b900000a-0000-0000-0000-000000000000");
}
