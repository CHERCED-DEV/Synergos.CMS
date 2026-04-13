using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;

namespace Synergos.CMS.Schema.Initializers;

/// <summary>
/// Creates Umbraco Macros for inline composition within Rich Text Editors.
/// Each macro reuses the same rendering infrastructure as Block Grid elements.
/// Idempotent: creates macros that don't exist; patches MacroSource on stale entries.
/// </summary>
internal sealed class MacroInitializer : ISchemaInitializer
{
    private readonly IMacroService     _macroService;
    private readonly IShortStringHelper _shortStringHelper;

    private const string TextBox   = "Umbraco.TextBox";
    private const string TextArea  = "Umbraco.TextArea";
    private const string TrueFalse = "Umbraco.TrueFalse";

    public MacroInitializer(IMacroService macroService, IShortStringHelper shortStringHelper)
    {
        _macroService      = macroService;
        _shortStringHelper = shortStringHelper;
    }

    public void Initialize()
    {
        // ── Native macros (Rich Text Editor helpers) ──────────────────────────

        EnsureMacro("macroCtaButton", "Macro.CtaButton", "Native/CtaButton",
            ("label",   "Label",   TextBox),
            ("url",     "URL",     TextBox),
            ("target",  "Target",  TextBox),
            ("variant", "Variant", TextBox));

        EnsureMacro("macroImage", "Macro.Image", "Native/Image",
            ("mediaSrc", "Image URL", TextBox),
            ("altText",  "Alt Text",  TextBox),
            ("caption",  "Caption",   TextBox),
            ("width",    "Width",     TextBox));

        EnsureMacro("macroAlertBox", "Macro.AlertBox", "Native/AlertBox",
            ("text",        "Text",        TextBox),
            ("alertType",   "Type",        TextBox),
            ("dismissible", "Dismissible", TrueFalse));

        EnsureMacro("macroVideoEmbed", "Macro.VideoEmbed", "Native/VideoEmbed",
            ("url",   "Video URL", TextBox),
            ("title", "Title",     TextBox));

        EnsureMacro("macroQuote", "Macro.Quote", "Native/Quote",
            ("text",   "Quote Text",  TextBox),
            ("author", "Author",      TextBox),
            ("role",   "Author Role", TextBox));

        EnsureMacro("macroCodeBlock", "Macro.CodeBlock", "Native/CodeBlock",
            ("language", "Language", TextBox),
            ("content",  "Code",     TextArea));

        EnsureMacro("macroStat", "Macro.Stat", "Native/Stat",
            ("value", "Value", TextBox),
            ("label", "Label", TextBox));

        EnsureMacro("macroBadge", "Macro.Badge", "Native/Badge",
            ("text",      "Badge Text", TextBox),
            ("badgeType", "Type",       TextBox));

        EnsureMacro("macroDivider", "Macro.Divider", "Native/Divider",
            ("variant", "Variant", TextBox));

        EnsureMacro("macroNewsletter", "Macro.Newsletter", "Native/Newsletter",
            ("title",       "Title",        TextBox),
            ("buttonLabel", "Button Label", TextBox),
            ("apiEndpoint", "API Endpoint", TextBox));

        EnsureMacro("macroCardPreview", "Macro.CardPreview", "Native/CardPreview",
            ("title",    "Title",     TextBox),
            ("summary",  "Summary",   TextBox),
            ("imageUrl", "Image URL", TextBox),
            ("linkUrl",  "Link URL",  TextBox));

        // ── CDN Angular elements — Modules ────────────────────────────────────

        EnsureMacro("cdnHero", "Cdn.Hero", "Modules/CdnHero",
            ("headingText",  "Heading",      TextBox),
            ("headingLevel", "Heading Level",TextBox),
            ("body",         "Body",         TextArea),
            ("imageSrc",     "Image URL",    TextBox),
            ("imageAlt",     "Image Alt",    TextBox),
            ("ctaLabel",     "CTA Label",    TextBox),
            ("ctaUrl",       "CTA URL",      TextBox),
            ("ctaTarget",    "CTA Target",   TextBox),
            ("variant",      "Variant",      TextBox),
            ("theme",        "Theme",        TextBox));

        EnsureMacro("cdnBanner", "Cdn.Banner", "Modules/CdnBanner",
            ("title",     "Title",      TextBox),
            ("body",      "Body",       TextArea),
            ("ctaLabel",  "CTA Label",  TextBox),
            ("ctaUrl",    "CTA URL",    TextBox),
            ("ctaTarget", "CTA Target", TextBox),
            ("variant",   "Variant",    TextBox),
            ("theme",     "Theme",      TextBox));

        EnsureMacro("cdnBannerSlider", "Cdn.BannerSlider", "Modules/CdnBannerSlider",
            ("headingText", "Heading",     TextBox),
            ("body",        "Body",        TextArea),
            ("variant",     "Variant",     TextBox),
            ("theme",       "Theme",       TextBox),
            ("slidesJson",  "Slides JSON", TextArea));

        EnsureMacro("cdnFaqSection", "Cdn.FaqSection", "Modules/CdnFaqSection",
            ("headingText", "Heading", TextBox),
            ("theme",       "Theme",   TextBox));

        EnsureMacro("cdnFeatureGrid", "Cdn.FeatureGrid", "Modules/CdnFeatureGrid",
            ("headingText", "Heading", TextBox),
            ("variant",     "Variant", TextBox),
            ("theme",       "Theme",   TextBox));

        EnsureMacro("cdnLogoCloud", "Cdn.LogoCloud", "Modules/CdnLogoCloud",
            ("headingText", "Heading", TextBox),
            ("body",        "Body",    TextArea),
            ("variant",     "Variant", TextBox),
            ("theme",       "Theme",   TextBox));

        EnsureMacro("cdnTestimonialSection", "Cdn.TestimonialSection", "Modules/CdnTestimonialSection",
            ("headingText", "Heading", TextBox),
            ("theme",       "Theme",   TextBox));

        EnsureMacro("cdnTabGroup", "Cdn.TabGroup", "Modules/CdnTabGroup",
            ("title",     "Title",      TextBox),
            ("ariaLabel", "Aria Label", TextBox),
            ("variant",   "Variant",    TextBox),
            ("theme",     "Theme",      TextBox));

        EnsureMacro("cdnDataTable", "Cdn.DataTable", "Modules/CdnDataTable",
            ("caption", "Caption", TextBox));

        EnsureMacro("cdnSection", "Cdn.Section", "Modules/CdnSection",
            ("variant", "Variant", TextBox),
            ("theme",   "Theme",   TextBox));

        EnsureMacro("cdnScriptEmbed", "Cdn.ScriptEmbed", "Modules/CdnScriptEmbed",
            ("scriptType", "Script Type", TextBox),
            ("content",    "Script Code", TextArea));

        // ── CDN Angular elements — Compositions ───────────────────────────────

        EnsureMacro("cdnCard", "Cdn.Card", "Compositions/CdnCard",
            ("title",     "Title",      TextBox),
            ("subtitle",  "Subtitle",   TextBox),
            ("body",      "Body",       TextArea),
            ("imageSrc",  "Image URL",  TextBox),
            ("imageAlt",  "Image Alt",  TextBox),
            ("ctaLabel",  "CTA Label",  TextBox),
            ("ctaUrl",    "CTA URL",    TextBox),
            ("badgeText", "Badge Text", TextBox),
            ("badgeType", "Badge Type", TextBox),
            ("variant",   "Variant",    TextBox),
            ("theme",     "Theme",      TextBox));

        EnsureMacro("cdnMediaText", "Cdn.MediaText", "Compositions/CdnMediaText",
            ("imageSrc",      "Image URL",      TextBox),
            ("imageAlt",      "Image Alt",      TextBox),
            ("headingText",   "Heading",        TextBox),
            ("body",          "Body",           TextArea),
            ("ctaLabel",      "CTA Label",      TextBox),
            ("ctaUrl",        "CTA URL",        TextBox),
            ("ctaTarget",     "CTA Target",     TextBox),
            ("mediaPosition", "Media Position", TextBox),
            ("variant",       "Variant",        TextBox),
            ("theme",         "Theme",          TextBox));

        EnsureMacro("cdnAlertBar", "Cdn.AlertBar", "Compositions/CdnAlertBar",
            ("title",       "Title",       TextBox),
            ("description", "Description", TextArea),
            ("ctaLabel",    "CTA Label",   TextBox),
            ("ctaUrl",      "CTA URL",     TextBox),
            ("variant",     "Variant",     TextBox),
            ("theme",       "Theme",       TextBox),
            ("dismissible", "Dismissible", TrueFalse));

        EnsureMacro("cdnButtonGroup", "Cdn.ButtonGroup", "Compositions/CdnButtonGroup",
            ("alignment", "Alignment", TextBox),
            ("direction", "Direction", TextBox),
            ("gap",       "Gap",       TextBox));

        EnsureMacro("cdnCtaGroup", "Cdn.CtaGroup", "Compositions/CdnCtaGroup",
            ("primaryLabel",    "Primary Label",    TextBox),
            ("primaryUrl",      "Primary URL",      TextBox),
            ("primaryTarget",   "Primary Target",   TextBox),
            ("primaryVariant",  "Primary Variant",  TextBox),
            ("secondaryLabel",  "Secondary Label",  TextBox),
            ("secondaryUrl",    "Secondary URL",    TextBox),
            ("secondaryTarget", "Secondary Target", TextBox),
            ("secondaryVariant","Secondary Variant",TextBox),
            ("alignment",       "Alignment",        TextBox));

        EnsureMacro("cdnFaqItem", "Cdn.FaqItem", "Compositions/CdnFaqItem",
            ("question", "Question", TextBox),
            ("answer",   "Answer",   TextArea),
            ("theme",    "Theme",    TextBox));

        EnsureMacro("cdnFeatureItem", "Cdn.FeatureItem", "Compositions/CdnFeatureItem",
            ("headingText", "Heading", TextBox),
            ("body",        "Body",    TextArea),
            ("icon",        "Icon URL",TextBox),
            ("variant",     "Variant", TextBox),
            ("theme",       "Theme",   TextBox));

        EnsureMacro("cdnGalleryItem", "Cdn.GalleryItem", "Compositions/CdnGalleryItem",
            ("src",     "Image URL", TextBox),
            ("alt",     "Alt Text",  TextBox),
            ("caption", "Caption",   TextBox));

        EnsureMacro("cdnIframeEmbed", "Cdn.IframeEmbed", "Compositions/CdnIframeEmbed",
            ("src",             "Source URL",      TextBox),
            ("title",           "Title",           TextBox),
            ("height",          "Height",          TextBox),
            ("allowFullscreen", "Allow Fullscreen",TrueFalse));

        EnsureMacro("cdnInfoBlock", "Cdn.InfoBlock", "Compositions/CdnInfoBlock",
            ("title",    "Title",     TextBox),
            ("body",     "Body",      TextArea),
            ("ctaLabel", "CTA Label", TextBox),
            ("ctaUrl",   "CTA URL",   TextBox),
            ("variant",  "Variant",   TextBox),
            ("theme",    "Theme",     TextBox));

        EnsureMacro("cdnKeyValue", "Cdn.KeyValue", "Compositions/CdnKeyValue",
            ("label",    "Label",     TextBox),
            ("value",    "Value",     TextBox),
            ("helpText", "Help Text", TextBox),
            ("theme",    "Theme",     TextBox));

        EnsureMacro("cdnLogoItem", "Cdn.LogoItem", "Compositions/CdnLogoItem",
            ("src",    "Image URL", TextBox),
            ("alt",    "Alt Text",  TextBox),
            ("href",   "Link URL",  TextBox),
            ("label",  "Label",     TextBox),
            ("target", "Target",    TextBox));

        EnsureMacro("cdnNewsletterForm", "Cdn.NewsletterForm", "Compositions/CdnNewsletterForm",
            ("title",          "Title",          TextBox),
            ("intro",          "Intro",          TextArea),
            ("placeholder",    "Placeholder",    TextBox),
            ("submitLabel",    "Submit Label",   TextBox),
            ("consentText",    "Consent Text",   TextBox),
            ("successMessage", "Success Message",TextBox),
            ("errorMessage",   "Error Message",  TextBox),
            ("actionUrl",      "Action URL",     TextBox),
            ("method",         "Method",         TextBox),
            ("theme",          "Theme",          TextBox));

        EnsureMacro("cdnSocialShare", "Cdn.SocialShare", "Compositions/CdnSocialShare",
            ("title",     "Title",      TextBox),
            ("pageUrl",   "Page URL",   TextBox),
            ("layout",    "Layout",     TextBox),
            ("linksJson", "Links JSON", TextArea));

        EnsureMacro("cdnTestimonialItem", "Cdn.TestimonialItem", "Compositions/CdnTestimonialItem",
            ("quote",     "Quote",      TextArea),
            ("name",      "Name",       TextBox),
            ("role",      "Role",       TextBox),
            ("avatarSrc", "Avatar URL", TextBox),
            ("avatarAlt", "Avatar Alt", TextBox),
            ("theme",     "Theme",      TextBox));

        EnsureMacro("cdnTimelineItem", "Cdn.TimelineItem", "Compositions/CdnTimelineItem",
            ("headingText", "Heading", TextBox),
            ("body",        "Body",    TextArea),
            ("date",        "Date",    TextBox),
            ("variant",     "Variant", TextBox),
            ("theme",       "Theme",   TextBox));

        EnsureMacro("cdnExternalWidget", "Cdn.ExternalWidget", "Compositions/CdnExternalWidget",
            ("src",      "Source URL",  TextBox),
            ("type",     "Embed Type",  TextBox),
            ("title",    "Title",       TextBox),
            ("endpoint", "API Endpoint",TextBox));

        // ── CDN Angular elements — Primitives ─────────────────────────────────

        EnsureMacro("cdnBadge", "Cdn.Badge", "Primitives/CdnBadge",
            ("text",      "Text",       TextBox),
            ("tone",      "Tone",       TextBox),
            ("ariaLabel", "Aria Label", TextBox));

        EnsureMacro("cdnButtonContainer", "Cdn.ButtonContainer", "Primitives/CdnButtonContainer",
            ("label",  "Label",  TextBox),
            ("href",   "URL",    TextBox),
            ("target", "Target", TextBox));

        EnsureMacro("cdnColumn", "Cdn.Column", "Primitives/CdnColumn",
            ("variant", "Variant", TextBox),
            ("theme",   "Theme",   TextBox));

        EnsureMacro("cdnContainerBlock", "Cdn.ContainerBlock", "Primitives/CdnContainerBlock",
            ("elementId", "Element ID", TextBox),
            ("ariaLabel", "Aria Label", TextBox),
            ("variant",   "Variant",    TextBox),
            ("theme",     "Theme",      TextBox));

        EnsureMacro("cdnDivider", "Cdn.Divider", "Primitives/CdnDivider",
            ("variant", "Variant", TextBox),
            ("theme",   "Theme",   TextBox));

        EnsureMacro("cdnGrid", "Cdn.Grid", "Primitives/CdnGrid",
            ("variant", "Variant", TextBox),
            ("theme",   "Theme",   TextBox));

        EnsureMacro("cdnIconBlock", "Cdn.IconBlock", "Primitives/CdnIconBlock",
            ("ariaLabel", "Aria Label", TextBox));

        EnsureMacro("cdnImageBlock", "Cdn.ImageBlock", "Primitives/CdnImageBlock",
            ("src", "Image URL", TextBox),
            ("alt", "Alt Text",  TextBox));

        EnsureMacro("cdnLinkBlock", "Cdn.LinkBlock", "Primitives/CdnLinkBlock",
            ("href",      "URL",        TextBox),
            ("label",     "Label",      TextBox),
            ("target",    "Target",     TextBox),
            ("ariaLabel", "Aria Label", TextBox));

        EnsureMacro("cdnSpacer", "Cdn.Spacer", "Primitives/CdnSpacer");

        EnsureMacro("cdnStack", "Cdn.Stack", "Primitives/CdnStack",
            ("variant", "Variant", TextBox),
            ("theme",   "Theme",   TextBox));

        EnsureMacro("cdnTextBlock", "Cdn.TextBlock", "Primitives/CdnTextBlock",
            ("headingText",  "Heading",       TextBox),
            ("headingLevel", "Heading Level", TextBox),
            ("variant",      "Variant",       TextBox),
            ("theme",        "Theme",         TextBox));

        EnsureMacro("cdnVideoBlock", "Cdn.VideoBlock", "Primitives/CdnVideoBlock",
            ("src",   "Video URL", TextBox),
            ("title", "Title",     TextBox));

        // ── CDN Angular elements — Experiences ────────────────────────────────

        EnsureMacro("cdnFeatureJourney", "Cdn.FeatureJourney", "Experiences/CdnFeatureJourney",
            ("title",     "Title",      TextBox),
            ("theme",     "Theme",      TextBox),
            ("variant",   "Variant",    TextBox),
            ("elementId", "Element ID", TextBox));

        EnsureMacro("cdnInsightExplorer", "Cdn.InsightExplorer", "Experiences/CdnInsightExplorer",
            ("title",     "Title",      TextBox),
            ("theme",     "Theme",      TextBox),
            ("variant",   "Variant",    TextBox),
            ("elementId", "Element ID", TextBox));

        EnsureMacro("cdnMediaExplorer", "Cdn.MediaExplorer", "Experiences/CdnMediaExplorer",
            ("title",           "Title",            TextBox),
            ("theme",           "Theme",            TextBox),
            ("variant",         "Variant",          TextBox),
            ("elementId",       "Element ID",       TextBox),
            ("defaultCategory", "Default Category", TextBox));

        EnsureMacro("cdnContentCarousel", "Cdn.ContentCarousel", "Experiences/CdnContentCarousel",
            ("title",     "Title",       TextBox),
            ("theme",     "Theme",       TextBox),
            ("variant",   "Variant",     TextBox),
            ("elementId", "Element ID",  TextBox),
            ("autoplay",  "Autoplay",    TrueFalse),
            ("interval",  "Interval ms", TextBox));

        EnsureMacro("cdnQuizFlow", "Cdn.QuizFlow", "Experiences/CdnQuizFlow",
            ("title",       "Title",        TextBox),
            ("theme",       "Theme",        TextBox),
            ("variant",     "Variant",      TextBox),
            ("elementId",   "Element ID",   TextBox),
            ("submitLabel", "Submit Label", TextBox));

        EnsureMacro("cdnFilterBoard", "Cdn.FilterBoard", "Experiences/CdnFilterBoard",
            ("title",         "Title",          TextBox),
            ("theme",         "Theme",          TextBox),
            ("variant",       "Variant",        TextBox),
            ("elementId",     "Element ID",     TextBox),
            ("defaultFilter", "Default Filter", TextBox));

        EnsureMacro("cdnRatingWidget", "Cdn.RatingWidget", "Experiences/CdnRatingWidget",
            ("title",     "Title",      TextBox),
            ("theme",     "Theme",      TextBox),
            ("variant",   "Variant",    TextBox),
            ("elementId", "Element ID", TextBox),
            ("maxRating", "Max Rating", TextBox));

        EnsureMacro("cdnCountdownClock", "Cdn.CountdownClock", "Experiences/CdnCountdownClock",
            ("title",       "Title",        TextBox),
            ("theme",       "Theme",        TextBox),
            ("variant",     "Variant",      TextBox),
            ("elementId",   "Element ID",   TextBox),
            ("targetDate",  "Target Date",  TextBox),
            ("expiredText", "Expired Text", TextBox));

        EnsureMacro("cdnNotificationStack", "Cdn.NotificationStack", "Experiences/CdnNotificationStack",
            ("title",     "Title",      TextBox),
            ("theme",     "Theme",      TextBox),
            ("variant",   "Variant",    TextBox),
            ("elementId", "Element ID", TextBox),
            ("maxItems",  "Max Items",  TextBox));

        EnsureMacro("cdnStat", "Cdn.Stat", "Compositions/CdnStat",
            ("value",   "Value",   TextBox),
            ("label",   "Label",   TextBox),
            ("prefix",  "Prefix",  TextBox),
            ("suffix",  "Suffix",  TextBox),
            ("variant", "Variant", TextBox),
            ("theme",   "Theme",   TextBox));

        EnsureMacro("cdnPricingCard", "Cdn.PricingCard", "Compositions/CdnPricingCard",
            ("title",       "Title",       TextBox),
            ("price",       "Price",       TextBox),
            ("period",      "Period",      TextBox),
            ("description", "Description", TextArea),
            ("ctaLabel",    "CTA Label",   TextBox),
            ("ctaUrl",      "CTA URL",     TextBox),
            ("badgeText",   "Badge Text",  TextBox),
            ("badgeTone",   "Badge Tone",  TextBox),
            ("variant",     "Variant",     TextBox),
            ("theme",       "Theme",       TextBox),
            ("featured",    "Featured",    TrueFalse));

        EnsureMacro("cdnAvatar", "Cdn.Avatar", "Primitives/CdnAvatar",
            ("src",     "Image URL", TextBox),
            ("alt",     "Alt Text",  TextBox),
            ("name",    "Name",      TextBox),
            ("size",    "Size",      TextBox),
            ("variant", "Variant",   TextBox));

        // ── CDN Angular elements — Shop (e-commerce) ─────────────────────────────

        EnsureMacro("cdnProductCard", "Cdn.ProductCard", "Shop/CdnProductCard",
            ("productSku", "Product SKU", TextBox),
            ("name",       "Name (editorial fallback)", TextBox),
            ("imageSrc",   "Image URL (editorial fallback)", TextBox),
            ("imageAlt",   "Image Alt",   TextBox),
            ("showPrice",  "Show Price",  TrueFalse),
            ("showBadge",  "Show Badge",  TrueFalse),
            ("layout",     "Layout",      TextBox),
            ("theme",      "Theme",       TextBox),
            ("variant",    "Variant",     TextBox));

        EnsureMacro("cdnProductGrid", "Cdn.ProductGrid", "Shop/CdnProductGrid",
            ("headingText",   "Heading",         TextBox),
            ("categoryAlias", "Category Alias",  TextBox),
            ("maxItems",      "Max Items",        TextBox),
            ("columns",       "Columns (2-4)",    TextBox),
            ("showFilters",   "Show Filters",     TrueFalse),
            ("sortOrder",     "Default Sort",     TextBox),
            ("theme",         "Theme",            TextBox),
            ("variant",       "Variant",          TextBox));

        EnsureMacro("cdnProductDetail", "Cdn.ProductDetail", "Shop/CdnProductDetail",
            ("productSku",             "Product SKU",            TextBox),
            ("showVariantPicker",      "Show Variant Picker",    TrueFalse),
            ("showQuantitySelector",   "Show Quantity Selector", TrueFalse),
            ("showRating",             "Show Rating",            TrueFalse),
            ("layout",                 "Layout",                 TextBox),
            ("theme",                  "Theme",                  TextBox),
            ("variant",                "Variant",                TextBox));

        EnsureMacro("cdnCartSummary", "Cdn.CartSummary", "Shop/CdnCartSummary",
            ("showCoupon",             "Show Coupon Field",     TrueFalse),
            ("checkoutUrl",            "Checkout URL",          TextBox),
            ("continueShoppingUrl",    "Continue Shopping URL", TextBox),
            ("theme",                  "Theme",                 TextBox),
            ("variant",                "Variant",               TextBox));

        EnsureMacro("cdnPriceDisplay", "Cdn.PriceDisplay", "Shop/CdnPriceDisplay",
            ("showOriginalPrice", "Show Original Price", TrueFalse),
            ("showDiscount",      "Show Discount",       TrueFalse),
            ("priceSize",         "Price Size (sm/md/lg)", TextBox),
            ("currency",          "Currency",            TextBox),
            ("theme",             "Theme",               TextBox),
            ("variant",           "Variant",             TextBox));

        EnsureMacro("cdnQuantitySelector", "Cdn.QuantitySelector", "Shop/CdnQuantitySelector",
            ("min",     "Min",     TextBox),
            ("max",     "Max",     TextBox),
            ("step",    "Step",    TextBox),
            ("theme",   "Theme",   TextBox),
            ("variant", "Variant", TextBox));

        EnsureMacro("cdnVariantPicker", "Cdn.VariantPicker", "Shop/CdnVariantPicker",
            ("variantType", "Variant Type (color/size/storage/custom)", TextBox),
            ("displayAs",   "Display As (buttons/swatches/dropdown)",   TextBox),
            ("theme",       "Theme",   TextBox),
            ("variant",     "Variant", TextBox));

        CleanupLegacyMacros();
    }

    /// <summary>
    /// Removes legacy macros that are no longer part of the active schema:
    ///   - 46 macroSg* duplicates (replaced by cdn* equivalents)
    ///   - macroAngularHost, cdnAngularHost, cdnMfHost, cdnMacroHost
    ///     (old "wrapper component" approach, superseded by Block Grid element types)
    ///
    /// Safe to call repeatedly — skips aliases that no longer exist.
    /// </summary>
    private void CleanupLegacyMacros()
    {
        var legacyAliases = new[]
        {
            // Retired wrapper-component macros (pre–Block Grid architecture)
            "macroAngularHost", "cdnAngularHost", "cdnMfHost", "cdnMacroHost",
            // macroSg* duplicates (replaced by cdn* equivalents)
            "macroSgAlertBar",       "macroSgAngularHost",    "macroSgBadge",
            "macroSgBanner",         "macroSgBannerSlider",   "macroSgButtonContainer",
            "macroSgButtonGroup",    "macroSgCard",           "macroSgColumn",
            "macroSgContainerBlock", "macroSgCtaGroup",       "macroSgDataTable",
            "macroSgDivider",        "macroSgExternalWidget", "macroSgFaqItem",
            "macroSgFaqSection",     "macroSgFeatureGrid",    "macroSgFeatureItem",
            "macroSgFeatureJourney", "macroSgGalleryItem",    "macroSgGrid",
            "macroSgHero",           "macroSgIconBlock",      "macroSgIframeEmbed",
            "macroSgImageBlock",     "macroSgInfoBlock",      "macroSgInsightExplorer",
            "macroSgKeyValue",       "macroSgLinkBlock",      "macroSgLogoCloud",
            "macroSgLogoItem",       "macroSgMediaExplorer",  "macroSgMediaText",
            "macroSgMfHost",         "macroSgNewsletterForm", "macroSgScriptEmbed",
            "macroSgSection",        "macroSgSocialShare",    "macroSgSpacer",
            "macroSgStack",          "macroSgTabGroup",       "macroSgTestimonialItem",
            "macroSgTestimonialSection", "macroSgTextBlock",  "macroSgTimelineItem",
            "macroSgVideoBlock"
        };

        foreach (var alias in legacyAliases)
        {
            var macro = _macroService.GetByAlias(alias);
            if (macro is not null)
                _macroService.Delete(macro);
        }
    }

    private void EnsureMacro(string alias, string name, string partialName,
        params (string alias, string label, string editorAlias)[] parameters)
    {
        var expectedSource = $"MacroPartials/{partialName}.cshtml";
        var existing = _macroService.GetByAlias(alias);

        if (existing is not null)
        {
            // Patch stale MacroSource (e.g. after partial view was moved to a subfolder)
            if (existing.MacroSource != expectedSource)
            {
                existing.MacroSource = expectedSource;
                _macroService.Save(existing);
            }
            return;
        }

        var macro = new Macro(
            _shortStringHelper,
            alias,
            name,
            macroSource: expectedSource,
            useInEditor: true,
            cacheByPage: false,
            cacheByMember: false,
            dontRender: false,
            cacheDuration: 0);

        for (var i = 0; i < parameters.Length; i++)
        {
            var (pAlias, pLabel, pEditor) = parameters[i];
            macro.Properties.Add(new MacroProperty(pAlias, pLabel, i, pEditor));
        }

        _macroService.Save(macro);
    }
}
