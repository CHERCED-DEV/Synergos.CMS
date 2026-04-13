using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.PropertyEditors;
using Umbraco.Cms.Core.Serialization;
using Umbraco.Cms.Core.Services;
using Synergos.CMS.Schema.Constants;

namespace Synergos.CMS.Schema.Initializers;

/// <summary>
/// Phase 1 — Creates all Synergos Data Types.
/// Idempotent: skips any Data Type whose Key already exists in the database.
/// </summary>
internal sealed class DataTypeInitializer : ISchemaInitializer
{
    private readonly IDataTypeService                    _dataTypeService;
    private readonly PropertyEditorCollection            _propertyEditors;
    private readonly IConfigurationEditorJsonSerializer  _serializer;

    public DataTypeInitializer(
        IDataTypeService dataTypeService,
        PropertyEditorCollection propertyEditors,
        IConfigurationEditorJsonSerializer serializer)
    {
        _dataTypeService = dataTypeService;
        _propertyEditors = propertyEditors;
        _serializer      = serializer;
    }

    public void Initialize()
    {
        // ── Text ──────────────────────────────────────────────────────────
        EnsureTextBox(DataTypeKeys.TextIdentifier, "DT.Text.Identifier", 100);
        EnsureTextArea(DataTypeKeys.TextMetaDesc,  "DT.Text.Notes",      500);
        EnsureTextBox(DataTypeKeys.TextAuthor,     "DT.Text.Author",     150);
        EnsureTextBox(DataTypeKeys.TextHexColor,   "DT.Text.HexColor",   50);
        EnsureTextArea(DataTypeKeys.TextAreaNotes,  "DT.TextArea.Notes",  2000);

        // ── Toggle ────────────────────────────────────────────────────────
        EnsureTrueFalse(DataTypeKeys.ToggleBoolean, "DT.Toggle.Boolean");

        // ── DateTime ──────────────────────────────────────────────────────
        EnsureDateTimePicker(DataTypeKeys.DateTimePicker, "DT.DateTime.Picker");

        // ── Core Dropdowns ────────────────────────────────────────────────
        EnsureDropdown(DataTypeKeys.SelectLifecycleStatus, "DT.Select.LifecycleStatus",
            ["draft", "active", "inactive", "deprecated", "archived"]);
        EnsureDropdown(DataTypeKeys.SelectOwnerRole,   "DT.Select.OwnerRole",
            ["owner", "editor", "contributor", "viewer"]);
        EnsureDropdown(DataTypeKeys.SelectSiteScope,   "DT.Select.SiteScope",
            ["global", "tenant", "shared"]);
        EnsureDropdown(DataTypeKeys.SelectAccessLevel, "DT.Select.AccessLevel",
            ["public", "private", "restricted"]);

        // ── Content ───────────────────────────────────────────────────────
        EnsureDropdown(DataTypeKeys.SelectHeadingLevel, "DT.Select.HeadingLevel",
            ["h1", "h2", "h3", "h4", "h5", "h6"]);
        EnsureDropdown(DataTypeKeys.SelectLinkTarget, "DT.Select.LinkTarget",
            ["_self", "_blank"]);
        EnsureDropdown(DataTypeKeys.SelectBadgeType, "DT.Select.BadgeType",
            ["info", "success", "warning"]);
        EnsureDropdown(DataTypeKeys.SelectEmbedType, "DT.Select.EmbedType",
            ["video", "map", "iframe"]);

        EnsureTextBox(DataTypeKeys.TextTitle,    "DT.Text.Title",    120);
        EnsureTextBox(DataTypeKeys.TextSubtitle, "DT.Text.Subtitle", 200);
        EnsureTextBox(DataTypeKeys.TextCaption,  "DT.Text.Caption",  200);
        EnsureTextBox(DataTypeKeys.TextUrl,      "DT.Text.Url",      500);
        EnsureTextArea(DataTypeKeys.TextSummary, "DT.Text.Summary",  300);
        EnsureRichText(DataTypeKeys.RichTextBody, "DT.RichText.Body");
        EnsureMediaPicker(DataTypeKeys.MediaImage, "DT.Media.Image");
        EnsureMultiUrlPicker(DataTypeKeys.LinkUrl, "DT.Link.Url");
        EnsureTags(DataTypeKeys.TagsContent, "DT.Tags.Content");
        EnsureBlockListEmpty(DataTypeKeys.BlockListCollection, "DT.BlockList.Collection");

        // ── DOM Dropdowns ──────────────────────────────────────────────────
        EnsureDropdown(DataTypeKeys.SelectContainerType, "DT.Select.ContainerType",
            ["fixed", "fluid", "fullWidth"]);
        EnsureDropdown(DataTypeKeys.SelectAlignmentLayout, "DT.Select.Alignment",
            ["left", "center", "right", "justify"]);
        EnsureDropdown(DataTypeKeys.SelectDirection, "DT.Select.Direction",
            ["row", "column"]);
        EnsureDropdown(DataTypeKeys.SelectVariant, "DT.Select.Variant",
            ["primary", "secondary", "tertiary", "ghost"]);
        EnsureDropdown(DataTypeKeys.SelectTheme, "DT.Select.Theme",
            ["light", "dark", "system"]);

        // ── Behavior Dropdowns ─────────────────────────────────────────────
        EnsureDropdown(DataTypeKeys.SelectInteractionType, "DT.Select.InteractionType",
            ["click", "hover", "scroll", "focus"]);
        EnsureDropdown(DataTypeKeys.SelectInteractionAction, "DT.Select.InteractionAction",
            ["navigate", "openModal", "openDrawer", "submit", "track", "toggle"]);
        EnsureDropdown(DataTypeKeys.SelectNavigationType, "DT.Select.NavigationType",
            ["internal", "external", "anchor"]);
        EnsureDropdown(DataTypeKeys.SelectHttpMethod, "DT.Select.HttpMethod",
            ["GET", "POST", "PUT", "DELETE", "PATCH"]);
        EnsureDropdown(DataTypeKeys.SelectScriptType, "DT.Select.ScriptType",
            ["inline", "external"]);

        // ── Spacing Dropdowns ──────────────────────────────────────────────
        EnsureDropdown(DataTypeKeys.SelectSpacingMargin, "DT.Select.SpacingMargin",
            ["none", "2xs", "xs", "sm", "md", "lg", "xl", "2xl", "3xl", "4xl", "auto"]);
        EnsureDropdown(DataTypeKeys.SelectSpacingPadding, "DT.Select.SpacingPadding",
            ["none", "2xs", "xs", "sm", "md", "lg", "xl", "2xl", "3xl", "4xl"]);
        EnsureDropdown(DataTypeKeys.SelectSpacingGap, "DT.Select.SpacingGap",
            ["none", "2xs", "xs", "sm", "md", "lg", "xl", "2xl", "3xl"]);

        // ── DOM / Behavior Text ────────────────────────────────────────────
        EnsureTextBox(DataTypeKeys.TextClassList,      "DT.Text.ClassList",      500);
        EnsureTextBox(DataTypeKeys.TextSpacingToken,   "DT.Text.SpacingToken",   100);
        EnsureTextArea(DataTypeKeys.TextDataAttributes, "DT.Text.DataAttributes", 1000);
        EnsureTextArea(DataTypeKeys.TextScriptContent,  "DT.Text.ScriptContent",  2000);

        // ── Seo Dropdowns ─────────────────────────────────────────────────
        EnsureDropdown(DataTypeKeys.SelectRobots, "DT.Select.Robots",
            ["index", "noindex", "nofollow", "noindex nofollow"]);

        // ── Integration Dropdowns ─────────────────────────────────────────
        EnsureDropdown(DataTypeKeys.SelectIntegrationProvider, "DT.Select.IntegrationProvider",
            ["custom", "hubspot", "mailchimp", "zapier", "google-analytics", "gtm", "segment"]);

        // ── Atomic Element Dropdowns ──────────────────────────────────────
        EnsureDropdown(DataTypeKeys.SelectCtaVariant,  "DT.Select.CtaVariant",
            ["primary", "secondary", "ghost", "link", "danger"]);
        EnsureDropdown(DataTypeKeys.SelectWidthPreset, "DT.Select.WidthPreset",
            ["xs", "sm", "md", "lg", "xl", "full", "auto"]);
        EnsureDropdown(DataTypeKeys.SelectMediaRatio,  "DT.Select.MediaRatio",
            ["1-1", "4-3", "16-9", "21-9", "auto"]);
        EnsureInteger(DataTypeKeys.NumberInteger, "DT.Number.Integer");

        // ── Platform UI Dropdowns ─────────────────────────────────────────
        EnsureDropdown(DataTypeKeys.SelectButtonStyle,    "DT.Select.ButtonStyle",    ["rounded", "pill", "square"]);
        EnsureDropdown(DataTypeKeys.SelectCardStyle,      "DT.Select.CardStyle",      ["flat", "elevated", "bordered"]);
        EnsureDropdown(DataTypeKeys.SelectHeaderStyle,    "DT.Select.HeaderStyle",    ["fixed", "sticky", "static"]);
        EnsureDropdown(DataTypeKeys.SelectListLayout,     "DT.Select.ListLayout",     ["grid", "list", "magazine"]);
        EnsureDropdown(DataTypeKeys.SelectPageLayout,     "DT.Select.PageLayout",     ["wide", "standard", "narrow"]);
        EnsureDropdown(DataTypeKeys.SelectIdentityPreset, "DT.Select.IdentityPreset", ["custom", "aurora", "graphite", "solaris"]);

        // ── Form Field Dropdowns ──────────────────────────────────────────
        EnsureDropdown(DataTypeKeys.SelectFieldType,  "DT.Select.FieldType",  ["text", "email", "phone", "textarea", "select", "checkbox", "radio", "hidden"]);
        EnsureDropdown(DataTypeKeys.SelectFieldWidth, "DT.Select.FieldWidth", ["full", "half"]);

        // ── Content Pickers (blog, forms, navigation) ─────────────────────
        EnsureContentPicker(DataTypeKeys.ContentPicker, "DT.Picker.Content");
        EnsureContentPickerMulti(DataTypeKeys.MultiContentPicker, "DT.Picker.ContentMulti");
        EnsurePageTagsPicker();

        // ── Site Culture ───────────────────────────────────────────────────
        EnsureDropdown(DataTypeKeys.SelectSiteCulture, "DT.Select.SiteCulture",
            ["en-US", "es-ES"]);

        // ── Angular / MF Mount ─────────────────────────────────────────────
        EnsureDropdown(DataTypeKeys.SelectAngularElement, "DT.Select.AngularElement",
        [
            "experienceFeatureJourney",
            "experienceInsightExplorer",
            "experienceMediaExplorer"
        ]);
        EnsureBlockListEmpty(DataTypeKeys.BlockListMountParams, "DT.BlockList.MountParams");

        // ── Alert Bar ─────────────────────────────────────────────────────────
        EnsureDropdown(DataTypeKeys.SelectAlertVariant, "DT.Select.AlertVariant",
            ["info", "warning", "success", "error"]);

        // ── Flow Engine ───────────────────────────────────────────────────────
        EnsureDropdown(DataTypeKeys.SelectFlowExecutionMode, "DT.Select.FlowExecutionMode",
            ["sequential", "parallel", "firstMatch"]);
        EnsureTextArea(DataTypeKeys.TextAreaJson, "DT.TextArea.Json", 10_000);

        // ── Macro Host ─────────────────────────────────────────────────────
        // Always updated so new cdn* macros are reflected without a schema version bump.
        UpdateDropdown(DataTypeKeys.SelectMacroAlias, "DT.Select.MacroAlias",
        [
            // Modules
            "cdnHero", "cdnBanner", "cdnBannerSlider", "cdnFaqSection", "cdnFeatureGrid",
            "cdnLogoCloud", "cdnTestimonialSection", "cdnTabGroup", "cdnDataTable",
            "cdnSection", "cdnAngularHost", "cdnMfHost", "cdnScriptEmbed", "cdnMacroHost",
            // Experiences
            "cdnFeatureJourney", "cdnInsightExplorer", "cdnMediaExplorer",
            // Compositions
            "cdnCard", "cdnMediaText", "cdnAlertBar", "cdnButtonGroup", "cdnCtaGroup",
            "cdnFaqItem", "cdnFeatureItem", "cdnGalleryItem", "cdnIframeEmbed", "cdnInfoBlock",
            "cdnKeyValue", "cdnLogoItem", "cdnNewsletterForm", "cdnSocialShare",
            "cdnTestimonialItem", "cdnTimelineItem", "cdnExternalWidget",
            // Primitives
            "cdnBadge", "cdnButtonContainer", "cdnColumn", "cdnContainerBlock",
            "cdnDivider", "cdnGrid", "cdnIconBlock", "cdnImageBlock", "cdnLinkBlock",
            "cdnSpacer", "cdnStack", "cdnTextBlock", "cdnVideoBlock"
        ]);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Private helpers — typed configuration objects (Umbraco 13 API)
    // ──────────────────────────────────────────────────────────────────────

    private void EnsureTextBox(Guid key, string name, int maxChars)
    {
        if (Exists(key)) return;
        if (!_propertyEditors.TryGet("Umbraco.TextBox", out var editor)) return;

        var dt = new DataType(editor, _serializer)
        {
            Key           = key,
            Name          = name,
            Configuration = new TextboxConfiguration { MaxChars = maxChars }
        };
        _dataTypeService.Save(dt);
    }

    private void EnsureTextArea(Guid key, string name, int maxChars)
    {
        if (Exists(key)) return;
        if (!_propertyEditors.TryGet("Umbraco.TextArea", out var editor)) return;

        var dt = new DataType(editor, _serializer)
        {
            Key           = key,
            Name          = name,
            Configuration = new TextAreaConfiguration { MaxChars = maxChars }
        };
        _dataTypeService.Save(dt);
    }

    private void EnsureTrueFalse(Guid key, string name)
    {
        if (Exists(key)) return;
        if (!_propertyEditors.TryGet("Umbraco.TrueFalse", out var editor)) return;

        var dt = new DataType(editor, _serializer) { Key = key, Name = name };
        _dataTypeService.Save(dt);
    }

    private void EnsureDateTimePicker(Guid key, string name)
    {
        if (Exists(key)) return;
        if (!_propertyEditors.TryGet("Umbraco.DateTime", out var editor)) return;

        var dt = new DataType(editor, _serializer) { Key = key, Name = name };
        _dataTypeService.Save(dt);
    }

    private void EnsureDropdown(Guid key, string name, string[] values)
    {
        if (!_propertyEditors.TryGet("Umbraco.DropDown.Flexible", out var editor)) return;

        var existing = _dataTypeService.GetDataType(key);
        if (existing is not null)
        {
            // Patch stale display names (migration from "Dropdown - {X}" → "DT.Select.{X}")
            if (existing.Name != name)
            {
                existing.Name = name;
                _dataTypeService.Save(existing);
            }
            return;
        }

        var dt = new DataType(editor, _serializer)
        {
            Key  = key,
            Name = name,
            Configuration = new DropDownFlexibleConfiguration
            {
                Multiple = false,
                Items    = values.Select((v, i) =>
                    new ValueListConfiguration.ValueListItem { Id = i + 1, Value = v })
                    .ToList()
            }
        };
        _dataTypeService.Save(dt);
    }

    /// <summary>
    /// Creates or overwrites a dropdown DataType — use for lists that grow over time
    /// so that new values are reflected on every schema run.
    /// </summary>
    private void UpdateDropdown(Guid key, string name, string[] values)
    {
        if (!_propertyEditors.TryGet("Umbraco.DropDown.Flexible", out var editor)) return;

        var config = new DropDownFlexibleConfiguration
        {
            Multiple = false,
            Items    = values.Select((v, i) =>
                new ValueListConfiguration.ValueListItem { Id = i + 1, Value = v })
                .ToList()
        };

        var existing = _dataTypeService.GetDataType(key);
        if (existing is not null)
        {
            existing.Configuration = config;
            _dataTypeService.Save(existing);
            return;
        }

        var dt = new DataType(editor, _serializer) { Key = key, Name = name, Configuration = config };
        _dataTypeService.Save(dt);
    }

    private void EnsureRichText(Guid key, string name)
    {
        if (Exists(key)) return;
        if (!_propertyEditors.TryGet("Umbraco.TinyMCE", out var editor)) return;

        var dt = new DataType(editor, _serializer) { Key = key, Name = name };
        _dataTypeService.Save(dt);
    }

    private void EnsureMediaPicker(Guid key, string name)
    {
        if (Exists(key)) return;
        if (!_propertyEditors.TryGet("Umbraco.MediaPicker3", out var editor)) return;

        var dt = new DataType(editor, _serializer)
        {
            Key  = key,
            Name = name,
            Configuration = new MediaPicker3Configuration
            {
                Multiple              = false,
                EnableLocalFocalPoint = true
            }
        };
        _dataTypeService.Save(dt);
    }

    private void EnsureMultiUrlPicker(Guid key, string name)
    {
        if (Exists(key)) return;
        if (!_propertyEditors.TryGet("Umbraco.MultiUrlPicker", out var editor)) return;

        var dt = new DataType(editor, _serializer)
        {
            Key           = key,
            Name          = name,
            Configuration = new MultiUrlPickerConfiguration { MaxNumber = 1 }
        };
        _dataTypeService.Save(dt);
    }

    private void EnsureTags(Guid key, string name)
    {
        if (Exists(key)) return;
        if (!_propertyEditors.TryGet("Umbraco.Tags", out var editor)) return;

        var dt = new DataType(editor, _serializer)
        {
            Key  = key,
            Name = name,
            Configuration = new TagConfiguration
            {
                Group       = "default",
                StorageType = TagsStorageType.Json
            }
        };
        _dataTypeService.Save(dt);
    }

    private void EnsureBlockListEmpty(Guid key, string name)
    {
        if (Exists(key)) return;
        if (!_propertyEditors.TryGet("Umbraco.BlockList", out var editor)) return;

        // Sin bloques definidos — se configuran desde el backoffice
        // una vez que los Element Types estén creados.
        var dt = new DataType(editor, _serializer)
        {
            Key  = key,
            Name = name,
            Configuration = new BlockListConfiguration
            {
                Blocks = Array.Empty<BlockListConfiguration.BlockConfiguration>()
            }
        };
        _dataTypeService.Save(dt);
    }

    private void EnsureInteger(Guid key, string name)
    {
        if (Exists(key)) return;
        if (!_propertyEditors.TryGet("Umbraco.Integer", out var editor)) return;

        var dt = new DataType(editor, _serializer) { Key = key, Name = name };
        _dataTypeService.Save(dt);
    }

    private void EnsureContentPicker(Guid key, string name)
    {
        if (Exists(key)) return;
        if (!_propertyEditors.TryGet("Umbraco.ContentPicker", out var editor)) return;

        var dt = new DataType(editor, _serializer) { Key = key, Name = name };
        _dataTypeService.Save(dt);
    }

    private void EnsureContentPickerMulti(Guid key, string name)
    {
        if (Exists(key)) return;
        if (!_propertyEditors.TryGet("Umbraco.MultiNodeTreePicker", out var editor)) return;

        var dt = new DataType(editor, _serializer)
        {
            Key  = key,
            Name = name,
            Configuration = new MultiNodePickerConfiguration
            {
                MaxNumber = 0,  // 0 = unlimited
                MinNumber = 0
            }
        };
        _dataTypeService.Save(dt);
    }

    /// <summary>
    /// Creates (or patches) the Page Tags picker filtered to the pageTag content type.
    /// </summary>
    private void EnsurePageTagsPicker()
    {
        const string pageTagAlias = ContentTypeKeys.Aliases.PageTag;

        var existing = _dataTypeService.GetDataType(DataTypeKeys.PageTagsPicker);
        if (existing is not null)
        {
            if (existing.Configuration is MultiNodePickerConfiguration cfg &&
                !string.Equals(cfg.Filter, pageTagAlias, StringComparison.Ordinal))
            {
                cfg.Filter = pageTagAlias;
                _dataTypeService.Save(existing);
            }
            return;
        }

        if (!_propertyEditors.TryGet("Umbraco.MultiNodeTreePicker", out var editor)) return;

        var dt = new DataType(editor, _serializer)
        {
            Key  = DataTypeKeys.PageTagsPicker,
            Name = "Content Picker - Page Tags",
            Configuration = new MultiNodePickerConfiguration
            {
                Filter    = pageTagAlias,
                MaxNumber = 0,
                MinNumber = 0
            }
        };
        _dataTypeService.Save(dt);
    }

    private bool Exists(Guid key) => _dataTypeService.GetDataType(key) is not null;
}
