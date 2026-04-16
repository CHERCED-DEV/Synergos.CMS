using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;
using Synergos.CMS.Schema.Constants;

namespace Synergos.CMS.Schema.Initializers;

/// <summary>
/// Phase 7 - Creates the atomic Element Types used by the Block Grid.
/// NavigationItem (cb*) and form-related elements (cc*) remain owned by PlatformInitializer.
/// ElementMountParam (cd*) is created here so the BlockList patch in Phase 7.5 can reference it.
/// </summary>
internal sealed class ElementTypeInitializer : SchemaInitializerBase
{
    private readonly ILogger<ElementTypeInitializer> _logger;

    public ElementTypeInitializer(
        IContentTypeService              contentTypeService,
        IDataTypeService                 dataTypeService,
        IShortStringHelper               shortStringHelper,
        ILogger<ElementTypeInitializer>? logger = null)
        : base(contentTypeService, dataTypeService, shortStringHelper)
    {
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<ElementTypeInitializer>.Instance;
    }

    public override void Initialize()
    {
        CleanupOrphanedFolders();

        var elementsRoot = EnsureRootFolder("Elements");

        var layout = EnsureChildFolder(elementsRoot, "Layout");
        EnsureType(ContentTypeKeys.ElementStructSection, "Section", "elementStructSection", "icon-layout", layout,
            ContentTypeKeys.CompDomLayout, ContentTypeKeys.CompDomSpacing, ContentTypeKeys.CompDomLayoutPreset, ContentTypeKeys.CompDomClass, ContentTypeKeys.CompDomAttributes);
        EnsureType(ContentTypeKeys.ElementStructContainer, "Container", "elementStructContainer", "icon-frame", layout,
            ContentTypeKeys.CompDomLayout, ContentTypeKeys.CompDomSpacing, ContentTypeKeys.CompDomLayoutPreset, ContentTypeKeys.CompDomVariant);
        EnsureType(ContentTypeKeys.ElementStructGrid, "Grid", "elementStructGrid", "icon-grid", layout,
            ContentTypeKeys.CompDomLayout, ContentTypeKeys.CompDomSpacing, ContentTypeKeys.CompDomLayoutPreset, ContentTypeKeys.CompDomClass);
        EnsureType(ContentTypeKeys.ElementStructColumn, "Column", "elementStructColumn", "icon-columns", layout,
            ContentTypeKeys.CompDomLayout, ContentTypeKeys.CompDomSpacing, ContentTypeKeys.CompDomLayoutPreset, ContentTypeKeys.CompDomClass);
        EnsureType(ContentTypeKeys.ElementStructStack, "Stack", "elementStructStack", "icon-thumbnail-list", layout,
            ContentTypeKeys.CompDomSpacing, ContentTypeKeys.CompDomLayoutPreset, ContentTypeKeys.CompDomClass);
        EnsureType(ContentTypeKeys.ElementStructDivider, "Divider", "elementStructDivider", "icon-split", layout,
            ContentTypeKeys.CompDomClass, ContentTypeKeys.CompDomVariant);
        EnsureType(ContentTypeKeys.ElementStructSpacer, "Spacer", "elementStructSpacer", "icon-height", layout,
            ContentTypeKeys.CompDomSpacing, ContentTypeKeys.CompDomLayoutPreset, ContentTypeKeys.CompDomClass);

        // Layout Presets — ready-to-use column configurations
        // CompDomLayout      → alignment, direction, containerType
        // CompDomSpacing     → padding, margin, gap
        // CompDomLayoutPreset → noPaddingTop/Bottom, noMarginTop/Bottom
        // CompDomClass       → classList
        // CompDomVariant     → theme, variant
        EnsureType(ContentTypeKeys.LayoutPreset1Col, "1 Columna", "layoutPreset1Col", "icon-layout", layout,
            ContentTypeKeys.CompDomLayout, ContentTypeKeys.CompDomSpacing, ContentTypeKeys.CompDomLayoutPreset, ContentTypeKeys.CompDomClass, ContentTypeKeys.CompDomVariant);
        EnsureType(ContentTypeKeys.LayoutPreset2ColEqual, "2 Columnas", "layoutPreset2ColEqual", "icon-split", layout,
            ContentTypeKeys.CompDomLayout, ContentTypeKeys.CompDomSpacing, ContentTypeKeys.CompDomLayoutPreset, ContentTypeKeys.CompDomClass, ContentTypeKeys.CompDomVariant);
        EnsureType(ContentTypeKeys.LayoutPreset3ColEqual, "3 Columnas", "layoutPreset3ColEqual", "icon-grid", layout,
            ContentTypeKeys.CompDomLayout, ContentTypeKeys.CompDomSpacing, ContentTypeKeys.CompDomLayoutPreset, ContentTypeKeys.CompDomClass, ContentTypeKeys.CompDomVariant);
        EnsureType(ContentTypeKeys.LayoutPreset4ColEqual, "4 Columnas", "layoutPreset4ColEqual", "icon-thumbnails", layout,
            ContentTypeKeys.CompDomLayout, ContentTypeKeys.CompDomSpacing, ContentTypeKeys.CompDomLayoutPreset, ContentTypeKeys.CompDomClass, ContentTypeKeys.CompDomVariant);
        EnsureType(ContentTypeKeys.LayoutPresetMainSidebar, "Principal + Sidebar", "layoutPresetMainSidebar", "icon-panel-show", layout,
            ContentTypeKeys.CompDomLayout, ContentTypeKeys.CompDomSpacing, ContentTypeKeys.CompDomLayoutPreset, ContentTypeKeys.CompDomClass, ContentTypeKeys.CompDomVariant);

        var text = EnsureChildFolder(elementsRoot, "Text");
        EnsureType(ContentTypeKeys.ElementTextHeading, "Heading", "elementTextHeading", "icon-font", text,
            ContentTypeKeys.CompContentHeading, ContentTypeKeys.CompDomClass, ContentTypeKeys.CompDomAttributes);
        EnsureType(ContentTypeKeys.ElementTextParagraph, "Paragraph", "elementTextParagraph", "icon-umb-content", text,
            ContentTypeKeys.CompContentText, ContentTypeKeys.CompDomClass);
        EnsureType(ContentTypeKeys.ElementTextRichText, "Rich Text", "elementTextRichText", "icon-edit", text,
            ContentTypeKeys.CompContentText, ContentTypeKeys.CompDomSpacing, ContentTypeKeys.CompDomVariant);
        EnsureType(ContentTypeKeys.ElementTextEyebrow, "Eyebrow", "elementTextEyebrow", "icon-tag", text,
            ContentTypeKeys.CompContentText, ContentTypeKeys.CompDomClass, ContentTypeKeys.CompDomVariant);
        EnsureType(ContentTypeKeys.ElementTextQuote, "Quote", "elementTextQuote", "icon-quote", text,
            ContentTypeKeys.CompContentText, ContentTypeKeys.CompDomClass, ContentTypeKeys.CompDomVariant);
        EnsureType(ContentTypeKeys.ElementTextLabel, "Label", "elementTextLabel", "icon-name-badge", text,
            ContentTypeKeys.CompContentText, ContentTypeKeys.CompDomClass, ContentTypeKeys.CompDomVariant);

        var action = EnsureChildFolder(elementsRoot, "Action");
        EnsureType(ContentTypeKeys.ElementActionButton, "Button", "elementActionButton", "icon-mouse-cursor", action,
            ContentTypeKeys.CompContentCta, ContentTypeKeys.CompBehaviorNavigation, ContentTypeKeys.CompBehaviorTracking, ContentTypeKeys.CompDomVariant, ContentTypeKeys.CompDomClass);
        EnsureType(ContentTypeKeys.ElementActionLink, "Link", "elementActionLink", "icon-link", action,
            ContentTypeKeys.CompContentCta, ContentTypeKeys.CompBehaviorNavigation, ContentTypeKeys.CompDomClass);
        EnsureType(ContentTypeKeys.ElementActionCtaGroup, "CTA Group", "elementActionCtaGroup", "icon-thumbnails", action,
            ContentTypeKeys.CompContentCollection, ContentTypeKeys.CompDomLayout, ContentTypeKeys.CompDomClass);

        var media = EnsureChildFolder(elementsRoot, "Media");
        EnsureType(ContentTypeKeys.ElementMediaImage, "Image", "elementMediaImage", "icon-picture", media,
            ContentTypeKeys.CompContentMedia, ContentTypeKeys.CompDomSpacing, ContentTypeKeys.CompDomClass);
        EnsureType(ContentTypeKeys.ElementMediaVideo, "Video", "elementMediaVideo", "icon-video", media,
            ContentTypeKeys.CompContentEmbed, ContentTypeKeys.CompDomSpacing, ContentTypeKeys.CompDomVariant);
        EnsureType(ContentTypeKeys.ElementMediaIcon, "Icon", "elementMediaIcon", "icon-favorite", media,
            ContentTypeKeys.CompContentMedia, ContentTypeKeys.CompContentBadge, ContentTypeKeys.CompDomClass, ContentTypeKeys.CompDomVariant);
        EnsureType(ContentTypeKeys.ElementMediaGalleryItem, "Gallery Item", "elementMediaGalleryItem", "icon-pictures", media,
            ContentTypeKeys.CompContentMedia, ContentTypeKeys.CompContentText, ContentTypeKeys.CompDomClass);
        EnsureType(ContentTypeKeys.ElementMediaLogoItem, "Logo Item", "elementMediaLogoItem", "icon-frame-alt", media,
            ContentTypeKeys.CompContentMedia, ContentTypeKeys.CompContentText, ContentTypeKeys.CompContentCta, ContentTypeKeys.CompDomClass);
        EnsureType(ContentTypeKeys.ElementMediaAvatar, "Avatar", "elementMediaAvatar", "icon-user", media,
            ContentTypeKeys.CompContentMedia, ContentTypeKeys.CompContentText, ContentTypeKeys.CompDomClass, ContentTypeKeys.CompDomVariant);

        var info = EnsureChildFolder(elementsRoot, "Info");
        EnsureType(ContentTypeKeys.ElementInfoBadge, "Badge", "elementInfoBadge", "icon-tag", info,
            ContentTypeKeys.CompContentBadge, ContentTypeKeys.CompDomClass, ContentTypeKeys.CompDomVariant);
        EnsureType(ContentTypeKeys.ElementInfoStat, "Stat", "elementInfoStat", "icon-bar-chart", info,
            ContentTypeKeys.CompContentText, ContentTypeKeys.CompDomClass, ContentTypeKeys.CompDomVariant);
        EnsureType(ContentTypeKeys.ElementInfoFeature, "Feature", "elementInfoFeature", "icon-check", info,
            ContentTypeKeys.CompContentText, ContentTypeKeys.CompContentMedia, ContentTypeKeys.CompContentCta, ContentTypeKeys.CompDomClass, ContentTypeKeys.CompDomVariant);
        EnsureType(ContentTypeKeys.ElementInfoKeyValue, "Key Value", "elementInfoKeyValue", "icon-document", info,
            ContentTypeKeys.CompContentText, ContentTypeKeys.CompDomClass);
        EnsureType(ContentTypeKeys.ElementInfoTimelineItem, "Timeline Item", "elementInfoTimelineItem", "icon-calendar", info,
            ContentTypeKeys.CompContentText, ContentTypeKeys.CompContentDate, ContentTypeKeys.CompDomClass, ContentTypeKeys.CompDomVariant);
        EnsureType(ContentTypeKeys.ElementInfoFaqItem, "FAQ Item", "elementInfoFaqItem", "icon-help", info,
            ContentTypeKeys.CompContentText, ContentTypeKeys.CompDomClass, ContentTypeKeys.CompBehaviorInteraction);
        EnsureType(ContentTypeKeys.ElementInfoTestimonialItem, "Testimonial", "elementInfoTestimonialItem", "icon-quote", info,
            ContentTypeKeys.CompContentText, ContentTypeKeys.CompContentMedia, ContentTypeKeys.CompContentBadge, ContentTypeKeys.CompDomClass);
        EnsureType(ContentTypeKeys.ElementInfoPricingCard, "Pricing Card", "elementInfoPricingCard", "icon-currency", info,
            ContentTypeKeys.CompContentText, ContentTypeKeys.CompContentCta, ContentTypeKeys.CompContentBadge,
            ContentTypeKeys.CompContentPricing,
            ContentTypeKeys.CompDomClass, ContentTypeKeys.CompDomVariant);

        var components = EnsureChildFolder(elementsRoot, "Components");
        EnsureType(ContentTypeKeys.ElementCompCard, "Card", "elementCompCard", "icon-item-arrangement", components,
            ContentTypeKeys.CompContentText, ContentTypeKeys.CompContentMedia, ContentTypeKeys.CompContentCta, ContentTypeKeys.CompContentBadge, ContentTypeKeys.CompDomClass, ContentTypeKeys.CompDomVariant);
        EnsureType(ContentTypeKeys.ElementCompHero, "Hero", "elementCompHero", "icon-fullscreen", components,
            ContentTypeKeys.CompContentHeading, ContentTypeKeys.CompContentText, ContentTypeKeys.CompContentMedia, ContentTypeKeys.CompContentCta, ContentTypeKeys.CompDomVariant, ContentTypeKeys.CompDomClass, ContentTypeKeys.CompDomSpacing);
        EnsureType(ContentTypeKeys.ElementCompFeatureGrid, "Feature Grid", "elementCompFeatureGrid", "icon-grid", components,
            ContentTypeKeys.CompContentCollection, ContentTypeKeys.CompDomLayout, ContentTypeKeys.CompDomSpacing, ContentTypeKeys.CompDomClass);
        EnsureType(ContentTypeKeys.ElementCompCtaBanner, "CTA Banner", "elementCompCtaBanner", "icon-megaphone", components,
            ContentTypeKeys.CompContentText, ContentTypeKeys.CompContentCta, ContentTypeKeys.CompDomVariant, ContentTypeKeys.CompDomSpacing);
        EnsureType(ContentTypeKeys.ElementCompMediaTextSplit, "Media + Text", "elementCompMediaTextSplit", "icon-split-alt", components,
            ContentTypeKeys.CompContentText, ContentTypeKeys.CompContentMedia, ContentTypeKeys.CompContentCta, ContentTypeKeys.CompDomLayout, ContentTypeKeys.CompDomVariant);
        // FaqList / TestimonialList / LogoCloud / Accordion legacy retirados —
        // reemplazados por AccordionGroup / TestimonialCarousel / LogoCloudGrid
        // (BlockList tipado o Block Grid Area, según sea interactivo o SSR puro).
        EnsureType(ContentTypeKeys.ElementCompFormBlock, "Form Block", "elementCompFormBlock", "icon-checkbox", components,
            ContentTypeKeys.CompContentText, ContentTypeKeys.CompDomClass, ContentTypeKeys.CompDomVariant);
        // BlogHighlight + ArticleList: the mappers read picker-based properties
        // (blogSource, articles, maxPosts, showExcerpt…) — they don't consume
        // CompContentCollection.items. Create with picker compositions + patch
        // inline props immediately so the single authoritative definition lives
        // here (was previously duplicated in PlatformInitializer → silent skip).
        EnsureType(ContentTypeKeys.ElementCompBlogHighlight, "Blog Highlight", "elementCompBlogHighlight", "icon-article", components,
            ContentTypeKeys.CompContentHeading, ContentTypeKeys.CompDomClass, ContentTypeKeys.CompDomVariant);
        PatchBlogHighlightProps();

        EnsureType(ContentTypeKeys.ElementCompArticleList, "Article List", "elementCompArticleList", "icon-list", components,
            ContentTypeKeys.CompContentHeading, ContentTypeKeys.CompDomClass, ContentTypeKeys.CompDomVariant);
        PatchArticleListProps();
        // ── Area-based containers (SSR puro, cero scripts) ────────────────
        // Los hijos viven en Block Grid Areas, se renderizan SSR uno por uno.
        // Idiomático Umbraco 13; cero DataTypes BlockList extra.
        EnsureAreaContainer(components,
            ContentTypeKeys.ElementCompCardGrid, "Card Grid", "elementCompCardGrid", "icon-thumbnails");
        EnsureAreaContainer(components,
            ContentTypeKeys.ElementCompLogoCloudGrid, "Logo Cloud Grid", "elementCompLogoCloudGrid", "icon-thumbnails");

        // ── BlockList-based containers (web component interactivo con JSON) ─
        // Carousel y Accordion necesitan JS compartido (swipe, toggle) —
        // el "un solo script" es el propósito del patrón.
        EnsureTypedContainer(components,
            ContentTypeKeys.ElementCompTestimonialCarousel, "Testimonial Carousel", "elementCompTestimonialCarousel", "icon-slideshow",
            DataTypeKeys.BlockListTestimonialItems, "testimonialItems", "Testimonials",
            "Testimonios del carrusel. Cada item es un elementInfoTestimonialItem; un solo web component anima todos los slides.");

        EnsureTypedContainer(components,
            ContentTypeKeys.ElementCompAccordionGroup, "Accordion Group", "elementCompAccordionGroup", "icon-list",
            DataTypeKeys.BlockListFaqItems, "faqItems", "FAQs",
            "Preguntas frecuentes agrupadas. Cada item es un elementInfoFaqItem; un solo web component gestiona el toggle de todos los paneles.");

        var integration = EnsureChildFolder(elementsRoot, "Integration");
        EnsureType(ContentTypeKeys.ElementIntScriptEmbed, "Script Embed", "elementIntScriptEmbed", "icon-code", integration,
            ContentTypeKeys.CompBehaviorScript, ContentTypeKeys.CompDomVisibility);
        EnsureType(ContentTypeKeys.ElementIntIframeEmbed, "iFrame Embed", "elementIntIframeEmbed", "icon-browser-window", integration,
            ContentTypeKeys.CompContentEmbed, ContentTypeKeys.CompDomSpacing, ContentTypeKeys.CompDomVariant);
        EnsureType(ContentTypeKeys.ElementIntExternalWidget, "External Widget", "elementIntExternalWidget", "icon-plugin", integration,
            ContentTypeKeys.CompContentEmbed, ContentTypeKeys.CompBehaviorAsync, ContentTypeKeys.CompDomClass, ContentTypeKeys.CompDomVariant);
        EnsureType(ContentTypeKeys.ElementIntAngularHost, "Angular Host", "elementIntAngularHost", "icon-application-window", integration,
            ContentTypeKeys.CompAngularMount, ContentTypeKeys.CompBehaviorAsync, ContentTypeKeys.CompDomClass, ContentTypeKeys.CompDomAttributes);
        EnsureType(ContentTypeKeys.ElementIntMfHost, "MF Host", "elementIntMfHost", "icon-merge", integration,
            ContentTypeKeys.CompMfMount, ContentTypeKeys.CompBehaviorAsync, ContentTypeKeys.CompDomClass);
        EnsureMacroHostType(integration);

        var experiences = EnsureChildFolder(elementsRoot, "Experiences");
        EnsureType(ContentTypeKeys.ElementExpFeatureJourney, "Feature Journey", "experienceFeatureJourney", "icon-map", experiences,
            ContentTypeKeys.CompContentText, ContentTypeKeys.CompContentCollection, ContentTypeKeys.CompDomVariant, ContentTypeKeys.CompDomClass, ContentTypeKeys.CompDomAttributes);
        EnsureType(ContentTypeKeys.ElementExpInsightExplorer, "Insight Explorer", "experienceInsightExplorer", "icon-telescope", experiences,
            ContentTypeKeys.CompContentText, ContentTypeKeys.CompContentCollection, ContentTypeKeys.CompDomVariant, ContentTypeKeys.CompDomClass, ContentTypeKeys.CompDomAttributes);
        EnsureType(ContentTypeKeys.ElementExpMediaExplorer, "Media Explorer", "experienceMediaExplorer", "icon-pictures", experiences,
            ContentTypeKeys.CompContentText, ContentTypeKeys.CompContentCollection, ContentTypeKeys.CompContentMetadata, ContentTypeKeys.CompDomVariant, ContentTypeKeys.CompDomClass, ContentTypeKeys.CompDomAttributes);
        EnsureType(ContentTypeKeys.ElementExpContentCarousel, "Content Carousel", "experienceContentCarousel", "icon-slideshow", experiences,
            ContentTypeKeys.CompContentText, ContentTypeKeys.CompContentCollection, ContentTypeKeys.CompContentMetadata, ContentTypeKeys.CompDomVariant, ContentTypeKeys.CompDomClass, ContentTypeKeys.CompDomAttributes);
        EnsureType(ContentTypeKeys.ElementExpQuizFlow, "Quiz Flow", "experienceQuizFlow", "icon-edit", experiences,
            ContentTypeKeys.CompContentText, ContentTypeKeys.CompContentCollection, ContentTypeKeys.CompDomVariant, ContentTypeKeys.CompDomClass, ContentTypeKeys.CompDomAttributes);
        EnsureType(ContentTypeKeys.ElementExpFilterBoard, "Filter Board", "experienceFilterBoard", "icon-filter", experiences,
            ContentTypeKeys.CompContentText, ContentTypeKeys.CompContentCollection, ContentTypeKeys.CompContentMetadata, ContentTypeKeys.CompDomVariant, ContentTypeKeys.CompDomClass, ContentTypeKeys.CompDomAttributes);
        EnsureType(ContentTypeKeys.ElementExpRatingWidget, "Rating Widget", "experienceRatingWidget", "icon-star", experiences,
            ContentTypeKeys.CompContentText, ContentTypeKeys.CompContentMetadata, ContentTypeKeys.CompDomVariant, ContentTypeKeys.CompDomClass, ContentTypeKeys.CompDomAttributes);
        EnsureType(ContentTypeKeys.ElementExpCountdownClock, "Countdown Clock", "experienceCountdownClock", "icon-time", experiences,
            ContentTypeKeys.CompContentText, ContentTypeKeys.CompContentMetadata, ContentTypeKeys.CompDomVariant, ContentTypeKeys.CompDomClass, ContentTypeKeys.CompDomAttributes);
        EnsureType(ContentTypeKeys.ElementExpNotificationStack, "Notification Stack", "experienceNotificationStack", "icon-message", experiences,
            ContentTypeKeys.CompContentText, ContentTypeKeys.CompContentCollection, ContentTypeKeys.CompDomVariant, ContentTypeKeys.CompDomClass, ContentTypeKeys.CompDomAttributes);

        var corporate = EnsureChildFolder(elementsRoot, "Corporate");
        EnsureType(ContentTypeKeys.ElementCorpTabGroup, "Tab Group", "elementCorpTabGroup", "icon-tab", corporate,
            ContentTypeKeys.CompContentCollection, ContentTypeKeys.CompContentText, ContentTypeKeys.CompDomClass, ContentTypeKeys.CompBehaviorInteraction);
        EnsureType(ContentTypeKeys.ElementCorpAlertBar, "Alert Bar", "elementCorpAlertBar", "icon-alert", corporate,
            ContentTypeKeys.CompContentText, ContentTypeKeys.CompContentCta, ContentTypeKeys.CompDomClass, ContentTypeKeys.CompDomVariant);
        EnsureType(ContentTypeKeys.ElementCorpBannerSlider, "Banner Slider", "elementCorpBannerSlider", "icon-slideshow", corporate,
            ContentTypeKeys.CompContentCollection, ContentTypeKeys.CompContentText, ContentTypeKeys.CompDomClass, ContentTypeKeys.CompDomVariant);
        EnsureType(ContentTypeKeys.ElementCorpNewsletterForm, "Newsletter", "elementCorpNewsletterForm", "icon-inbox", corporate,
            ContentTypeKeys.CompContentText, ContentTypeKeys.CompContentCta, ContentTypeKeys.CompBehaviorAsync, ContentTypeKeys.CompDomClass);
        EnsureType(ContentTypeKeys.ElementCorpSocialShare, "Social Share", "elementCorpSocialShare", "icon-share", corporate,
            ContentTypeKeys.CompContentCollection, ContentTypeKeys.CompDomClass, ContentTypeKeys.CompDomVariant);
        EnsureType(ContentTypeKeys.ElementCorpDataTable, "Data Table", "elementCorpDataTable", "icon-grid", corporate,
            ContentTypeKeys.CompContentText, ContentTypeKeys.CompContentCollection, ContentTypeKeys.CompDomClass, ContentTypeKeys.CompDomVariant);
        EnsureType(ContentTypeKeys.ElementCorpContactInfo, "Contact Info", "elementCorpContactInfo", "icon-message", corporate,
            ContentTypeKeys.CompContentText, ContentTypeKeys.CompContentCta,
            ContentTypeKeys.CompContentLocation,
            ContentTypeKeys.CompDomClass, ContentTypeKeys.CompDomVariant);
        EnsureType(ContentTypeKeys.ElementCorpMapEmbed, "Map Embed", "elementCorpMapEmbed", "icon-globe", corporate,
            ContentTypeKeys.CompContentEmbed,
            ContentTypeKeys.CompContentLocation,
            ContentTypeKeys.CompDomSpacing, ContentTypeKeys.CompDomClass);
        EnsureType(ContentTypeKeys.ElementCorpMissionBlock, "Mission Block", "elementCorpMissionBlock", "icon-certificate", corporate,
            ContentTypeKeys.CompContentText, ContentTypeKeys.CompContentMedia, ContentTypeKeys.CompDomClass, ContentTypeKeys.CompDomVariant);

        var shop = EnsureChildFolder(elementsRoot, "Shop");
        EnsureType(ContentTypeKeys.ElementShopProductCard, "Product Card", "elementShopProductCard", "icon-item-arrangement", shop,
            ContentTypeKeys.CompContentMedia, ContentTypeKeys.CompContentText, ContentTypeKeys.CompContentBadge, ContentTypeKeys.CompDomVariant);
        EnsureType(ContentTypeKeys.ElementShopProductGrid, "Product Grid", "elementShopProductGrid", "icon-grid", shop,
            ContentTypeKeys.CompContentText, ContentTypeKeys.CompContentCollection, ContentTypeKeys.CompDomVariant, ContentTypeKeys.CompDomClass);
        EnsureType(ContentTypeKeys.ElementShopProductDetail, "Product Detail", "elementShopProductDetail", "icon-document", shop,
            ContentTypeKeys.CompContentMedia, ContentTypeKeys.CompContentText, ContentTypeKeys.CompContentBadge, ContentTypeKeys.CompDomVariant, ContentTypeKeys.CompDomLayout);
        EnsureType(ContentTypeKeys.ElementShopCartSummary, "Cart Summary", "elementShopCartSummary", "icon-shopping-basket-remove", shop,
            ContentTypeKeys.CompContentText, ContentTypeKeys.CompDomVariant, ContentTypeKeys.CompBehaviorInteraction);
        EnsureType(ContentTypeKeys.ElementShopCartItem, "Cart Item", "elementShopCartItem", "icon-item-arrangement", shop,
            ContentTypeKeys.CompContentMedia, ContentTypeKeys.CompContentText, ContentTypeKeys.CompDomVariant);
        EnsureType(ContentTypeKeys.ElementShopPriceDisplay, "Price Display", "elementShopPriceDisplay", "icon-currency", shop,
            ContentTypeKeys.CompDomVariant, ContentTypeKeys.CompDomClass);
        EnsureType(ContentTypeKeys.ElementShopQuantitySelector, "Quantity Selector", "elementShopQuantitySelector", "icon-height", shop,
            ContentTypeKeys.CompDomVariant, ContentTypeKeys.CompBehaviorInteraction);
        EnsureType(ContentTypeKeys.ElementShopVariantPicker, "Variant Picker", "elementShopVariantPicker", "icon-list", shop,
            ContentTypeKeys.CompContentCollection, ContentTypeKeys.CompDomVariant, ContentTypeKeys.CompBehaviorInteraction);

        CleanupLegacyChildFolders(elementsRoot);

        // Mount Infrastructure — used by BlockListMountParams block list
        EnsureMountParamType(integration);
    }

    /// <summary>
    /// Creates the MacroHost element type with:
    /// - macroAlias: dropdown from all registered cdn* macros
    /// - mountParams: key/value BlockList forwarded to the macro as parameters
    /// Allows editors to place any CDN macro inside a Block Grid layout.
    /// </summary>
    private void EnsureMacroHostType(int folderId)
    {
        if (Cts.Get(ContentTypeKeys.ElementIntMacroHost) is not null) return;
        if (Cts.Get("elementIntMacroHost") is not null) return;

        var dropdownDt    = Dts.GetDataType(DataTypeKeys.SelectMacroAlias);
        var blockListDt   = Dts.GetDataType(DataTypeKeys.BlockListMountParams);
        if (dropdownDt is null || blockListDt is null) return;

        var ct = new ContentType(Ssh, folderId)
        {
            Key           = ContentTypeKeys.ElementIntMacroHost,
            Name          = "Macro Host",
            Alias         = "elementIntMacroHost",
            Description   = "Puente hacia un macro de Umbraco desde el Block Grid. Permite insertar elementos CDN (Angular) dentro de un layout SSR.",
            Icon          = "icon-plugin",
            IsElement     = true,
            AllowedAsRoot = false,
            ContentTypeComposition = new List<IContentTypeComposition>()
        };

        var propAlias = new PropertyType(Ssh, dropdownDt)
        {
            Alias       = "macroAlias",
            Name        = "Macro",
            Description = "Selecciona el macro CDN a invocar. El macro inyecta el Angular Element correspondiente.",
            Mandatory   = true,
            SortOrder   = 0
        };

        var propParams = new PropertyType(Ssh, blockListDt)
        {
            Alias       = "mountParams",
            Name        = "Parámetros",
            Description = "Pares clave/valor que se pasan al macro como parámetros. Las claves deben coincidir con los aliases de propiedades del macro.",
            Mandatory   = false,
            SortOrder   = 10
        };

        var group = new PropertyGroup(isPublishing: true)
        {
            Name      = "Macro Host",
            Alias     = "macroHost",
            SortOrder = 0,
        };
        group.PropertyTypes = new PropertyTypeCollection(true);
        group.PropertyTypes.Add(propAlias);
        group.PropertyTypes.Add(propParams);

        ct.PropertyGroups.Add(group);
        TrySave(ct);
    }

    /// <summary>
    /// Creates the MountParam element type with raw paramKey/paramValue properties.
    /// This type is used inside the BlockListMountParams DataType, which gets patched
    /// in Phase 7.5 after this method runs.
    /// </summary>
    private void EnsureMountParamType(int folderId)
    {
        if (Cts.Get(ContentTypeKeys.ElementMountParam) is not null) return;
        if (Cts.Get("mountParam") is not null) return;

        var textBoxDt   = Dts.GetDataType(DataTypeKeys.TextIdentifier);
        var textValueDt = Dts.GetDataType(DataTypeKeys.TextUrl);
        if (textBoxDt is null || textValueDt is null) return;

        var ct = new ContentType(Ssh, folderId)
        {
            Key           = ContentTypeKeys.ElementMountParam,
            Name          = "Mount Param",
            Alias         = "mountParam",
            Description   = "Par clave/valor para parametrizar un elemento Angular o microfrontend montado.",
            Icon          = "icon-settings",
            IsElement     = true,
            AllowedAsRoot = false,
            ContentTypeComposition = new List<IContentTypeComposition>()
        };

        var propKey = new PropertyType(Ssh, textBoxDt)
        {
            Alias       = "paramKey",
            Name        = "Key",
            Description = "Nombre del parámetro. Debe coincidir con el nombre del @Input() del componente Angular.",
            SortOrder   = 0
        };
        var propVal = new PropertyType(Ssh, textValueDt)
        {
            Alias       = "paramValue",
            Name        = "Value",
            Description = "Valor del parámetro. Se serializa como string — el componente Angular hace el cast al tipo esperado.",
            SortOrder   = 10
        };

        var group = new PropertyGroup(isPublishing: true)
        {
            Name      = "Mount Param",
            Alias     = "mountParam",
            SortOrder = 0,
        };
        group.PropertyTypes = new PropertyTypeCollection(true);
        group.PropertyTypes.Add(propKey);
        group.PropertyTypes.Add(propVal);

        ct.PropertyGroups.Add(group);
        TrySave(ct);
    }

    /// <summary>
    /// Patches ElementCompBlogHighlight with picker-based inline properties
    /// (blogSource, maxPosts, showExcerpt, showImage, listLayout). Idempotent —
    /// adds missing props only, never modifies existing ones.
    /// </summary>
    private void PatchBlogHighlightProps()
    {
        var ct = Cts.Get(ContentTypeKeys.ElementCompBlogHighlight);
        if (ct is null) return;

        var pickerDt = Dts.GetDataType(DataTypeKeys.ContentPicker);
        var intDt    = Dts.GetDataType(DataTypeKeys.NumberInteger);
        var boolDt   = Dts.GetDataType(DataTypeKeys.ToggleBoolean);
        var layoutDt = Dts.GetDataType(DataTypeKeys.SelectListLayout);
        if (pickerDt is null || intDt is null || boolDt is null || layoutDt is null) return;

        var group = ct.PropertyGroups.FirstOrDefault(g => g.Alias == "blogHighlight");
        if (group is null)
        {
            group = new PropertyGroup(isPublishing: true)
            {
                Name          = "Blog Highlight",
                Alias         = "blogHighlight",
                SortOrder     = 20,
                PropertyTypes = new PropertyTypeCollection(true)
            };
            ct.PropertyGroups.Add(group);
        }

        var dirty = false;
        dirty |= AddMissing(ct, group, pickerDt, "blogSource",  "Blog Source",  0,  "Pick a BlogHome node to pull posts from");
        dirty |= AddMissing(ct, group, intDt,    "maxPosts",    "Max Posts",    10, "Number of posts to display (default: 3)");
        dirty |= AddMissing(ct, group, boolDt,   "showExcerpt", "Show Excerpt", 20, "Render the post excerpt in each card");
        dirty |= AddMissing(ct, group, boolDt,   "showImage",   "Show Image",   30, "Render the featured image in each card");
        dirty |= AddMissing(ct, group, layoutDt, "listLayout",  "Layout",       40, "Presentation layout for the listing");

        if (dirty) TrySave(ct);
    }

    /// <summary>
    /// Patches ElementCompArticleList with picker-based inline properties
    /// (articles, showExcerpt, showImage, listLayout).
    /// </summary>
    private void PatchArticleListProps()
    {
        var ct = Cts.Get(ContentTypeKeys.ElementCompArticleList);
        if (ct is null) return;

        var multiPickerDt = Dts.GetDataType(DataTypeKeys.MultiContentPicker);
        var boolDt        = Dts.GetDataType(DataTypeKeys.ToggleBoolean);
        var layoutDt      = Dts.GetDataType(DataTypeKeys.SelectListLayout);
        if (multiPickerDt is null || boolDt is null || layoutDt is null) return;

        var group = ct.PropertyGroups.FirstOrDefault(g => g.Alias == "articleList");
        if (group is null)
        {
            group = new PropertyGroup(isPublishing: true)
            {
                Name          = "Article List",
                Alias         = "articleList",
                SortOrder     = 20,
                PropertyTypes = new PropertyTypeCollection(true)
            };
            ct.PropertyGroups.Add(group);
        }

        var dirty = false;
        dirty |= AddMissing(ct, group, multiPickerDt, "articles",    "Articles",     0,  "Pick pages or blog posts to display");
        dirty |= AddMissing(ct, group, boolDt,        "showExcerpt", "Show Excerpt", 10, "Render each item's excerpt");
        dirty |= AddMissing(ct, group, boolDt,        "showImage",   "Show Image",   20, "Render each item's featured image");
        dirty |= AddMissing(ct, group, layoutDt,      "listLayout",  "Layout",       30, "Presentation layout for the listing");

        if (dirty) TrySave(ct);
    }

    private bool AddMissing(IContentType ct, PropertyGroup group, IDataType dt,
                            string alias, string name, int sortOrder, string description)
    {
        if (ct.PropertyTypes.Any(p => p.Alias == alias)) return false;
        group.PropertyTypes!.Add(new PropertyType(Ssh, dt)
        {
            Alias = alias, Name = name, SortOrder = sortOrder, Description = description
        });
        return true;
    }

    /// <summary>
    /// Contenedor area-based (SSR idiomático Umbraco 13). El element type
    /// solo expone configuración del grid (título, columnas, gap); los
    /// hijos viven en un Block Grid Area con allowance tipado — cada hijo
    /// se renderiza SSR independientemente (cero scripts añadidos).
    /// Patrón usado por CardGrid y LogoCloudGrid.
    /// </summary>
    private void EnsureAreaContainer(int folderId, Guid key, string name, string alias, string icon)
    {
        EnsureType(key, name, alias, icon, folderId,
            ContentTypeKeys.CompContentText,
            ContentTypeKeys.CompDomClass,
            ContentTypeKeys.CompDomSpacing,
            ContentTypeKeys.CompDomVariant);

        var ct = Cts.Get(key);
        if (ct is null) return;

        var intDt  = Dts.GetDataType(DataTypeKeys.NumberInteger);
        var textDt = Dts.GetDataType(DataTypeKeys.TextIdentifier);
        if (intDt is null || textDt is null) return;

        var dirty = false;

        var group = ct.PropertyGroups.FirstOrDefault(g => g.Alias == "container");
        if (group is null)
        {
            group = new PropertyGroup(isPublishing: true)
            {
                Name          = "Container",
                Alias         = "container",
                SortOrder     = 50,
                PropertyTypes = new PropertyTypeCollection(true)
            };
            ct.PropertyGroups.Add(group);
            dirty = true;
        }

        if (ct.PropertyTypes.All(p => p.Alias != "gridColumns"))
        {
            group.PropertyTypes!.Add(new PropertyType(Ssh, intDt)
            {
                Alias       = "gridColumns",
                Name        = "Columns",
                Description = "Número de columnas en desktop. Vacío = layout automático fluido.",
                SortOrder   = 0
            });
            dirty = true;
        }

        if (ct.PropertyTypes.All(p => p.Alias != "gridGap"))
        {
            group.PropertyTypes!.Add(new PropertyType(Ssh, textDt)
            {
                Alias       = "gridGap",
                Name        = "Gap",
                Description = "Separación entre items. Token CSS (ej. 'sm', 'md', 'lg') o valor con unidad (ej. '1.5rem').",
                SortOrder   = 10
            });
            dirty = true;
        }

        if (dirty) TrySave(ct);
    }

    /// <summary>
    /// Contenedor tipado de N items (patrón "un solo script para N"). Crea
    /// el element con compositions estándar (Text, DomClass/Variant/Spacing)
    /// y añade un grupo "Container" con: items (BlockList tipado), gridColumns,
    /// gridGap. El mapper del contenedor lee los items como datos y emite
    /// un único <c>&lt;script&gt;</c> en el view — evita N scripts duplicados.
    /// Usado solo para componentes interactivos (carousel, accordion) donde
    /// el JS compartido justifica el patrón. Para grids puramente visuales
    /// usar <see cref="EnsureAreaContainer"/> (Block Grid Areas + SSR).
    /// </summary>
    private void EnsureTypedContainer(
        int folderId, Guid key, string name, string alias, string icon,
        Guid itemsDtKey, string itemsAlias, string itemsName, string itemsDescription)
    {
        EnsureType(key, name, alias, icon, folderId,
            ContentTypeKeys.CompContentText,
            ContentTypeKeys.CompDomClass,
            ContentTypeKeys.CompDomSpacing,
            ContentTypeKeys.CompDomVariant);

        var ct = Cts.Get(key);
        if (ct is null) return;

        var blockListDt = Dts.GetDataType(itemsDtKey);
        var intDt       = Dts.GetDataType(DataTypeKeys.NumberInteger);
        var textDt      = Dts.GetDataType(DataTypeKeys.TextIdentifier);
        if (blockListDt is null || intDt is null || textDt is null) return;

        var dirty = false;

        var group = ct.PropertyGroups.FirstOrDefault(g => g.Alias == "container");
        if (group is null)
        {
            group = new PropertyGroup(isPublishing: true)
            {
                Name          = "Container",
                Alias         = "container",
                SortOrder     = 50,
                PropertyTypes = new PropertyTypeCollection(true)
            };
            ct.PropertyGroups.Add(group);
            dirty = true;
        }

        if (ct.PropertyTypes.All(p => p.Alias != itemsAlias))
        {
            group.PropertyTypes!.Add(new PropertyType(Ssh, blockListDt)
            {
                Alias = itemsAlias, Name = itemsName, Description = itemsDescription, SortOrder = 0
            });
            dirty = true;
        }

        if (ct.PropertyTypes.All(p => p.Alias != "gridColumns"))
        {
            group.PropertyTypes!.Add(new PropertyType(Ssh, intDt)
            {
                Alias       = "gridColumns",
                Name        = "Columns",
                Description = "Número de columnas en desktop. Vacío = layout automático fluido.",
                SortOrder   = 10
            });
            dirty = true;
        }

        if (ct.PropertyTypes.All(p => p.Alias != "gridGap"))
        {
            group.PropertyTypes!.Add(new PropertyType(Ssh, textDt)
            {
                Alias       = "gridGap",
                Name        = "Gap",
                Description = "Separación entre items. Token CSS (ej. 'sm', 'md', 'lg') o valor con unidad (ej. '1.5rem').",
                SortOrder   = 20
            });
            dirty = true;
        }

        if (dirty) TrySave(ct);
    }

    private void CleanupOrphanedFolders()
    {
        string[] oldRootFolders = ["Structural", "Textual", "Action", "Media", "Informational", "Composition", "Composed", "Integration", "Corporate"];
        foreach (var name in oldRootFolders)
        {
            var containers = Cts.GetContainers(name, 1);
            foreach (var container in containers.Where(c => c.ParentId == -1))
                Cts.DeleteContainer(container.Id);
        }
    }

    private void CleanupLegacyChildFolders(int parentId)
    {
        DeleteChildFolderIfUnused(parentId, "Structural");
        DeleteChildFolderIfUnused(parentId, "Textual");
        DeleteChildFolderIfUnused(parentId, "Informational");
        DeleteChildFolderIfUnused(parentId, "Composed");
    }

    private void DeleteChildFolderIfUnused(int parentId, string name)
    {
        var folder = Cts.GetContainers(name, 2)
            .FirstOrDefault(c => c.ParentId == parentId);
        if (folder is null) return;

        var isInUse = Cts.GetAll().Any(ct => ct.ParentId == folder.Id);
        if (!isInUse)
            Cts.DeleteContainer(folder.Id);
    }

    private static string GetDescription(Guid key) => key switch
    {
        var k when k == ContentTypeKeys.ElementStructSection => "Bloque estructural principal. Agrupa otros bloques y define una sección completa de la página.",
        var k when k == ContentTypeKeys.ElementStructContainer => "Contenedor genérico para agrupar contenido y controlar su ancho o alineación.",
        var k when k == ContentTypeKeys.ElementStructGrid => "Estructura de columnas para distribuir bloques en una cuadrícula.",
        var k when k == ContentTypeKeys.ElementStructColumn => "Columna individual dentro de un grid. Contiene bloques de contenido o componentes.",
        var k when k == ContentTypeKeys.ElementStructStack => "Contenedor vertical para apilar bloques uno debajo del otro con espaciado consistente.",
        var k when k == ContentTypeKeys.ElementStructDivider => "Separador visual entre bloques o secciones de contenido.",
        var k when k == ContentTypeKeys.ElementStructSpacer => "Espacio en blanco controlado para ajustar el ritmo visual entre bloques.",
        var k when k == ContentTypeKeys.LayoutPreset1Col => "Layout de 1 columna al 100% del ancho. Ideal para contenido de ancho completo.",
        var k when k == ContentTypeKeys.LayoutPreset2ColEqual => "Layout de 2 columnas iguales (50% + 50%). Ideal para comparaciones o contenido lado a lado.",
        var k when k == ContentTypeKeys.LayoutPreset3ColEqual => "Layout de 3 columnas iguales (33% + 33% + 33%). Ideal para features, cards o beneficios.",
        var k when k == ContentTypeKeys.LayoutPreset4ColEqual => "Layout de 4 columnas iguales (25% × 4). Ideal para logos, stats o grids compactos.",
        var k when k == ContentTypeKeys.LayoutPresetMainSidebar => "Layout principal + sidebar (66% + 33%). Ideal para artículos, blogs o contenido con panel lateral.",
        var k when k == ContentTypeKeys.ElementTextHeading => "Encabezado semántico con control de nivel y texto principal.",
        var k when k == ContentTypeKeys.ElementTextParagraph => "Bloque de texto breve o descriptivo para párrafos simples.",
        var k when k == ContentTypeKeys.ElementTextRichText => "Texto enriquecido para contenido largo con formato editorial.",
        var k when k == ContentTypeKeys.ElementTextEyebrow => "Texto corto de contexto colocado sobre un título o bloque principal.",
        var k when k == ContentTypeKeys.ElementTextQuote => "Cita destacada para testimonios, frases clave o extractos editoriales.",
        var k when k == ContentTypeKeys.ElementTextLabel => "Etiqueta corta para identificar estados, campos o pequeñas piezas de información.",
        var k when k == ContentTypeKeys.ElementActionButton => "Botón de llamada a la acción con enlace y estilo configurable.",
        var k when k == ContentTypeKeys.ElementActionLink => "Enlace editorial simple para navegación o acciones secundarias.",
        var k when k == ContentTypeKeys.ElementActionCtaGroup => "Grupo de botones o enlaces para presentar varias acciones relacionadas.",
        var k when k == ContentTypeKeys.ElementMediaImage => "Imagen individual con soporte para texto alternativo y metadatos básicos.",
        var k when k == ContentTypeKeys.ElementMediaVideo => "Bloque de video o recurso audiovisual embebido.",
        var k when k == ContentTypeKeys.ElementMediaIcon => "Icono visual para reforzar mensajes, estados o beneficios.",
        var k when k == ContentTypeKeys.ElementMediaGalleryItem => "Elemento individual de una galería con imagen y texto opcional.",
        var k when k == ContentTypeKeys.ElementMediaLogoItem => "Logo individual para listados de marcas, aliados o clientes.",
        var k when k == ContentTypeKeys.ElementMediaAvatar => "Avatar o retrato de una persona, autor o integrante del equipo.",
        var k when k == ContentTypeKeys.ElementInfoBadge => "Insignia breve para resaltar estados, categorías o atributos clave.",
        var k when k == ContentTypeKeys.ElementInfoStat => "Dato numérico o métrica destacada con texto de apoyo.",
        var k when k == ContentTypeKeys.ElementInfoFeature => "Ítem informativo para beneficios, capacidades o características.",
        var k when k == ContentTypeKeys.ElementInfoKeyValue => "Par clave-valor para fichas técnicas, detalles o especificaciones.",
        var k when k == ContentTypeKeys.ElementInfoTimelineItem => "Paso o evento individual dentro de una línea de tiempo.",
        var k when k == ContentTypeKeys.ElementInfoFaqItem => "Pregunta frecuente con su respuesta asociada.",
        var k when k == ContentTypeKeys.ElementInfoTestimonialItem => "Testimonio individual con texto, autor e información visual opcional.",
        var k when k == ContentTypeKeys.ElementInfoPricingCard => "Tarjeta de precio o plan con detalles y acción principal.",
        var k when k == ContentTypeKeys.ElementCompCard => "Tarjeta de contenido reutilizable con imagen, texto y enlace.",
        var k when k == ContentTypeKeys.ElementCompHero => "Bloque de cabecera visual con título, descripción, imagen y llamada a la acción.",
        var k when k == ContentTypeKeys.ElementCompFeatureGrid => "Conjunto de features organizado en formato de rejilla.",
        var k when k == ContentTypeKeys.ElementCompCtaBanner => "Banner destacado para una llamada a la acción principal.",
        var k when k == ContentTypeKeys.ElementCompMediaTextSplit => "Componente dividido entre contenido visual y texto.",
        var k when k == ContentTypeKeys.ElementCompFormBlock => "Bloque de formulario con encabezado y referencia a una definición de formulario.",
        var k when k == ContentTypeKeys.ElementCompBlogHighlight => "Resumen del blog para destacar entradas recientes o un contenido principal.",
        var k when k == ContentTypeKeys.ElementCompArticleList => "Listado curado de páginas o artículos seleccionados manualmente.",
        var k when k == ContentTypeKeys.ElementCompCardGrid => "Grid de tarjetas. Acepta N Cards y los renderiza con un solo web component — un único script en vez de N.",
        var k when k == ContentTypeKeys.ElementCompLogoCloudGrid => "Logo Cloud. Contenedor de logos con un solo web component — un único script para toda la nube.",
        var k when k == ContentTypeKeys.ElementCompTestimonialCarousel => "Carrusel de testimonios. Un solo web component anima todos los slides — un único script para el carrusel completo.",
        var k when k == ContentTypeKeys.ElementCompAccordionGroup => "Acordeón de FAQs. Un solo web component gestiona el toggle — un único script para todo el acordeón.",
        var k when k == ContentTypeKeys.ElementIntScriptEmbed => "Inserta un script externo o interno cuando una integración lo requiere.",
        var k when k == ContentTypeKeys.ElementIntIframeEmbed => "Muestra contenido externo dentro de un iframe.",
        var k when k == ContentTypeKeys.ElementIntExternalWidget => "Contenedor para widgets de terceros embebidos en la página.",
        var k when k == ContentTypeKeys.ElementIntAngularHost => "Punto de montaje para un componente o experiencia Angular externa.",
        var k when k == ContentTypeKeys.ElementIntMfHost      => "Punto de montaje para un microfrontend externo.",
        var k when k == ContentTypeKeys.ElementIntMacroHost   => "Puente hacia un macro de Umbraco desde el Block Grid. Permite insertar elementos CDN (Angular) dentro de un layout SSR.",
        var k when k == ContentTypeKeys.ElementCorpTabGroup => "Grupo de pestañas para organizar contenido en secciones conmutables.",
        var k when k == ContentTypeKeys.ElementCorpAlertBar => "Barra de alerta o aviso importante visible dentro de la página.",
        var k when k == ContentTypeKeys.ElementCorpBannerSlider => "Carrusel de banners para mensajes promocionales o institucionales.",
        var k when k == ContentTypeKeys.ElementCorpNewsletterForm => "Bloque de suscripción a newsletter o captación de contactos.",
        var k when k == ContentTypeKeys.ElementCorpSocialShare => "Bloque para compartir contenido en redes sociales.",
        var k when k == ContentTypeKeys.ElementCorpDataTable => "Tabla de datos para información comparativa o estructurada.",
        var k when k == ContentTypeKeys.ElementCorpContactInfo => "Bloque con datos de contacto, enlaces y referencias de atención.",
        var k when k == ContentTypeKeys.ElementCorpMapEmbed => "Mapa o ubicación embebida dentro de la página.",
        var k when k == ContentTypeKeys.ElementCorpMissionBlock => "Bloque institucional para propósito, misión o mensaje corporativo.",
        var k when k == ContentTypeKeys.ElementExpFeatureJourney     => "Experiencia interactiva de recorrido por funcionalidades o beneficios clave.",
        var k when k == ContentTypeKeys.ElementExpInsightExplorer    => "Explorador de contenido editorial con filtrado por categorías o etiquetas.",
        var k when k == ContentTypeKeys.ElementExpMediaExplorer      => "Explorador de galería o recursos multimedia con categorías y navegación.",
        var k when k == ContentTypeKeys.ElementExpContentCarousel    => "Carrusel de contenido editorial con navegación y reproducción automática.",
        var k when k == ContentTypeKeys.ElementExpQuizFlow           => "Experiencia interactiva de cuestionario o encuesta con pasos y resultados.",
        var k when k == ContentTypeKeys.ElementExpFilterBoard        => "Tablero de contenido filtrable por categorías o atributos.",
        var k when k == ContentTypeKeys.ElementExpRatingWidget       => "Widget de valoración con sistema de estrellas o puntuación.",
        var k when k == ContentTypeKeys.ElementExpCountdownClock     => "Reloj de cuenta regresiva para eventos o lanzamientos.",
        var k when k == ContentTypeKeys.ElementExpNotificationStack  => "Pila de notificaciones o mensajes emergentes interactivos.",
        // Shop (cf*)
        var k when k == ContentTypeKeys.ElementShopProductCard      => "Tarjeta de producto que carga datos del catálogo via /api/shop/products/sku/{sku}.",
        var k when k == ContentTypeKeys.ElementShopProductGrid      => "Grilla de productos con filtros, búsqueda y paginación via /api/shop/products.",
        var k when k == ContentTypeKeys.ElementShopProductDetail    => "Vista de detalle de producto con galería, variantes, selector de cantidad y CTA de carrito.",
        var k when k == ContentTypeKeys.ElementShopCartSummary      => "Panel drawer del carrito de compras con listado de ítems, totales y checkout.",
        var k when k == ContentTypeKeys.ElementShopCartItem         => "Ítem individual del carrito con imagen, nombre, precio y selector de cantidad.",
        var k when k == ContentTypeKeys.ElementShopPriceDisplay     => "Display de precio con soporte para precio original, precio con descuento y porcentaje.",
        var k when k == ContentTypeKeys.ElementShopQuantitySelector => "Selector de cantidad numérico con controles de incremento/decremento y validación.",
        var k when k == ContentTypeKeys.ElementShopVariantPicker    => "Selector de variantes de producto (talla, color, almacenamiento) en modo botones, swatches o dropdown.",
        _ => string.Empty
    };

    private void EnsureType(Guid key, string name, string alias, string icon, int folderId, params Guid[] compositionKeys)
    {
        var description = GetDescription(key);
        var existing = Cts.Get(key);
        if (existing is not null)
        {
            var dirty = false;

            if (!string.Equals(existing.Name, name, StringComparison.Ordinal))
            {
                existing.Name = name;
                dirty = true;
            }

            if (!string.Equals(existing.Icon, icon, StringComparison.Ordinal))
            {
                existing.Icon = icon;
                dirty = true;
            }

            if (!string.Equals(existing.Description, description, StringComparison.Ordinal))
            {
                existing.Description = description;
                dirty = true;
            }

            if (existing.ParentId != folderId)
            {
                existing.ParentId = folderId;
                dirty = true;
            }

            if (!existing.IsElement)
            {
                existing.IsElement = true;
                dirty = true;
            }

            if (existing.AllowedAsRoot)
            {
                existing.AllowedAsRoot = false;
                dirty = true;
            }

            // Update compositions if they have changed (e.g. a composition was swapped)
            var currentKeys = existing.ContentTypeComposition
                .Select(c => c.Key).ToHashSet();
            var desiredKeys = new HashSet<Guid>(compositionKeys);
            if (!currentKeys.SetEquals(desiredKeys))
            {
                existing.ContentTypeComposition = compositionKeys
                    .Select(k => Cts.Get(k))
                    .Where(c => c is not null)
                    .Cast<IContentTypeComposition>()
                    .ToList();
                dirty = true;
            }

            if (dirty) Cts.Save(existing);
            return;
        }

        var ct = new ContentType(Ssh, folderId)
        {
            Key = key,
            Name = name,
            Alias = alias,
            Description = description,
            Icon = icon,
            IsElement = true,
            AllowedAsRoot = false,
            ContentTypeComposition = new List<IContentTypeComposition>()
        };

        ct.ContentTypeComposition = compositionKeys
            .Select(k => Cts.Get(k))
            .Where(c => c is not null)
            .Cast<IContentTypeComposition>()
            .ToList();

        TrySave(ct);
    }

    // ─── Safe save ────────────────────────────────────────────────────────────

    private void TrySave(IContentType ct)
    {
        try
        {
            Cts.Save(ct);
            _logger.LogDebug("ElementTypeInitializer: created ContentType '{Alias}' ({Key}).", ct.Alias, ct.Key);
        }
        catch (Exception ex)
        {
            var existing = Cts.Get(ct.Key) ?? Cts.Get(ct.Alias);
            if (existing is not null)
            {
                _logger.LogInformation(
                    "ElementTypeInitializer.TrySave: ContentType '{Alias}' exists (Id={Id}) after save error — continuing.",
                    ct.Alias, existing.Id);
                return;
            }

            _logger.LogWarning(ex,
                "ElementTypeInitializer.TrySave: ContentType '{Alias}' ({TypeKey}) was NOT created ({ExType}). " +
                "Check for orphaned umbracoNode row: SELECT uniqueId, text FROM umbracoNode WHERE uniqueId='{OrphanKey}';",
                ct.Alias, ct.Key, ex.GetType().Name, ct.Key);
        }
    }
}
