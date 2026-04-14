namespace Synergos.CMS.Schema.Constants;

/// <summary>
/// Stable GUIDs for all Synergos Content Types.
/// Never change these after first deployment.
/// </summary>
public static class ContentTypeKeys
{
    // ─── Core Compositions (b1*) ───────────────────────────────────────────
    public static readonly Guid CompCoreLifecycle  = new("b1000006-0000-0000-0000-000000000000");
    public static readonly Guid CompCoreBase       = new("b1000007-0000-0000-0000-000000000000");
    public static readonly Guid CompCoreOwnership  = new("b1000008-0000-0000-0000-000000000000");
    public static readonly Guid CompCoreTenant     = new("b1000009-0000-0000-0000-000000000000");
    public static readonly Guid CompCoreAccess     = new("b1000010-0000-0000-0000-000000000000");
    public static readonly Guid CompCoreVersioning = new("b1000011-0000-0000-0000-000000000000");
    public static readonly Guid CompCoreAudit      = new("b1000012-0000-0000-0000-000000000000");

    // ─── Content Compositions (b2*) ────────────────────────────────────────
    public static readonly Guid CompContentHeading    = new("b2000001-0000-0000-0000-000000000000");
    public static readonly Guid CompContentText       = new("b2000002-0000-0000-0000-000000000000");
    public static readonly Guid CompContentMedia      = new("b2000003-0000-0000-0000-000000000000");
    public static readonly Guid CompContentCta        = new("b2000004-0000-0000-0000-000000000000");
    public static readonly Guid CompContentBadge      = new("b2000005-0000-0000-0000-000000000000");
    public static readonly Guid CompContentCollection = new("b2000006-0000-0000-0000-000000000000");
    public static readonly Guid CompContentAuthor     = new("b2000007-0000-0000-0000-000000000000");
    public static readonly Guid CompContentDate       = new("b2000008-0000-0000-0000-000000000000");
    public static readonly Guid CompContentMetadata   = new("b2000009-0000-0000-0000-000000000000");
    public static readonly Guid CompContentEmbed      = new("b2000010-0000-0000-0000-000000000000");

    // ─── DOM Compositions (b3*) ────────────────────────────────────────────
    public static readonly Guid CompDomClass         = new("b3000001-0000-0000-0000-000000000000");
    public static readonly Guid CompDomAttributes    = new("b3000002-0000-0000-0000-000000000000");
    public static readonly Guid CompDomLayout        = new("b3000003-0000-0000-0000-000000000000");
    public static readonly Guid CompDomSpacing       = new("b3000004-0000-0000-0000-000000000000");
    public static readonly Guid CompDomVisibility    = new("b3000005-0000-0000-0000-000000000000");
    public static readonly Guid CompDomVariant       = new("b3000006-0000-0000-0000-000000000000");
    public static readonly Guid CompDomLayoutPreset  = new("b3000007-0000-0000-0000-000000000000");
    public static readonly Guid CompDomLayoutProfile = new("b3000008-0000-0000-0000-000000000000");

    // ─── Behavior Compositions (b4*) ──────────────────────────────────────
    public static readonly Guid CompBehaviorTracking    = new("b4000001-0000-0000-0000-000000000000");
    public static readonly Guid CompBehaviorInteraction = new("b4000002-0000-0000-0000-000000000000");
    public static readonly Guid CompBehaviorNavigation  = new("b4000003-0000-0000-0000-000000000000");
    public static readonly Guid CompBehaviorFeatureFlag = new("b4000004-0000-0000-0000-000000000000");
    public static readonly Guid CompBehaviorAsync       = new("b4000005-0000-0000-0000-000000000000");
    public static readonly Guid CompBehaviorScript      = new("b4000006-0000-0000-0000-000000000000");

    // ─── Seo Compositions (b5*) ───────────────────────────────────────────
    public static readonly Guid CompSeo         = new("b5000001-0000-0000-0000-000000000000");

    // ─── Integration Compositions (b6*) ───────────────────────────────────
    public static readonly Guid CompIntegration  = new("b6000001-0000-0000-0000-000000000000");
    public static readonly Guid CompAngularMount = new("b6000002-0000-0000-0000-000000000000");
    public static readonly Guid CompMfMount      = new("b6000003-0000-0000-0000-000000000000");

    // ─── Visibility Compositions (b7*) ────────────────────────────────────
    public static readonly Guid CompVisibility  = new("b7000001-0000-0000-0000-000000000000");
    public static readonly Guid CompTagging     = new("b8000001-0000-0000-0000-000000000000");

    // ─── Element Types — Structural (c3*) ─────────────────────────────────
    public static readonly Guid ElementStructSection   = new("c3000001-0000-0000-0000-000000000000");
    public static readonly Guid ElementStructContainer = new("c3000002-0000-0000-0000-000000000000");
    public static readonly Guid ElementStructGrid      = new("c3000003-0000-0000-0000-000000000000");
    public static readonly Guid ElementStructColumn    = new("c3000004-0000-0000-0000-000000000000");
    public static readonly Guid ElementStructStack     = new("c3000005-0000-0000-0000-000000000000");
    public static readonly Guid ElementStructDivider   = new("c3000006-0000-0000-0000-000000000000");
    public static readonly Guid ElementStructSpacer    = new("c3000007-0000-0000-0000-000000000000");

    // ─── Element Types — Layout Presets (c3*) ─────────────────────────────
    public static readonly Guid LayoutPreset1Col        = new("c3000010-0000-0000-0000-000000000000");
    public static readonly Guid LayoutPreset2ColEqual   = new("c3000011-0000-0000-0000-000000000000");
    public static readonly Guid LayoutPreset3ColEqual   = new("c3000012-0000-0000-0000-000000000000");
    public static readonly Guid LayoutPreset4ColEqual   = new("c3000013-0000-0000-0000-000000000000");
    public static readonly Guid LayoutPresetMainSidebar = new("c3000014-0000-0000-0000-000000000000");

    // ─── Element Types — Textual (c4*) ────────────────────────────────────
    public static readonly Guid ElementTextHeading   = new("c4000001-0000-0000-0000-000000000000");
    public static readonly Guid ElementTextParagraph = new("c4000002-0000-0000-0000-000000000000");
    public static readonly Guid ElementTextRichText  = new("c4000003-0000-0000-0000-000000000000");
    public static readonly Guid ElementTextEyebrow   = new("c4000004-0000-0000-0000-000000000000");
    public static readonly Guid ElementTextQuote     = new("c4000005-0000-0000-0000-000000000000");
    public static readonly Guid ElementTextLabel     = new("c4000006-0000-0000-0000-000000000000");

    // ─── Element Types — Action (c5*) ─────────────────────────────────────
    public static readonly Guid ElementActionButton   = new("c5000001-0000-0000-0000-000000000000");
    public static readonly Guid ElementActionLink     = new("c5000002-0000-0000-0000-000000000000");
    public static readonly Guid ElementActionCtaGroup = new("c5000003-0000-0000-0000-000000000000");

    // ─── Element Types — Media (c6*) ──────────────────────────────────────
    public static readonly Guid ElementMediaImage       = new("c6000001-0000-0000-0000-000000000000");
    public static readonly Guid ElementMediaVideo       = new("c6000002-0000-0000-0000-000000000000");
    public static readonly Guid ElementMediaIcon        = new("c6000003-0000-0000-0000-000000000000");
    public static readonly Guid ElementMediaGalleryItem = new("c6000004-0000-0000-0000-000000000000");
    public static readonly Guid ElementMediaLogoItem    = new("c6000005-0000-0000-0000-000000000000");
    public static readonly Guid ElementMediaAvatar      = new("c6000006-0000-0000-0000-000000000000");

    // ─── Element Types — Informational (c7*) ──────────────────────────────
    public static readonly Guid ElementInfoBadge           = new("c7000001-0000-0000-0000-000000000000");
    public static readonly Guid ElementInfoStat            = new("c7000002-0000-0000-0000-000000000000");
    public static readonly Guid ElementInfoFeature         = new("c7000003-0000-0000-0000-000000000000");
    public static readonly Guid ElementInfoKeyValue        = new("c7000004-0000-0000-0000-000000000000");
    public static readonly Guid ElementInfoTimelineItem    = new("c7000005-0000-0000-0000-000000000000");
    public static readonly Guid ElementInfoFaqItem         = new("c7000006-0000-0000-0000-000000000000");
    public static readonly Guid ElementInfoTestimonialItem = new("c7000007-0000-0000-0000-000000000000");
    public static readonly Guid ElementInfoPricingCard     = new("c7000008-0000-0000-0000-000000000000");

    // ─── Element Types — Composition (c8*) ────────────────────────────────
    public static readonly Guid ElementCompCard             = new("c8000001-0000-0000-0000-000000000000");
    public static readonly Guid ElementCompHero             = new("c8000002-0000-0000-0000-000000000000");
    public static readonly Guid ElementCompFeatureGrid      = new("c8000003-0000-0000-0000-000000000000");
    public static readonly Guid ElementCompCtaBanner        = new("c8000004-0000-0000-0000-000000000000");
    public static readonly Guid ElementCompFaqList          = new("c8000005-0000-0000-0000-000000000000");
    public static readonly Guid ElementCompTestimonialList  = new("c8000006-0000-0000-0000-000000000000");
    public static readonly Guid ElementCompLogoCloud        = new("c8000007-0000-0000-0000-000000000000");
    public static readonly Guid ElementCompMediaTextSplit   = new("c8000008-0000-0000-0000-000000000000");
    public static readonly Guid ElementCompAccordion        = new("c8000009-0000-0000-0000-000000000000");
    public static readonly Guid ElementCompBlogHighlight    = new("c8000010-0000-0000-0000-000000000000");
    public static readonly Guid ElementCompArticleList      = new("c8000011-0000-0000-0000-000000000000");
    public static readonly Guid ElementCompFormBlock        = new("c8000012-0000-0000-0000-000000000000");

    // ─── Element Types — Integration (c9*) ────────────────────────────────
    public static readonly Guid ElementIntScriptEmbed    = new("c9000001-0000-0000-0000-000000000000");
    public static readonly Guid ElementIntIframeEmbed    = new("c9000002-0000-0000-0000-000000000000");
    public static readonly Guid ElementIntExternalWidget = new("c9000003-0000-0000-0000-000000000000");
    public static readonly Guid ElementIntAngularHost    = new("c9000004-0000-0000-0000-000000000000");
    public static readonly Guid ElementIntMfHost         = new("c9000005-0000-0000-0000-000000000000");
    public static readonly Guid ElementIntMacroHost      = new("c9000006-0000-0000-0000-000000000000");

    // ─── Element Types — Corporate (ca*) ──────────────────────────────────
    public static readonly Guid ElementCorpTabGroup       = new("ca000001-0000-0000-0000-000000000000");
    public static readonly Guid ElementCorpAlertBar       = new("ca000002-0000-0000-0000-000000000000");
    public static readonly Guid ElementCorpBannerSlider   = new("ca000003-0000-0000-0000-000000000000");
    public static readonly Guid ElementCorpNewsletterForm = new("ca000004-0000-0000-0000-000000000000");
    public static readonly Guid ElementCorpSocialShare    = new("ca000005-0000-0000-0000-000000000000");
    public static readonly Guid ElementCorpDataTable      = new("ca000006-0000-0000-0000-000000000000");
    public static readonly Guid ElementCorpContactInfo    = new("ca000007-0000-0000-0000-000000000000");
    public static readonly Guid ElementCorpMapEmbed       = new("ca000008-0000-0000-0000-000000000000");
    public static readonly Guid ElementCorpMissionBlock   = new("ca000009-0000-0000-0000-000000000000");

    // ─── Element Types — Experiences (ce*) ───────────────────────────────
    public static readonly Guid ElementExpFeatureJourney    = new("ce000001-0000-0000-0000-000000000000");
    public static readonly Guid ElementExpInsightExplorer   = new("ce000002-0000-0000-0000-000000000000");
    public static readonly Guid ElementExpMediaExplorer     = new("ce000003-0000-0000-0000-000000000000");
    public static readonly Guid ElementExpContentCarousel   = new("ce000004-0000-0000-0000-000000000000");
    public static readonly Guid ElementExpQuizFlow          = new("ce000005-0000-0000-0000-000000000000");
    public static readonly Guid ElementExpFilterBoard       = new("ce000006-0000-0000-0000-000000000000");
    public static readonly Guid ElementExpRatingWidget      = new("ce000007-0000-0000-0000-000000000000");
    public static readonly Guid ElementExpCountdownClock    = new("ce000008-0000-0000-0000-000000000000");
    public static readonly Guid ElementExpNotificationStack = new("ce000009-0000-0000-0000-000000000000");

    // ─── Element Types — Mount Infrastructure (cd*) ──────────────────────
    public static readonly Guid ElementMountParam = new("cd000001-0000-0000-0000-000000000000");

    // ─── Element Types — Navigation (cb*) ────────────────────────────────
    public static readonly Guid ElementNavItem = new("cb000001-0000-0000-0000-000000000000");

    // ─── Element Types — Forms (cc*) ──────────────────────────────────────
    public static readonly Guid ElementFormField = new("cc000001-0000-0000-0000-000000000000");
    public static readonly Guid ElementFormEmbed = new("cc000002-0000-0000-0000-000000000000");

    // ─── Element Types — Shop (cf*) ───────────────────────────────────────
    public static readonly Guid ElementShopProductCard      = new("cf000001-0000-0000-0000-000000000000");
    public static readonly Guid ElementShopProductGrid      = new("cf000002-0000-0000-0000-000000000000");
    public static readonly Guid ElementShopProductDetail    = new("cf000003-0000-0000-0000-000000000000");
    public static readonly Guid ElementShopCartSummary      = new("cf000004-0000-0000-0000-000000000000");
    public static readonly Guid ElementShopCartItem         = new("cf000005-0000-0000-0000-000000000000");
    public static readonly Guid ElementShopPriceDisplay     = new("cf000006-0000-0000-0000-000000000000");
    public static readonly Guid ElementShopQuantitySelector = new("cf000007-0000-0000-0000-000000000000");
    public static readonly Guid ElementShopVariantPicker    = new("cf000008-0000-0000-0000-000000000000");

    // ─── Document Types — Core (d2*) ──────────────────────────────────────
    public static readonly Guid SiteRoot = new("d2000001-0000-0000-0000-000000000000");
    public static readonly Guid PageBase = new("d2000002-0000-0000-0000-000000000000");
    /// <summary>
    /// Brand-agnostic page with no chrome (no header, footer, banner, alert bar).
    /// Renders directly via the PageBare template. Useful for landing campaigns,
    /// modal/iframe content, print layouts, or any page that must control its
    /// own shell without the master _Layout wrapper.
    /// </summary>
    public static readonly Guid PageBare = new("d2000007-0000-0000-0000-000000000000");

    // Retired — kept for DB cleanup reference only.
    public static readonly Guid HomePage     = new("d2000003-0000-0000-0000-000000000000");
    public static readonly Guid AboutPage    = new("d2000004-0000-0000-0000-000000000000");
    public static readonly Guid ServicesPage = new("d2000005-0000-0000-0000-000000000000");
    public static readonly Guid ContactPage  = new("d2000006-0000-0000-0000-000000000000");
    public static readonly Guid LandingPage  = new("d2000007-0000-0000-0000-000000000000");

    // ─── Document Types — Platform (d3*) ──────────────────────────────────
    public static readonly Guid PlatformRoot        = new("d3000001-0000-0000-0000-000000000000");
    public static readonly Guid GlobalSettings      = new("d3000002-0000-0000-0000-000000000000");
    public static readonly Guid SiteSettings        = new("d3000003-0000-0000-0000-000000000000");
    public static readonly Guid ThemeSettings       = new("d3000004-0000-0000-0000-000000000000");
    public static readonly Guid SharedContentFolder = new("d3000005-0000-0000-0000-000000000000");
    public static readonly Guid ReusableBlock       = new("d3000006-0000-0000-0000-000000000000");
    public static readonly Guid NavigationGroup     = new("d3000007-0000-0000-0000-000000000000");
    public static readonly Guid LayoutProfile       = new("d3000008-0000-0000-0000-000000000000");

    // ─── Document Types — Blog (d4*) ──────────────────────────────────────
    public static readonly Guid BlogHome = new("d4000001-0000-0000-0000-000000000000");
    public static readonly Guid BlogPost = new("d4000002-0000-0000-0000-000000000000");
    public static readonly Guid Author   = new("d4000003-0000-0000-0000-000000000000");
    public static readonly Guid Category = new("d4000004-0000-0000-0000-000000000000");

    // ─── Document Types — Forms (d5*) ─────────────────────────────────────
    public static readonly Guid FormDefinition = new("d5000001-0000-0000-0000-000000000000");

    // ─── Document Types — Tags (d6*) ──────────────────────────────────────
    public static readonly Guid PageTag        = new("d6000001-0000-0000-0000-000000000000");
    public static readonly Guid PageTagsFolder = new("d6000002-0000-0000-0000-000000000000");

    // ─── Document Types — Flow Engine (fe*) ──────────────────────────────
    // NOTE: fe000001 and fe000002 are reserved as block-element UDIs in existing
    // Block Grid content. Using fe000010/fe000011 to avoid collision.
    public static readonly Guid FlowSettingsRoot = new("fe000010-0000-0000-0000-000000000000");
    public static readonly Guid FlowDefinition   = new("fe000011-0000-0000-0000-000000000000");

    // ─── Document Types — Shop (d8*) ──────────────────────────────────────
    public static readonly Guid ShopRoot         = new("d8000001-0000-0000-0000-000000000000");
    public static readonly Guid ShopCatalogPage  = new("d8000002-0000-0000-0000-000000000000");
    public static readonly Guid ShopCategoryPage = new("d8000003-0000-0000-0000-000000000000");
    public static readonly Guid ShopProductPage  = new("d8000004-0000-0000-0000-000000000000");
    public static readonly Guid ShopCartPage     = new("d8000005-0000-0000-0000-000000000000");

    // ─── Media Types (e1*) ────────────────────────────────────────────────
    public static readonly Guid MediaImageAsset    = new("e1000001-0000-0000-0000-000000000000");
    public static readonly Guid MediaVideoAsset    = new("e1000002-0000-0000-0000-000000000000");
    public static readonly Guid MediaIconAsset     = new("e1000003-0000-0000-0000-000000000000");
    public static readonly Guid MediaDocumentAsset = new("e1000004-0000-0000-0000-000000000000");
    public static readonly Guid MediaSocialImage   = new("e1000005-0000-0000-0000-000000000000");

    // ─── Aliases (strings para IContentTypeService.Get() lookups) ─────────
    public static class Aliases
    {
        public const string SiteRoot            = "siteRoot";
        public const string PageBase            = "pageBase";
        public const string PageBare            = "pageBare";
        public const string ArticlePage         = "articlePage";
        public const string PlatformRoot        = "platformRoot";
        public const string GlobalSettings      = "globalSettings";
        public const string SiteSettingsAlias    = "siteSettings";
        public const string ThemeSettings       = "themeSettings";
        public const string SharedContentFolder = "sharedContentFolder";
        public const string ReusableBlock       = "reusableBlock";
        public const string NavigationGroup     = "navigationGroup";
        public const string LayoutProfileAlias  = "layoutProfile";
        public const string BlogHome            = "blogHome";
        public const string BlogPost            = "blogPost";
        public const string Author              = "author";
        public const string Category            = "category";
        public const string FormDefinition      = "formDefinition";
        public const string PageTag             = "pageTag";
        public const string PageTagsFolder      = "pageTagsFolder";

        // Flow Engine (suffixed with Alias to avoid shadowing outer Guid members — S3218)
        public const string FlowSettingsRootAlias = "flowSettingsRoot";
        public const string FlowDefinitionAlias   = "flowDefinition";

        // Shop (suffixed with Alias to avoid shadowing outer Guid members — S3218)
        public const string ShopRootAlias         = "shopRoot";
        public const string ShopCatalogPageAlias  = "shopCatalogPage";
        public const string ShopCategoryPageAlias = "shopCategoryPage";
        public const string ShopProductPageAlias  = "shopProductPage";
        public const string ShopCartPageAlias     = "shopCartPage";
    }
}
