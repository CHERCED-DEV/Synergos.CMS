namespace Synergos.CMS.Schema.Constants;

/// <summary>
/// Stable GUIDs for all Synergos Data Types.
/// Never change these after first deployment — they are the schema identity.
/// </summary>
public static class DataTypeKeys
{
    // ─── Text ──────────────────────────────────────────────────────────────
    public static readonly Guid TextIdentifier = new("a1000005-0000-0000-0000-000000000000");
    public static readonly Guid TextMetaDesc   = new("a1000004-0000-0000-0000-000000000000");
    public static readonly Guid TextAuthor     = new("a1000010-0000-0000-0000-000000000000");

    // ─── Toggle ────────────────────────────────────────────────────────────
    public static readonly Guid ToggleBoolean  = new("a1000011-0000-0000-0000-000000000000");

    // ─── DateTime ──────────────────────────────────────────────────────────
    public static readonly Guid DateTimePicker = new("a1000015-0000-0000-0000-000000000000");

    // ─── Core Dropdowns ────────────────────────────────────────────────────
    public static readonly Guid SelectLifecycleStatus = new("a1000017-0000-0000-0000-000000000000");
    public static readonly Guid SelectOwnerRole       = new("a1000018-0000-0000-0000-000000000000");
    public static readonly Guid SelectSiteScope       = new("a1000019-0000-0000-0000-000000000000");
    public static readonly Guid SelectAccessLevel     = new("a1000020-0000-0000-0000-000000000000");

    // ─── Content ───────────────────────────────────────────────────────────
    public static readonly Guid SelectHeadingLevel  = new("a1000021-0000-0000-0000-000000000000");
    public static readonly Guid TextTitle           = new("a1000022-0000-0000-0000-000000000000");
    public static readonly Guid TextSubtitle        = new("a1000023-0000-0000-0000-000000000000");
    public static readonly Guid RichTextBody        = new("a1000024-0000-0000-0000-000000000000");
    public static readonly Guid TextSummary         = new("a1000025-0000-0000-0000-000000000000");
    public static readonly Guid TextCaption         = new("a1000026-0000-0000-0000-000000000000");
    public static readonly Guid MediaImage          = new("a1000027-0000-0000-0000-000000000000");
    public static readonly Guid LinkUrl             = new("a1000028-0000-0000-0000-000000000000");
    public static readonly Guid SelectLinkTarget    = new("a1000029-0000-0000-0000-000000000000");
    public static readonly Guid SelectBadgeType     = new("a1000030-0000-0000-0000-000000000000");
    public static readonly Guid TagsContent         = new("a1000031-0000-0000-0000-000000000000");
    public static readonly Guid TextUrl             = new("a1000032-0000-0000-0000-000000000000");
    public static readonly Guid SelectEmbedType     = new("a1000033-0000-0000-0000-000000000000");
    public static readonly Guid BlockListCollection = new("a1000034-0000-0000-0000-000000000000");

    // ─── DOM Dropdowns ─────────────────────────────────────────────────────
    public static readonly Guid SelectContainerType   = new("a1000035-0000-0000-0000-000000000000");
    public static readonly Guid SelectAlignmentLayout = new("a1000036-0000-0000-0000-000000000000");
    public static readonly Guid SelectDirection       = new("a1000037-0000-0000-0000-000000000000");
    public static readonly Guid SelectVariant         = new("a1000038-0000-0000-0000-000000000000");
    public static readonly Guid SelectTheme           = new("a1000039-0000-0000-0000-000000000000");

    // ─── Behavior Dropdowns ────────────────────────────────────────────────
    public static readonly Guid SelectInteractionType   = new("a1000040-0000-0000-0000-000000000000");
    public static readonly Guid SelectInteractionAction = new("a1000041-0000-0000-0000-000000000000");
    public static readonly Guid SelectNavigationType    = new("a1000042-0000-0000-0000-000000000000");
    public static readonly Guid SelectHttpMethod        = new("a1000043-0000-0000-0000-000000000000");
    public static readonly Guid SelectScriptType        = new("a1000044-0000-0000-0000-000000000000");

    // ─── DOM / Behavior Text ───────────────────────────────────────────────
    public static readonly Guid TextClassList      = new("a1000045-0000-0000-0000-000000000000");
    public static readonly Guid TextSpacingToken   = new("a1000046-0000-0000-0000-000000000000");
    public static readonly Guid TextDataAttributes = new("a1000047-0000-0000-0000-000000000000");
    public static readonly Guid TextScriptContent  = new("a1000048-0000-0000-0000-000000000000");

    // ─── SEO / Integration Dropdowns ──────────────────────────────────────
    public static readonly Guid SelectRobots              = new("a1000049-0000-0000-0000-000000000000");
    public static readonly Guid SelectIntegrationProvider = new("a1000050-0000-0000-0000-000000000000");

    // ─── Atomic Element Dropdowns ──────────────────────────────────────────
    public static readonly Guid SelectCtaVariant  = new("a1000051-0000-0000-0000-000000000000");
    public static readonly Guid SelectWidthPreset = new("a1000052-0000-0000-0000-000000000000");
    public static readonly Guid SelectMediaRatio  = new("a1000053-0000-0000-0000-000000000000");
    public static readonly Guid NumberInteger     = new("a1000054-0000-0000-0000-000000000000");

    // ─── Spacing Dropdowns ─────────────────────────────────────────────────
    public static readonly Guid SelectSpacingMargin  = new("a1000055-0000-0000-0000-000000000000");
    public static readonly Guid SelectSpacingPadding = new("a1000056-0000-0000-0000-000000000000");
    public static readonly Guid SelectSpacingGap     = new("a1000057-0000-0000-0000-000000000000");

    // ─── Block Grid — Page Sections ────────────────────────────────────────
    public static readonly Guid BlockGridPageSections = new("a2000002-0000-0000-0000-000000000000");

    // ─── Platform / Settings Data Types ────────────────────────────────────
    public static readonly Guid ContentPicker      = new("a1000058-0000-0000-0000-000000000000");
    public static readonly Guid MultiContentPicker = new("a1000059-0000-0000-0000-000000000000");
    public static readonly Guid SelectButtonStyle  = new("a1000060-0000-0000-0000-000000000000");
    public static readonly Guid SelectCardStyle    = new("a1000061-0000-0000-0000-000000000000");
    public static readonly Guid SelectHeaderStyle  = new("a1000062-0000-0000-0000-000000000000");
    public static readonly Guid SelectListLayout   = new("a1000063-0000-0000-0000-000000000000");
    public static readonly Guid SelectFieldType    = new("a1000064-0000-0000-0000-000000000000");
    public static readonly Guid SelectFieldWidth   = new("a1000065-0000-0000-0000-000000000000");
    public static readonly Guid TextHexColor       = new("a1000066-0000-0000-0000-000000000000");
    public static readonly Guid SelectPageLayout   = new("a1000067-0000-0000-0000-000000000000");
    public static readonly Guid TextAreaNotes      = new("a1000068-0000-0000-0000-000000000000");
    public static readonly Guid PageTagsPicker     = new("a1000069-0000-0000-0000-000000000000");

    // ─── Block Lists ────────────────────────────────────────────────────────
    public static readonly Guid BlockListNavItems   = new("a2000003-0000-0000-0000-000000000000");
    public static readonly Guid BlockListFormFields = new("a2000004-0000-0000-0000-000000000000");
    public static readonly Guid BlockGridReusable   = new("a2000005-0000-0000-0000-000000000000");

    // ─── Site Culture ──────────────────────────────────────────────────────
    public static readonly Guid SelectSiteCulture = new("a1000070-0000-0000-0000-000000000000");

    // ─── Angular / MF Mount ────────────────────────────────────────────────
    public static readonly Guid SelectAngularElement = new("a1000071-0000-0000-0000-000000000000");
    public static readonly Guid BlockListMountParams = new("a2000006-0000-0000-0000-000000000000");

    // ─── Macro Host ────────────────────────────────────────────────────────
    public static readonly Guid SelectMacroAlias = new("a1000072-0000-0000-0000-000000000000");

    // ─── Alert Bar ─────────────────────────────────────────────────────────
    public static readonly Guid SelectAlertVariant = new("a1000074-0000-0000-0000-000000000000");

    // ─── Multi-Identity ────────────────────────────────────────────────────
    public static readonly Guid SelectIdentityPreset = new("a1000075-0000-0000-0000-000000000000");

    // ─── Flow Engine ───────────────────────────────────────────────────────
    public static readonly Guid SelectFlowExecutionMode = new("fe000073-0000-0000-0000-000000000000");
    public static readonly Guid TextAreaJson            = new("fe000074-0000-0000-0000-000000000000");

    // ─── Blog ──────────────────────────────────────────────────────────────
    /// <summary>
    /// Dropdown of editorial post types — article, news, tutorial, caseStudy,
    /// interview, opinion, release. Values are authored by
    /// <see cref="Domain.Shared.BlogPostTypeExtensions.ToAlias"/> so adding a
    /// type requires updating the enum + extensions only.
    /// </summary>
    public static readonly Guid SelectBlogPostType      = new("a1000076-0000-0000-0000-000000000000");

    // Reservados pero no registrados — CardGrid y LogoCloudGrid migraron a
    // Block Grid Areas (patrón area-based idiomático Umbraco 13).
    // public static readonly Guid BlockListCardItems = new("a1000077-…");
    // public static readonly Guid BlockListLogoItems = new("a1000078-…");

    /// <summary>BlockList tipado para TestimonialCarousel — solo elementInfoTestimonialItem.</summary>
    public static readonly Guid BlockListTestimonialItems = new("a1000079-0000-0000-0000-000000000000");
    /// <summary>BlockList tipado para AccordionGroup — solo elementInfoFaqItem.</summary>
    public static readonly Guid BlockListFaqItems        = new("a1000080-0000-0000-0000-000000000000");
}
