using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.PropertyEditors;
using Umbraco.Cms.Core.Serialization;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;
using Synergos.CMS.Schema.Constants;

namespace Synergos.CMS.Schema.Initializers;

/// <summary>
/// Phase 8 — Crea el Block Grid de secciones y los Document Types contenedor.
///
/// v9.2.1 — Block Grid configurado con:
///   • 9 grupos (Layout, Text, Action, Media, Info, Components, Integration, Corporate, Blog)
///   • Custom views por grupo: 5 estructurales (color) + 7 de contenido (por grupo)
///   • Áreas en bloques estructurales (Section, Grid, Column, Container, Stack)
///   • ColumnSpanOptions para redimensionamiento
///   • AllowAtRoot / AllowInAreas para control de jerarquía
///   • 58 Element Types en 8 familias (c3-ca + cc)
///   • 2 Document Types activos: SiteRoot + PageBase
///   • Tipos retirados (homePage, aboutPage, servicesPage, contactPage, landingPage): eliminados por CleanupLegacyTypes()
/// </summary>
internal sealed class DocumentTypeInitializer : SchemaInitializerBase
{
    private const string LayoutComposerStylesheet = "/App_Plugins/LayoutComposer/styles/layout-composer.css";

    private readonly IFileService                     _fileService;
    private readonly PropertyEditorCollection         _propertyEditors;
    private readonly IConfigurationEditorJsonSerializer _serializer;
    private readonly string                           _viewsPath;

    public DocumentTypeInitializer(
        IContentTypeService contentTypeService,
        IDataTypeService dataTypeService,
        IFileService fileService,
        IShortStringHelper shortStringHelper,
        PropertyEditorCollection propertyEditors,
        IConfigurationEditorJsonSerializer serializer,
        string contentRootPath)
        : base(contentTypeService, dataTypeService, shortStringHelper)
    {
        _fileService     = fileService;
        _propertyEditors = propertyEditors;
        _serializer      = serializer;
        _viewsPath       = Path.Combine(contentRootPath, "Views");
    }

    public override void Initialize()
    {
        // 0. Cleanup — eliminar tipos de arquitecturas anteriores
        CleanupLegacyTypes();
        var pagesFolderId = EnsureRootFolder("Pages");

        // 1. Block Grid DataType con los 50 Element Types + Groups + Areas
        EnsureBlockGridPageSections();

        // 2. Document Types (only universal types — business page types retired)
        EnsureSiteRoot(pagesFolderId);
        EnsurePageBase(pagesFolderId);

        // Blog types are owned exclusively by BlogInitializer (Phase 8c).

        // 3. Shop types (extracted to ShopInitializer — see ShopInitializer.cs)
        new ShopInitializer(Cts, Dts, _fileService, Ssh, _viewsPath, pagesFolderId).Initialize();

        // 4. Allowed children
        PatchAllowedChildren();
    }

    // ── Cleanup ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Set canónico de GUIDs — todo Element Type en la BD que NO esté aquí será eliminado.
    /// </summary>
    private static readonly HashSet<Guid> CanonicalElementKeys = new(new[]
    {
        // Structural (c3*)
        ContentTypeKeys.ElementStructSection,   ContentTypeKeys.ElementStructContainer,
        ContentTypeKeys.ElementStructGrid,      ContentTypeKeys.ElementStructColumn,
        ContentTypeKeys.ElementStructStack,     ContentTypeKeys.ElementStructDivider,
        ContentTypeKeys.ElementStructSpacer,
        // Textual (c4*)
        ContentTypeKeys.ElementTextHeading,     ContentTypeKeys.ElementTextParagraph,
        ContentTypeKeys.ElementTextRichText,    ContentTypeKeys.ElementTextEyebrow,
        ContentTypeKeys.ElementTextQuote,       ContentTypeKeys.ElementTextLabel,
        // Action (c5*)
        ContentTypeKeys.ElementActionButton,    ContentTypeKeys.ElementActionLink,
        ContentTypeKeys.ElementActionCtaGroup,
        // Media (c6*)
        ContentTypeKeys.ElementMediaImage,      ContentTypeKeys.ElementMediaVideo,
        ContentTypeKeys.ElementMediaIcon,       ContentTypeKeys.ElementMediaGalleryItem,
        ContentTypeKeys.ElementMediaLogoItem,
        // Informational (c7*)
        ContentTypeKeys.ElementInfoBadge,       ContentTypeKeys.ElementInfoStat,
        ContentTypeKeys.ElementInfoFeature,     ContentTypeKeys.ElementInfoKeyValue,
        ContentTypeKeys.ElementInfoTimelineItem, ContentTypeKeys.ElementInfoFaqItem,
        ContentTypeKeys.ElementInfoTestimonialItem,
        // Composition (c8*)
        ContentTypeKeys.ElementCompCard,        ContentTypeKeys.ElementCompHero,
        ContentTypeKeys.ElementCompFeatureGrid, ContentTypeKeys.ElementCompCtaBanner,
        ContentTypeKeys.ElementCompFaqList,     ContentTypeKeys.ElementCompTestimonialList,
        ContentTypeKeys.ElementCompLogoCloud,   ContentTypeKeys.ElementCompMediaTextSplit,
        // Integration (c9*)
        ContentTypeKeys.ElementIntScriptEmbed,  ContentTypeKeys.ElementIntIframeEmbed,
        ContentTypeKeys.ElementIntExternalWidget, ContentTypeKeys.ElementIntAngularHost,
        ContentTypeKeys.ElementIntMfHost,
        // Corporate (ca*)
        ContentTypeKeys.ElementCorpTabGroup,    ContentTypeKeys.ElementCorpAlertBar,
        ContentTypeKeys.ElementCorpBannerSlider, ContentTypeKeys.ElementCorpNewsletterForm,
        ContentTypeKeys.ElementCorpSocialShare, ContentTypeKeys.ElementCorpDataTable,
        ContentTypeKeys.ElementCorpContactInfo, ContentTypeKeys.ElementCorpMapEmbed,
        ContentTypeKeys.ElementCorpMissionBlock,
        // Additions — UI-only elements that need CMS backing
        ContentTypeKeys.ElementMediaAvatar,
        ContentTypeKeys.ElementInfoPricingCard,
        ContentTypeKeys.ElementCompAccordion,
        // Phase 3 — Forms
        ContentTypeKeys.ElementCompFormBlock,
        ContentTypeKeys.ElementFormEmbed,
        // Phase 5 — Blog blocks
        ContentTypeKeys.ElementCompBlogHighlight,
        ContentTypeKeys.ElementCompArticleList,
        // Layout Presets (c3001*)
        ContentTypeKeys.LayoutPreset1Col,
        ContentTypeKeys.LayoutPreset2ColEqual,
        ContentTypeKeys.LayoutPreset3ColEqual,
        ContentTypeKeys.LayoutPreset4ColEqual,
        ContentTypeKeys.LayoutPresetMainSidebar,
        // Experiences (ce*) — must be here or CleanupLegacyTypes will delete them
        ContentTypeKeys.ElementExpFeatureJourney,    ContentTypeKeys.ElementExpInsightExplorer,
        ContentTypeKeys.ElementExpMediaExplorer,     ContentTypeKeys.ElementExpContentCarousel,
        ContentTypeKeys.ElementExpQuizFlow,          ContentTypeKeys.ElementExpFilterBoard,
        ContentTypeKeys.ElementExpRatingWidget,      ContentTypeKeys.ElementExpCountdownClock,
        ContentTypeKeys.ElementExpNotificationStack,
        // Shop (cf*) — e-commerce element types
        ContentTypeKeys.ElementShopProductCard,      ContentTypeKeys.ElementShopProductGrid,
        ContentTypeKeys.ElementShopProductDetail,    ContentTypeKeys.ElementShopCartSummary,
        ContentTypeKeys.ElementShopCartItem,         ContentTypeKeys.ElementShopPriceDisplay,
        ContentTypeKeys.ElementShopQuantitySelector, ContentTypeKeys.ElementShopVariantPicker,
    });

    private void CleanupLegacyTypes()
    {
        // 1. Purge ALL Element Types in the DB that are NOT in the canonical set.
        //    This catches old c1*/c2* types, renamed types, and any other orphans.
        var allTypes = Cts.GetAll()
            .Where(ct => ct.IsElement)
            .ToList();

        foreach (var ct in allTypes)
        {
            // Skip compositions (b*) — those are managed by their own initializers
            if (CanonicalElementKeys.Contains(ct.Key)) continue;
            if (ct.Alias.StartsWith("comp", StringComparison.OrdinalIgnoreCase)) continue;

            Cts.Delete(ct);
        }

        // 2. Old Document Types (d1* + milestone-1 orphans + retired d2* business pages)
        string[] oldDocAliases = [
            // milestone-1 camelCase variants
            "pageLanding", "pageArticle",
            "pageHome", "pageProduct", "pageService", "pageContact", "pageQa", "pageProfile",
            // retired d2* business pages (superseded by pageBase)
            "homePage", "aboutPage", "servicesPage", "contactPage", "landingPage",
            // retired d2* blog pages (superseded by d4* blogHome / blogPost)
            "blogHomePage", "blogPostPage",
        ];
        foreach (var alias in oldDocAliases)
        {
            var ct = Cts.Get(alias);
            if (ct is not null) Cts.Delete(ct);
        }

    }

    // ── Block Grid DataType ────────────────────────────────────────────────────
    //
    // Stable GUIDs for Groups (bg*) — never change after first deployment.
    //
    private static readonly Guid GroupLayoutKey      = new("b9000001-0000-0000-0000-000000000000");
    private static readonly Guid GroupTextKey        = new("b9000002-0000-0000-0000-000000000000");
    private static readonly Guid GroupActionKey      = new("b9000003-0000-0000-0000-000000000000");
    private static readonly Guid GroupMediaKey       = new("b9000004-0000-0000-0000-000000000000");
    private static readonly Guid GroupInfoKey        = new("b9000005-0000-0000-0000-000000000000");
    private static readonly Guid GroupComponentsKey  = new("b9000006-0000-0000-0000-000000000000");
    private static readonly Guid GroupIntegrationKey  = new("b9000007-0000-0000-0000-000000000000");
    private static readonly Guid GroupCorporateKey    = new("b9000008-0000-0000-0000-000000000000");
    private static readonly Guid GroupBlogKey         = new("b9000009-0000-0000-0000-000000000000");
    private static readonly Guid GroupExperiencesKey  = new("b900000a-0000-0000-0000-000000000000");

    //
    // Stable GUIDs for Areas (fa*) — internal to Block Grid configuration.
    //
    private static readonly Guid AreaSectionContent  = new("fa000001-0000-0000-0000-000000000000");
    private static readonly Guid AreaGridColumns     = new("fa000002-0000-0000-0000-000000000000");
    private static readonly Guid AreaColumnContent   = new("fa000003-0000-0000-0000-000000000000");
    private static readonly Guid AreaContainerContent = new("fa000004-0000-0000-0000-000000000000");
    private static readonly Guid AreaStackContent    = new("fa000005-0000-0000-0000-000000000000");

    // Layout Preset areas centralised in Schema/Constants/BlockGridAreaKeys.cs.
    // Keep aliases here for readability of the block configuration block below.
    private static readonly Guid AreaPreset1ColMain     = BlockGridAreaKeys.Preset1ColMain;
    private static readonly Guid AreaPreset2ColLeft     = BlockGridAreaKeys.Preset2ColLeft;
    private static readonly Guid AreaPreset2ColRight    = BlockGridAreaKeys.Preset2ColRight;
    private static readonly Guid AreaPreset3Col1        = BlockGridAreaKeys.Preset3Col1;
    private static readonly Guid AreaPreset3Col2        = BlockGridAreaKeys.Preset3Col2;
    private static readonly Guid AreaPreset3Col3        = BlockGridAreaKeys.Preset3Col3;
    private static readonly Guid AreaPreset4Col1        = BlockGridAreaKeys.Preset4Col1;
    private static readonly Guid AreaPreset4Col2        = BlockGridAreaKeys.Preset4Col2;
    private static readonly Guid AreaPreset4Col3        = BlockGridAreaKeys.Preset4Col3;
    private static readonly Guid AreaPreset4Col4        = BlockGridAreaKeys.Preset4Col4;
    private static readonly Guid AreaPresetMainContent  = BlockGridAreaKeys.PresetMainContent;
    private static readonly Guid AreaPresetSidebar      = BlockGridAreaKeys.PresetSidebar;

    private void EnsureBlockGridPageSections()
    {
        if (!_propertyEditors.TryGet(Umbraco.Cms.Core.Constants.PropertyEditors.Aliases.BlockGrid, out var editor))
            return;

        // ── Groups ─────────────────────────────────────────────────────────
        var groups = new BlockGridConfiguration.BlockGridGroupConfiguration[]
        {
            new() { Key = GroupLayoutKey,      Name = "Layout" },
            new() { Key = GroupTextKey,        Name = "Text" },
            new() { Key = GroupActionKey,      Name = "Action" },
            new() { Key = GroupMediaKey,       Name = "Media" },
            new() { Key = GroupInfoKey,        Name = "Info" },
            new() { Key = GroupComponentsKey,  Name = "Components" },
            new() { Key = GroupIntegrationKey,  Name = "Integration" },
            new() { Key = GroupCorporateKey,    Name = "Corporate" },
            new() { Key = GroupBlogKey,         Name = "Blog" },
            new() { Key = GroupExperiencesKey,  Name = "Experiences" },
        };

        // ── Column Span presets ─────────────────────────────────────────────
        var spanFull    = new[] { ColSpan(12) };
        var spanContent = new[] { ColSpan(3), ColSpan(4), ColSpan(6), ColSpan(8), ColSpan(12) };
        var spanAll     = Enumerable.Range(1, 12).Select(ColSpan).ToArray();

        // ── Block configurations ────────────────────────────────────────────
        var blocks = new List<BlockGridConfiguration.BlockGridBlockConfiguration>();

        // ── Structural (c3*) — containers with Areas ────────────────────────

        // Section — top-level container, no nesting of sections
        blocks.Add(LayoutBlock(ContentTypeKeys.ElementStructSection, allowAtRoot: true, allowInAreas: false,
            columnSpans: spanFull, areaGridColumns: 12,
            areas: new[]
            {
                Area(AreaSectionContent, "sectionContent", columnSpan: 12)
            },
            view: "~/App_Plugins/LayoutComposer/views/block-section.html"));

        // Container — generic wrapper, can go anywhere (always full-width in its slot)
        blocks.Add(LayoutBlock(ContentTypeKeys.ElementStructContainer, allowAtRoot: true, allowInAreas: true,
            columnSpans: spanFull, areaGridColumns: 12,
            areas: new[]
            {
                Area(AreaContainerContent, "containerContent", columnSpan: 12)
            },
            view: "~/App_Plugins/LayoutComposer/views/block-container.html"));

        // Grid — container specifically for columns
        blocks.Add(LayoutBlock(ContentTypeKeys.ElementStructGrid, allowAtRoot: true, allowInAreas: true,
            columnSpans: spanFull, areaGridColumns: 12,
            areas: new[]
            {
                Area(AreaGridColumns, "gridColumns", columnSpan: 12,
                    specifiedAllowance: new[]
                    {
                        new BlockGridConfiguration.BlockGridAreaConfigurationSpecifiedAllowance
                        {
                            ElementTypeKey = ContentTypeKeys.ElementStructColumn
                        }
                    })
            },
            view: "~/App_Plugins/LayoutComposer/views/block-grid.html"));

        // Column — only inside Grid, variable width
        blocks.Add(LayoutBlock(ContentTypeKeys.ElementStructColumn, allowAtRoot: false, allowInAreas: true,
            columnSpans: spanAll, areaGridColumns: 12,
            areas: new[]
            {
                Area(AreaColumnContent, "columnContent", columnSpan: 12)
            },
            view: "~/App_Plugins/LayoutComposer/views/block-column.html"));

        // Stack — vertical stack container (always full-width in its slot)
        blocks.Add(LayoutBlock(ContentTypeKeys.ElementStructStack, allowAtRoot: true, allowInAreas: true,
            columnSpans: spanFull, areaGridColumns: 12,
            areas: new[]
            {
                Area(AreaStackContent, "stackContent", columnSpan: 12)
            },
            view: "~/App_Plugins/LayoutComposer/views/block-stack.html"));

        // ── Layout Presets (c3001*) — ready-to-use column configurations ─

        // 1 Column (12/12) — full width
        blocks.Add(LayoutBlock(ContentTypeKeys.LayoutPreset1Col, allowAtRoot: true, allowInAreas: true,
            columnSpans: spanFull, areaGridColumns: 12,
            areas: new[]
            {
                Area(AreaPreset1ColMain, "main", columnSpan: 12)
            },
            view: "~/App_Plugins/LayoutComposer/views/block-layout-1col.html"));

        // 2 Columns Equal (6/12 + 6/12)
        blocks.Add(LayoutBlock(ContentTypeKeys.LayoutPreset2ColEqual, allowAtRoot: true, allowInAreas: true,
            columnSpans: spanFull, areaGridColumns: 12,
            areas: new[]
            {
                Area(AreaPreset2ColLeft,  "left",  columnSpan: 6),
                Area(AreaPreset2ColRight, "right", columnSpan: 6)
            },
            view: "~/App_Plugins/LayoutComposer/views/block-layout-2col.html"));

        // 3 Columns Equal (4/12 + 4/12 + 4/12)
        blocks.Add(LayoutBlock(ContentTypeKeys.LayoutPreset3ColEqual, allowAtRoot: true, allowInAreas: true,
            columnSpans: spanFull, areaGridColumns: 12,
            areas: new[]
            {
                Area(AreaPreset3Col1, "col1", columnSpan: 4),
                Area(AreaPreset3Col2, "col2", columnSpan: 4),
                Area(AreaPreset3Col3, "col3", columnSpan: 4)
            },
            view: "~/App_Plugins/LayoutComposer/views/block-layout-3col.html"));

        // 4 Columns Equal (3/12 + 3/12 + 3/12 + 3/12)
        blocks.Add(LayoutBlock(ContentTypeKeys.LayoutPreset4ColEqual, allowAtRoot: true, allowInAreas: true,
            columnSpans: spanFull, areaGridColumns: 12,
            areas: new[]
            {
                Area(AreaPreset4Col1, "col1", columnSpan: 3),
                Area(AreaPreset4Col2, "col2", columnSpan: 3),
                Area(AreaPreset4Col3, "col3", columnSpan: 3),
                Area(AreaPreset4Col4, "col4", columnSpan: 3)
            },
            view: "~/App_Plugins/LayoutComposer/views/block-layout-4col.html"));

        // Main + Sidebar (8/12 + 4/12)
        blocks.Add(LayoutBlock(ContentTypeKeys.LayoutPresetMainSidebar, allowAtRoot: true, allowInAreas: true,
            columnSpans: spanFull, areaGridColumns: 12,
            areas: new[]
            {
                Area(AreaPresetMainContent, "main",    columnSpan: 8),
                Area(AreaPresetSidebar,     "sidebar", columnSpan: 4)
            },
            view: "~/App_Plugins/LayoutComposer/views/block-layout-main-sidebar.html"));

        // Divider — leaf, no areas
        blocks.Add(ContentBlock(ContentTypeKeys.ElementStructDivider, GroupLayoutKey, spanFull,
            view: "~/App_Plugins/LayoutComposer/views/block-separator.html"));

        // Spacer — leaf, no areas
        blocks.Add(ContentBlock(ContentTypeKeys.ElementStructSpacer, GroupLayoutKey, spanFull,
            view: "~/App_Plugins/LayoutComposer/views/block-separator.html"));

        // ── Textual (c4*) ──────────────────────────────────────────────────
        blocks.Add(ContentBlock(ContentTypeKeys.ElementTextHeading,   GroupTextKey, spanContent));
        blocks.Add(ContentBlock(ContentTypeKeys.ElementTextParagraph, GroupTextKey, spanContent));
        blocks.Add(ContentBlock(ContentTypeKeys.ElementTextRichText,  GroupTextKey, spanContent));
        blocks.Add(ContentBlock(ContentTypeKeys.ElementTextEyebrow,   GroupTextKey, spanContent));
        blocks.Add(ContentBlock(ContentTypeKeys.ElementTextQuote,     GroupTextKey, spanContent));
        blocks.Add(ContentBlock(ContentTypeKeys.ElementTextLabel,     GroupTextKey, spanContent));

        // ── Action (c5*) ───────────────────────────────────────────────────
        blocks.Add(ContentBlock(ContentTypeKeys.ElementActionButton,   GroupActionKey, spanContent));
        blocks.Add(ContentBlock(ContentTypeKeys.ElementActionLink,     GroupActionKey, spanContent));
        blocks.Add(ContentBlock(ContentTypeKeys.ElementActionCtaGroup, GroupActionKey, spanContent));

        // ── Media (c6*) ────────────────────────────────────────────────────
        blocks.Add(ContentBlock(ContentTypeKeys.ElementMediaImage,       GroupMediaKey, spanContent));
        blocks.Add(ContentBlock(ContentTypeKeys.ElementMediaVideo,       GroupMediaKey, spanContent));
        blocks.Add(ContentBlock(ContentTypeKeys.ElementMediaIcon,        GroupMediaKey, spanContent));
        blocks.Add(ContentBlock(ContentTypeKeys.ElementMediaGalleryItem, GroupMediaKey, spanContent));
        blocks.Add(ContentBlock(ContentTypeKeys.ElementMediaLogoItem,    GroupMediaKey, spanContent));

        // ── Informational (c7*) ────────────────────────────────────────────
        blocks.Add(ContentBlock(ContentTypeKeys.ElementInfoBadge,           GroupInfoKey, spanContent));
        blocks.Add(ContentBlock(ContentTypeKeys.ElementInfoStat,            GroupInfoKey, spanContent));
        blocks.Add(ContentBlock(ContentTypeKeys.ElementInfoFeature,         GroupInfoKey, spanContent));
        blocks.Add(ContentBlock(ContentTypeKeys.ElementInfoKeyValue,        GroupInfoKey, spanContent));
        blocks.Add(ContentBlock(ContentTypeKeys.ElementInfoTimelineItem,    GroupInfoKey, spanContent));
        blocks.Add(ContentBlock(ContentTypeKeys.ElementInfoFaqItem,         GroupInfoKey, spanContent));
        blocks.Add(ContentBlock(ContentTypeKeys.ElementInfoTestimonialItem, GroupInfoKey, spanContent));

        // ── Composition (c8*) ──────────────────────────────────────────────
        blocks.Add(ContentBlock(ContentTypeKeys.ElementCompCard,            GroupComponentsKey, spanContent));
        blocks.Add(ContentBlock(ContentTypeKeys.ElementCompHero,            GroupComponentsKey, spanFull));
        blocks.Add(ContentBlock(ContentTypeKeys.ElementCompFeatureGrid,     GroupComponentsKey, spanFull));
        blocks.Add(ContentBlock(ContentTypeKeys.ElementCompCtaBanner,       GroupComponentsKey, spanFull));
        blocks.Add(ContentBlock(ContentTypeKeys.ElementCompFaqList,         GroupComponentsKey, spanFull));
        blocks.Add(ContentBlock(ContentTypeKeys.ElementCompTestimonialList, GroupComponentsKey, spanFull));
        blocks.Add(ContentBlock(ContentTypeKeys.ElementCompLogoCloud,       GroupComponentsKey, spanFull));
        blocks.Add(ContentBlock(ContentTypeKeys.ElementCompMediaTextSplit,  GroupComponentsKey, spanFull));

        // ── Integration (c9*) ──────────────────────────────────────────────
        blocks.Add(ContentBlock(ContentTypeKeys.ElementIntScriptEmbed,    GroupIntegrationKey, spanFull));
        blocks.Add(ContentBlock(ContentTypeKeys.ElementIntIframeEmbed,    GroupIntegrationKey, spanContent));
        blocks.Add(ContentBlock(ContentTypeKeys.ElementIntExternalWidget, GroupIntegrationKey, spanContent));
        blocks.Add(ContentBlock(ContentTypeKeys.ElementIntAngularHost,    GroupIntegrationKey, spanFull));
        blocks.Add(ContentBlock(ContentTypeKeys.ElementIntMfHost,         GroupIntegrationKey, spanFull));
        blocks.Add(ContentBlock(ContentTypeKeys.ElementIntMacroHost,      GroupIntegrationKey, spanFull,
            view: "~/App_Plugins/LayoutComposer/views/block-macro-host.html"));

        // ── Corporate (ca*) ────────────────────────────────────────────────
        blocks.Add(ContentBlock(ContentTypeKeys.ElementCorpTabGroup,       GroupCorporateKey, spanFull));
        blocks.Add(ContentBlock(ContentTypeKeys.ElementCorpAlertBar,       GroupCorporateKey, spanFull));
        blocks.Add(ContentBlock(ContentTypeKeys.ElementCorpBannerSlider,   GroupCorporateKey, spanFull));
        blocks.Add(ContentBlock(ContentTypeKeys.ElementCorpNewsletterForm, GroupCorporateKey, spanContent));
        blocks.Add(ContentBlock(ContentTypeKeys.ElementCorpSocialShare,    GroupCorporateKey, spanContent));
        blocks.Add(ContentBlock(ContentTypeKeys.ElementCorpDataTable,      GroupCorporateKey, spanFull));
        blocks.Add(ContentBlock(ContentTypeKeys.ElementCorpContactInfo,  GroupCorporateKey, spanContent));
        blocks.Add(ContentBlock(ContentTypeKeys.ElementCorpMapEmbed,     GroupCorporateKey, spanFull));
        blocks.Add(ContentBlock(ContentTypeKeys.ElementCorpMissionBlock, GroupCorporateKey, spanFull));

        // ── Forms (cc*) ───────────────────────────────────────────────────────
        blocks.Add(ContentBlock(ContentTypeKeys.ElementFormEmbed,      GroupCorporateKey,   spanContent,
            view: "~/App_Plugins/LayoutComposer/views/block-form.html"));

        // ── Blog blocks (c8* extensions) ──────────────────────────────────

        // ── Additions (UI-backed) ──────────────────────────────────────────
        blocks.Add(ContentBlock(ContentTypeKeys.ElementMediaAvatar,     GroupMediaKey,      spanContent));
        blocks.Add(ContentBlock(ContentTypeKeys.ElementInfoPricingCard, GroupInfoKey,        spanContent));
        blocks.Add(ContentBlock(ContentTypeKeys.ElementCompAccordion,   GroupComponentsKey,  spanFull));
        blocks.Add(ContentBlock(ContentTypeKeys.ElementCompFormBlock,   GroupComponentsKey,  spanContent,
            view: "~/App_Plugins/LayoutComposer/views/block-form.html"));

        // ── Blog (b9000009) ────────────────────────────────────────────────
        blocks.Add(ContentBlock(ContentTypeKeys.ElementCompBlogHighlight, GroupBlogKey, spanFull));
        blocks.Add(ContentBlock(ContentTypeKeys.ElementCompArticleList,   GroupBlogKey, spanFull));

        // ── Experiences (b900000a) ─────────────────────────────────────────
        blocks.Add(ContentBlock(ContentTypeKeys.ElementExpFeatureJourney,     GroupExperiencesKey, spanFull));
        blocks.Add(ContentBlock(ContentTypeKeys.ElementExpInsightExplorer,    GroupExperiencesKey, spanFull));
        blocks.Add(ContentBlock(ContentTypeKeys.ElementExpMediaExplorer,      GroupExperiencesKey, spanFull));
        blocks.Add(ContentBlock(ContentTypeKeys.ElementExpContentCarousel,    GroupExperiencesKey, spanFull));
        blocks.Add(ContentBlock(ContentTypeKeys.ElementExpQuizFlow,           GroupExperiencesKey, spanFull));
        blocks.Add(ContentBlock(ContentTypeKeys.ElementExpFilterBoard,        GroupExperiencesKey, spanFull));
        blocks.Add(ContentBlock(ContentTypeKeys.ElementExpRatingWidget,       GroupExperiencesKey, spanFull));
        blocks.Add(ContentBlock(ContentTypeKeys.ElementExpCountdownClock,     GroupExperiencesKey, spanFull));
        blocks.Add(ContentBlock(ContentTypeKeys.ElementExpNotificationStack,  GroupExperiencesKey, spanFull));

        // ── Final configuration ────────────────────────────────────────────

        var config = new BlockGridConfiguration
        {
            Blocks      = blocks.ToArray(),
            BlockGroups = groups,
            GridColumns = 12,
            LayoutStylesheet = LayoutComposerStylesheet
        };

        var existingDt = Dts.GetDataType(DataTypeKeys.BlockGridPageSections);
        if (existingDt is not null)
        {
            // Update configuration in-place — preserves the DataType ID so
            // PropertyTypes referencing this DataType survive without cascading.
            existingDt.Configuration = config;
            Dts.Save(existingDt);
            return;
        }

        var dt = new DataType(editor, _serializer)
        {
            Key           = DataTypeKeys.BlockGridPageSections,
            Name          = "DT.BlockGrid.PageSections",
            Configuration = config
        };
        Dts.Save(dt);
    }

    // ── Block builder helpers ─────────────────────────────────────────────────

    /// <summary>Creates a structural block with Areas (container behavior).</summary>
    private static BlockGridConfiguration.BlockGridBlockConfiguration LayoutBlock(
        Guid elementKey, bool allowAtRoot, bool allowInAreas,
        BlockGridConfiguration.BlockGridColumnSpanOption[] columnSpans,
        int areaGridColumns,
        BlockGridConfiguration.BlockGridAreaConfiguration[] areas,
        string? view = null)
        => new()
        {
            ContentElementTypeKey = elementKey,
            GroupKey              = GroupLayoutKey.ToString(),
            AllowAtRoot           = allowAtRoot,
            AllowInAreas          = allowInAreas,
            ColumnSpanOptions     = columnSpans,
            AreaGridColumns       = areaGridColumns,
            Areas                 = areas,
            View                  = view,
            Stylesheet            = LayoutComposerStylesheet
        };

    /// <summary>Creates a content block (leaf, no areas). View is derived from group unless overridden.</summary>
    private static BlockGridConfiguration.BlockGridBlockConfiguration ContentBlock(
        Guid elementKey, Guid groupKey,
        BlockGridConfiguration.BlockGridColumnSpanOption[] columnSpans,
        string? view = null)
        => new()
        {
            ContentElementTypeKey = elementKey,
            GroupKey              = groupKey.ToString(),
            AllowAtRoot           = true,
            AllowInAreas          = true,
            ColumnSpanOptions     = columnSpans,
            View                  = view ?? GroupView(groupKey),
            Stylesheet            = LayoutComposerStylesheet
        };

    private static string? GroupView(Guid groupKey)
    {
        if (groupKey == GroupTextKey)        return "~/App_Plugins/LayoutComposer/views/block-text.html";
        if (groupKey == GroupActionKey)      return "~/App_Plugins/LayoutComposer/views/block-action.html";
        if (groupKey == GroupMediaKey)       return "~/App_Plugins/LayoutComposer/views/block-media.html";
        if (groupKey == GroupInfoKey)        return "~/App_Plugins/LayoutComposer/views/block-info.html";
        if (groupKey == GroupComponentsKey)  return "~/App_Plugins/LayoutComposer/views/block-composition.html";
        if (groupKey == GroupIntegrationKey) return "~/App_Plugins/LayoutComposer/views/block-integration.html";
        if (groupKey == GroupCorporateKey)   return "~/App_Plugins/LayoutComposer/views/block-corporate.html";
        if (groupKey == GroupBlogKey)         return "~/App_Plugins/LayoutComposer/views/block-blog.html";
        if (groupKey == GroupExperiencesKey)  return "~/App_Plugins/LayoutComposer/views/block-experience.html";
        return null; // Layout group — Divider/Spacer use per-block view overrides
    }

    /// <summary>Creates an Area configuration.</summary>
    private static BlockGridConfiguration.BlockGridAreaConfiguration Area(
        Guid key, string alias, int columnSpan,
        BlockGridConfiguration.BlockGridAreaConfigurationSpecifiedAllowance[]? specifiedAllowance = null)
        => new()
        {
            Key               = key,
            Alias             = alias,
            ColumnSpan        = columnSpan,
            RowSpan           = 1,
            MinAllowed        = 0,
            MaxAllowed        = null, // null = unlimited
            SpecifiedAllowance = specifiedAllowance ?? []
        };

    /// <summary>Creates a ColumnSpanOption.</summary>
    private static BlockGridConfiguration.BlockGridColumnSpanOption ColSpan(int span)
        => new() { ColumnSpan = span };

    // ── Site.Root ──────────────────────────────────────────────────────────

    private void EnsureSiteRoot(int folderId)
    {
        var siteRootTemplate = EnsureTemplate("Site Root", "SiteRoot");

        var existing = Cts.Get(ContentTypeKeys.SiteRoot)
                    ?? Cts.Get(ContentTypeKeys.Aliases.SiteRoot);

        if (existing is not null)
        {
            // Patch alias + templates on pre-existing type
            var dirty = false;
            if (!string.Equals(existing.Name, "Site Root", StringComparison.Ordinal))
            { existing.Name = "Site Root"; dirty = true; }
            dirty |= PatchTypeDescription(existing, "Nodo raíz del sitio. Contiene el nombre, logo y navegación global.");
            if (existing.ParentId != folderId)
            { existing.ParentId = folderId; dirty = true; }
            if (!string.Equals(existing.Alias, ContentTypeKeys.Aliases.SiteRoot, StringComparison.Ordinal))
            { existing.Alias = ContentTypeKeys.Aliases.SiteRoot; dirty = true; }
            if (existing.AllowedTemplates?.Any() != true)
            { existing.AllowedTemplates = new[] { siteRootTemplate }; existing.SetDefaultTemplate(siteRootTemplate); dirty = true; }

            // Navigation and footer fields now live exclusively in SiteSettings.
            // SiteRoot retains only identity fields (siteName, siteTagline, siteLogo, defaultCulture).
            dirty |= PatchPropertyDescription(existing, "siteName",    "Nombre principal del sitio. Se usa en la cabecera, el árbol de Umbraco y como referencia editorial general.");
            dirty |= PatchPropertyDescription(existing, "siteTagline", "Bajada o frase corta de marca. Acompaña el nombre del sitio cuando el diseño la muestra.");
            dirty |= PatchPropertyDescription(existing, "siteLogo",    "Logo principal del sitio. Se muestra en cabecera, pie u otros espacios de identidad visual.");

            // Remove legacy fields that now live exclusively in SiteSettings.
            // They were present in old siteroot.config but are now orphaned in the DB.
            foreach (var legacyAlias in new[] { "footerCopy", "headerNavigation", "footerNavigation" })
            {
                if (existing.PropertyTypes.Any(p => p.Alias == legacyAlias))
                {
                    existing.RemovePropertyType(legacyAlias);
                    dirty = true;
                }
            }

            if (!existing.PropertyTypes.Any(p => p.Alias == "defaultCulture"))
            {
                var settingsGroup = existing.PropertyGroups.FirstOrDefault(g => g.Alias == "settings");
                if (settingsGroup is not null)
                {
                    settingsGroup.PropertyTypes!.Add(Prop("defaultCulture", "Default Culture", DataTypeKeys.SelectSiteCulture, 30,
                        description: "Cultura/idioma de este sitio (ej: en-US, es-ES). Determina el idioma de las etiquetas y el Dictionary de Umbraco. Crea un SiteRoot por idioma para activar el selector de cultura en el header."));
                    dirty = true;
                }
            }

            if (dirty) Cts.Save(existing);
            return;
        }

        var ct = new ContentType(Ssh, folderId)
        {
            Key           = ContentTypeKeys.SiteRoot,
            Name          = "Site Root",
            Alias         = ContentTypeKeys.Aliases.SiteRoot,
            Description   = "Nodo raíz del sitio. Contiene el nombre, logo y navegación global.",
            Icon          = "icon-home",
            AllowedAsRoot = true
        };

        ct.ContentTypeComposition = new[] { ContentTypeKeys.CompSeo, ContentTypeKeys.CompVisibility }
            .Select(k => Cts.Get(k))
            .Where(c => c is not null)
            .Cast<IContentTypeComposition>()
            .ToList();

        // SiteRoot holds only identity fields. Navigation and footer copy live in SiteSettings.
        var tab = Tab("Settings", "settings", 0);
        tab.PropertyTypes!.Add(Prop("siteName",       "Site Name", DataTypeKeys.TextTitle,         0,  mandatory: true, description: "Nombre principal del sitio. Se usa en la cabecera, el árbol de Umbraco y como referencia editorial general."));
        tab.PropertyTypes!.Add(Prop("siteTagline",    "Tagline",   DataTypeKeys.TextSubtitle,      10, description: "Bajada o frase corta de marca. Acompaña el nombre del sitio cuando el diseño la muestra."));
        tab.PropertyTypes!.Add(Prop("siteLogo",       "Logo",      DataTypeKeys.MediaImage,        20, description: "Logo principal del sitio. Se muestra en cabecera, pie u otros espacios de identidad visual."));
        tab.PropertyTypes!.Add(Prop("defaultCulture", "Default Culture", DataTypeKeys.SelectSiteCulture, 30, description: "Cultura/idioma de este sitio (ej: en-US, es-ES). Determina el idioma de las etiquetas y el Dictionary de Umbraco. Crea un SiteRoot por idioma para activar el selector de cultura en el header."));

        ct.PropertyGroups.Add(tab);
        ct.AllowedTemplates = new[] { siteRootTemplate };
        ct.SetDefaultTemplate(siteRootTemplate);
        Cts.Save(ct);
    }

    // ── Page.Base ─────────────────────────────────────────────────────────────
    // Contenedor mínimo. Toda la riqueza viene del Block Grid.

    private void EnsurePageBase(int folderId)
    {
        var pageBaseTemplate = EnsureTemplate("Page Base", "PageBase");
        var pageBareTemplate = EnsureTemplate("Page Bare", "PageBare");

        var existing = Cts.Get(ContentTypeKeys.PageBase);
        if (existing is not null)
        {
            var dirty = false;
            if (!string.Equals(existing.Name, "Page Base", StringComparison.Ordinal))
            { existing.Name = "Page Base"; dirty = true; }
            dirty |= PatchTypeDescription(existing, "Página estándar del sitio. Construye el contenido arrastrando bloques en la sección 'Page Sections'.");
            if (existing.ParentId != folderId)
            { existing.ParentId = folderId; dirty = true; }
            if (existing.AllowedTemplates?.Any() != true)
            { existing.AllowedTemplates = new[] { pageBaseTemplate, pageBareTemplate }; existing.SetDefaultTemplate(pageBaseTemplate); dirty = true; }
            dirty |= SyncCompositions(existing,
                ContentTypeKeys.CompCoreBase,
                ContentTypeKeys.CompSeo,
                ContentTypeKeys.CompDomLayout,
                ContentTypeKeys.CompDomLayoutProfile,
                ContentTypeKeys.CompVisibility,
                ContentTypeKeys.CompTagging);

            // Enable culture variation for es-ES + en-US bilingual support.
            // Content properties vary per language; DOM/Visibility remain invariant.
            dirty |= PatchCultureVariation(existing, "pageTitle", "pageSubtitle", "pageSections");

            // Patch descriptions for properties created before descriptions were added
            dirty |= PatchPropertyDescription(existing, "pageTitle",    "Título principal de la página. Se renderiza como H1 y como título en el árbol de Umbraco. Máximo 70 caracteres recomendado. Obligatorio para SEO correcto.");
            dirty |= PatchPropertyDescription(existing, "pageSubtitle", "Subtítulo o tagline complementario al título de la página. Aparece debajo del H1 en layouts de tipo hero o banner. Opcional.");
            dirty |= PatchPropertyDescription(existing, "pageSections", "Construye la página arrastrando bloques atómicos. Secciones, columnas, contenido y componentes compuestos disponibles en el catálogo.");

            // Guard: recreate pageSections if a cascading DataType delete destroyed it
            if (!existing.PropertyTypes.Any(p => p.Alias == "pageSections"))
            {
                var contentGroup = existing.PropertyGroups.FirstOrDefault(g => g.Alias == "content");
                if (contentGroup is null)
                {
                    contentGroup = Tab("Content", "content", 0);
                    existing.PropertyGroups.Add(contentGroup);
                }
                contentGroup.PropertyTypes!.Add(Prop("pageSections", "Page Sections", DataTypeKeys.BlockGridPageSections, 20,
                    description: "Construye la página arrastrando bloques atómicos. Secciones, columnas, contenido y componentes compuestos disponibles en el catálogo."));
                dirty = true;
            }

            if (dirty) Cts.Save(existing);
            return;
        }

        var ct = new ContentType(Ssh, folderId)
        {
            Key        = ContentTypeKeys.PageBase,
            Name       = "Page Base",
            Alias      = ContentTypeKeys.Aliases.PageBase,
            Description = "Página estándar del sitio. Construye el contenido arrastrando bloques en la sección 'Page Sections'.",
            Icon       = "icon-document-line",
            Variations = ContentVariation.Culture   // bilingual: es-ES + en-US
        };

        // Composiciones
        ct.ContentTypeComposition = new[]
        {
            ContentTypeKeys.CompCoreBase,
            ContentTypeKeys.CompSeo,
            ContentTypeKeys.CompDomLayout,
            ContentTypeKeys.CompDomLayoutProfile,
            ContentTypeKeys.CompVisibility,
            ContentTypeKeys.CompTagging
        }
        .Select(k => Cts.Get(k))
        .Where(c => c is not null)
        .Cast<IContentTypeComposition>()
        .ToList();

        var tab = Tab("Content", "content", 0);
        var pageTitleProp = Prop("pageTitle",    "Page Title",    DataTypeKeys.TextTitle,             0,
            description: "Título principal de la página. Se renderiza como H1 y como título en el árbol de Umbraco. Máximo 70 caracteres recomendado. Obligatorio para SEO correcto.");
        pageTitleProp.Variations = ContentVariation.Culture;
        tab.PropertyTypes!.Add(pageTitleProp);

        var pageSubtitleProp = Prop("pageSubtitle", "Page Subtitle", DataTypeKeys.TextSubtitle,      10,
            description: "Subtítulo o tagline complementario al título de la página. Aparece debajo del H1 en layouts de tipo hero o banner. Opcional.");
        pageSubtitleProp.Variations = ContentVariation.Culture;
        tab.PropertyTypes!.Add(pageSubtitleProp);

        var pageSectionsProp = Prop("pageSections", "Page Sections", DataTypeKeys.BlockGridPageSections, 20,
            description: "Construye la página arrastrando bloques atómicos. Secciones, columnas, contenido y componentes compuestos disponibles en el catálogo.");
        pageSectionsProp.Variations = ContentVariation.Culture;
        tab.PropertyTypes!.Add(pageSectionsProp);

        ct.PropertyGroups.Add(tab);
        ct.AllowedTemplates = new[] { pageBaseTemplate, pageBareTemplate };
        ct.SetDefaultTemplate(pageBaseTemplate);
        Cts.Save(ct);
    }

    // ── Allowed children ──────────────────────────────────────────────────────

    private void PatchAllowedChildren()
    {
        SetAllowedChildren(ContentTypeKeys.SiteRoot, ContentTypeKeys.PageBase, ContentTypeKeys.ShopRoot);
        SetAllowedChildren(ContentTypeKeys.PageBase, ContentTypeKeys.PageBase);
        SetAllowedChildren(ContentTypeKeys.ShopRoot, ContentTypeKeys.ShopCatalogPage, ContentTypeKeys.ShopCartPage);
        SetAllowedChildren(ContentTypeKeys.ShopCatalogPage, ContentTypeKeys.ShopCategoryPage, ContentTypeKeys.ShopProductPage);
        SetAllowedChildren(ContentTypeKeys.ShopCategoryPage, ContentTypeKeys.ShopProductPage);
        // SiteRoot → BlogHome and BlogHome → BlogPost are patched by BlogInitializer (Phase 8c).
    }

    private void SetAllowedChildren(Guid parentKey, params Guid[] childKeys)
    {
        var parent = Cts.Get(parentKey);
        if (parent is null) return;

        var allowed = childKeys
            .Select(k => Cts.Get(k))
            .Where(ct => ct is not null)
            .Select((ct, i) => new ContentTypeSort(ct!.Id, i))
            .ToArray();

        // Always update — merge with any existing allowed children
        var existing = parent.AllowedContentTypes?.ToList() ?? [];
        var existingIds = existing.Select(a => a.Id.Value).ToHashSet();

        foreach (var a in allowed)
        {
            if (!existingIds.Contains(a.Id.Value))
                existing.Add(a);
        }

        parent.AllowedContentTypes = existing;
        Cts.Save(parent);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Creates or retrieves an Umbraco template in the DB.
    /// Always syncs the DB template Content with the physical cshtml source file.
    /// This repairs databases corrupted by previous runs where SaveTemplate
    /// overwrote custom views with Umbraco's default @inherits UmbracoViewPage.
    /// </summary>
    private ITemplate EnsureTemplate(string name, string alias)
    {
        var physicalPath = Path.Combine(_viewsPath, $"{alias}.cshtml");
        var sourceContent = System.IO.File.Exists(physicalPath)
            ? System.IO.File.ReadAllText(physicalPath)
            : null;

        var existing = _fileService.GetTemplate(alias);
        if (existing is not null)
        {
            if (sourceContent is not null && existing.Content != sourceContent)
            {
                existing.Content = sourceContent;
                _fileService.SaveTemplate(existing);
                // SaveTemplate may overwrite the physical file — restore our source.
                System.IO.File.WriteAllText(physicalPath, sourceContent);
            }
            return existing;
        }

        var template = new Template(Ssh, name, alias);
        if (sourceContent is not null)
            template.Content = sourceContent;

        _fileService.SaveTemplate(template);

        // SaveTemplate may overwrite the physical file — restore our source.
        if (sourceContent is not null)
            System.IO.File.WriteAllText(physicalPath, sourceContent);

        return _fileService.GetTemplate(alias)!;
    }

    /// <summary>
    /// Ensures the ContentType and the listed property aliases all have
    // PatchCultureVariation is inherited from SchemaInitializerBase.
}
