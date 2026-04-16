using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.PropertyEditors;
using Umbraco.Cms.Core.Serialization;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;
using Synergos.CMS.Schema.Constants;

namespace Synergos.CMS.Schema.Initializers;

/// <summary>
/// Phase 8b — Platform infrastructure Document Types.
///
/// Creates the multi-site scaffolding:
///   PlatformRoot → SiteRoot(s) → pages
///                → GlobalSettings
///                → SharedContentFolder → ReusableBlock, NavigationGroup, Author, Category, FormDefinition
///
/// Settings hierarchy:
///   GlobalSettings  (platform-wide defaults)
///   SiteSettings    (per-site overrides, child of SiteRoot)
///   ThemeSettings   (per-site branding, child of SiteRoot)
///
/// Also creates:
///   NavigationItem  (element type for nav block list)
///   FormField       (element type for form block list)
///   + BlockList DataTypes: NavItems, FormFields, ReusableBlock (depend on element types above)
/// </summary>
internal sealed class PlatformInitializer : SchemaInitializerBase
{
    private readonly IFileService                     _fs;
    private readonly PropertyEditorCollection         _editors;
    private readonly IConfigurationEditorJsonSerializer _serializer;

    public PlatformInitializer(
        IContentTypeService cts,
        IDataTypeService dts,
        IFileService fs,
        IShortStringHelper ssh,
        PropertyEditorCollection editors,
        IConfigurationEditorJsonSerializer serializer)
        : base(cts, dts, ssh)
    {
        _fs         = fs;
        _editors    = editors;
        _serializer = serializer;
    }

    public override void Initialize()
    {
        var platformFolderId = EnsureRootFolder("Platform");
        var settingsFolderId = EnsureChildFolder(platformFolderId, "Settings");
        var sharedFolderId = EnsureChildFolder(platformFolderId, "Shared");
        var formsFolderId = EnsureRootFolder("Forms");

        // 1. Element Types (NavigationItem, FormField, FormEmbed)
        //    BlogHighlight + ArticleList are owned by ElementTypeInitializer
        //    (was silently skipping here because Phase 7 created them first).
        EnsureNavigationItemElement();
        EnsureFormFieldElement();
        EnsureFormEmbedElement();

        // 3. Block Lists that reference the element types
        EnsureBlockListNavItems();
        EnsureBlockListFormFields();
        EnsureBlockGridReusable();

        // 4. Document Types (order matters: leaf types first)
        EnsureFormDefinition(formsFolderId);
        EnsureNavigationGroup(sharedFolderId);
        EnsureReusableBlock(sharedFolderId);
        EnsureSharedContentFolder(sharedFolderId);
        EnsureAuthor(sharedFolderId);
        EnsureCategory(sharedFolderId);
        // Settings types extracted to SiteSettingsInitializer — see SiteSettingsInitializer.cs
        new SiteSettingsInitializer(Cts, Dts, Ssh, settingsFolderId).Initialize();
        // Layout config tree (HeaderConfig, FooterConfig, AlertBarConfig, BannerConfig + 7 folder types)
        new LayoutConfigInitializer(Cts, Dts, Ssh, settingsFolderId).Initialize();
        EnsurePlatformRoot(platformFolderId);

        // 5. Allowed children
        PatchPlatformAllowedChildren();
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Element Types
    // ══════════════════════════════════════════════════════════════════════════

    private void EnsureNavigationItemElement()
    {
        if (Cts.Get(ContentTypeKeys.ElementNavItem) is not null) return;

        var ct = new ContentType(Ssh, -1)
        {
            Key       = ContentTypeKeys.ElementNavItem,
            Name      = "Navigation Item",
            Alias     = "elementNavItem",
            Icon      = "icon-link",
            IsElement = true
        };

        var tab = Tab("Content", "content", 0);
        tab.PropertyTypes!.Add(Prop("navLabel",       "Label",         DataTypeKeys.TextTitle,     0,  mandatory: true));
        tab.PropertyTypes!.Add(Prop("navLink",        "Link",          DataTypeKeys.LinkUrl,       10, mandatory: true));
        tab.PropertyTypes!.Add(Prop("navIcon",        "Icon",          DataTypeKeys.MediaImage,    20));
        tab.PropertyTypes!.Add(Prop("navHighlighted", "Is Highlighted", DataTypeKeys.ToggleBoolean, 30));

        ct.PropertyGroups.Add(tab);
        Cts.Save(ct);
    }

    private void EnsureFormFieldElement()
    {
        var existing = Cts.Get(ContentTypeKeys.ElementFormField);
        if (existing is not null)
        {
            // Migration guard: ElementTypeInitializer (Phase 6) previously created this type
            // using composition properties instead of explicit form-builder properties.
            // If "fieldLabel" is missing, it was created incorrectly — delete and recreate.
            var hasCorrectProps = existing.PropertyTypes.Any(p => p.Alias == "fieldLabel");
            if (hasCorrectProps) return;
            Cts.Delete(existing);
        }

        var ct = new ContentType(Ssh, -1)
        {
            Key       = ContentTypeKeys.ElementFormField,
            Name      = "Form Field",
            Alias     = "elementFormField",
            Icon      = "icon-autofill",
            IsElement = true
        };

        var tab = Tab("Field", "field", 0);
        tab.PropertyTypes!.Add(Prop("fieldLabel",       "Label",       DataTypeKeys.TextTitle,       0,  mandatory: true));
        tab.PropertyTypes!.Add(Prop("fieldName",        "Name",        DataTypeKeys.TextIdentifier,  10, mandatory: true));
        tab.PropertyTypes!.Add(Prop("fieldType",        "Type",        DataTypeKeys.SelectFieldType, 20, mandatory: true));
        tab.PropertyTypes!.Add(Prop("fieldPlaceholder", "Placeholder", DataTypeKeys.TextTitle,       30));
        tab.PropertyTypes!.Add(Prop("fieldRequired",    "Required",    DataTypeKeys.ToggleBoolean,   40));
        tab.PropertyTypes!.Add(Prop("fieldOptions",     "Options",     DataTypeKeys.TextAreaNotes,   50, description: "One option per line (for select/radio)"));
        tab.PropertyTypes!.Add(Prop("fieldValidation",  "Validation",  DataTypeKeys.TextIdentifier,  60, description: "Regex pattern"));
        tab.PropertyTypes!.Add(Prop("fieldWidth",       "Width",       DataTypeKeys.SelectFieldWidth, 70));

        ct.PropertyGroups.Add(tab);
        Cts.Save(ct);
    }

    /// <summary>
    /// Element.Form.Embed — embeds a FormDefinition in the page Block Grid.
    /// Uses a ContentPicker to reference the FormDefinition node.
    /// Composed with CompDomClass and CompDomVariant for styling.
    /// </summary>
    private void EnsureFormEmbedElement()
    {
        var existing = Cts.Get(ContentTypeKeys.ElementFormEmbed);
        if (existing is not null)
        {
            // Migration guard: same as FormField — if "formPicker" is missing, the type was
            // created by ElementTypeInitializer (Phase 6) with wrong composition structure.
            var hasCorrectProps = existing.PropertyTypes.Any(p => p.Alias == "formPicker");
            if (hasCorrectProps) return;
            Cts.Delete(existing);
        }

        var ct = new ContentType(Ssh, -1)
        {
            Key       = ContentTypeKeys.ElementFormEmbed,
            Name      = "Form Embed",
            Alias     = "elementFormEmbed",
            Icon      = "icon-checkbox",
            IsElement = true
        };

        // Inherit DOM compositions for styling
        ct.ContentTypeComposition = new[]
        {
            ContentTypeKeys.CompDomClass,
            ContentTypeKeys.CompDomVariant
        }
        .Select(k => Cts.Get(k))
        .Where(c => c is not null)
        .Cast<IContentTypeComposition>()
        .ToList();

        var tab = Tab("Form", "form", 0);
        tab.PropertyTypes!.Add(Prop("formPicker",     "Form",           DataTypeKeys.ContentPicker,  0,
            mandatory: true, description: "Pick a FormDefinition from Shared Content"));
        tab.PropertyTypes!.Add(Prop("formHeading",    "Form Heading",   DataTypeKeys.TextTitle,      10,
            description: "Optional heading above the form"));
        tab.PropertyTypes!.Add(Prop("formSubheading", "Form Subheading", DataTypeKeys.TextSubtitle,  20,
            description: "Optional subheading above the form"));
        ct.PropertyGroups.Add(tab);

        Cts.Save(ct);
    }

    /// <summary>
    // BlogHighlight + ArticleList element creation moved to ElementTypeInitializer
    // (Phase 7, with PatchBlogHighlightProps + PatchArticleListProps). The
    // duplicate creators here used to silently skip because Phase 7 created
    // the types first — with wrong compositions and without the picker properties.

    // ══════════════════════════════════════════════════════════════════════════
    // Block Lists referencing new element types
    // ══════════════════════════════════════════════════════════════════════════

    private void EnsureBlockListNavItems()
    {
        if (Dts.GetDataType(DataTypeKeys.BlockListNavItems) is not null) return;
        if (!_editors.TryGet(Umbraco.Cms.Core.Constants.PropertyEditors.Aliases.BlockList, out var editor)) return;

        var config = new BlockListConfiguration
        {
            Blocks = new[]
            {
                new BlockListConfiguration.BlockConfiguration
                {
                    ContentElementTypeKey = ContentTypeKeys.ElementNavItem
                }
            }
        };
        var dt = new DataType(editor, _serializer) { Key = DataTypeKeys.BlockListNavItems, Name = "DT.BlockList.NavItems", Configuration = config };
        Dts.Save(dt);
    }

    private void EnsureBlockListFormFields()
    {
        if (Dts.GetDataType(DataTypeKeys.BlockListFormFields) is not null) return;
        if (!_editors.TryGet(Umbraco.Cms.Core.Constants.PropertyEditors.Aliases.BlockList, out var editor)) return;

        var config = new BlockListConfiguration
        {
            Blocks = new[]
            {
                new BlockListConfiguration.BlockConfiguration
                {
                    ContentElementTypeKey = ContentTypeKeys.ElementFormField
                }
            }
        };
        var dt = new DataType(editor, _serializer) { Key = DataTypeKeys.BlockListFormFields, Name = "DT.BlockList.FormFields", Configuration = config };
        Dts.Save(dt);
    }

    private void EnsureBlockGridReusable()
    {
        // Reusable blocks share the same Block Grid config as page sections
        // This is intentional — reusable blocks can contain any element
        // Copy the page sections block grid config
        var source = Dts.GetDataType(DataTypeKeys.BlockGridPageSections);
        if (source is null) return;
        if (!_editors.TryGet(Umbraco.Cms.Core.Constants.PropertyEditors.Aliases.BlockGrid, out var editor)) return;

        var existing = Dts.GetDataType(DataTypeKeys.BlockGridReusable);
        if (existing is not null)
        {
            existing.Configuration = source.Configuration;
            Dts.Save(existing);
            return;
        }

        var dt = new DataType(editor, _serializer)
        {
            Key           = DataTypeKeys.BlockGridReusable,
            Name          = "DT.BlockGrid.Reusable",
            Configuration = source.Configuration
        };
        Dts.Save(dt);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Document Types — Leaf types first
    // ══════════════════════════════════════════════════════════════════════════

    private void EnsureFormDefinition(int folderId)
    {
        const string description = "Definición de formulario. Configura campos, mensajes y opciones de integración.";
        if (TryPatchExistingContentType(ContentTypeKeys.FormDefinition, "Form Definition", folderId, description, "icon-checkbox")) return;

        var ct = new ContentType(Ssh, folderId)
        {
            Key   = ContentTypeKeys.FormDefinition,
            Name  = "Form Definition",
            Description = description,
            Alias = ContentTypeKeys.Aliases.FormDefinition,
            Icon  = "icon-checkbox"
        };

        var tab = Tab("Form", "form", 0);
        tab.PropertyTypes!.Add(Prop("formName",        "Form Name",      DataTypeKeys.TextTitle,          0, mandatory: true));
        tab.PropertyTypes!.Add(Prop("formAlias",       "Alias",          DataTypeKeys.TextIdentifier,     10, mandatory: true));
        tab.PropertyTypes!.Add(Prop("formFields",      "Fields",         DataTypeKeys.BlockListFormFields, 20));
        tab.PropertyTypes!.Add(Prop("submitLabel",     "Submit Label",   DataTypeKeys.TextTitle,          30));
        tab.PropertyTypes!.Add(Prop("recipientEmail",  "Recipient Email", DataTypeKeys.TextTitle,         40));
        tab.PropertyTypes!.Add(Prop("webhookUrl",      "Webhook URL",    DataTypeKeys.TextUrl,            50));
        ct.PropertyGroups.Add(tab);

        var tabTy = Tab("Thank You", "thankYou", 1);
        tabTy.PropertyTypes!.Add(Prop("thankYouMessage",     "Thank You Message", DataTypeKeys.RichTextBody, 0));
        tabTy.PropertyTypes!.Add(Prop("thankYouRedirectUrl", "Redirect URL",      DataTypeKeys.LinkUrl,      10));
        ct.PropertyGroups.Add(tabTy);

        var tabInt = Tab("Integration", "integration", 2);
        tabInt.PropertyTypes!.Add(Prop("formIntegrationProvider", "Provider",       DataTypeKeys.SelectIntegrationProvider, 0));
        tabInt.PropertyTypes!.Add(Prop("formIntegrationId",      "Integration ID", DataTypeKeys.TextIdentifier,            10));
        ct.PropertyGroups.Add(tabInt);

        Cts.Save(ct);
    }

    private void EnsureNavigationGroup(int folderId)
    {
        const string description = "Conjunto reutilizable de enlaces para cabeceras, pies o menús secundarios.";
        if (TryPatchExistingContentType(ContentTypeKeys.NavigationGroup, "Navigation Group", folderId, description, "icon-bulleted-list")) return;

        var ct = new ContentType(Ssh, folderId)
        {
            Key   = ContentTypeKeys.NavigationGroup,
            Name  = "Navigation Group",
            Description = description,
            Alias = ContentTypeKeys.Aliases.NavigationGroup,
            Icon  = "icon-bulleted-list"
        };

        var tab = Tab("Navigation", "navigation", 0);
        tab.PropertyTypes!.Add(Prop("groupName",  "Group Name", DataTypeKeys.TextTitle,         0, mandatory: true));
        tab.PropertyTypes!.Add(Prop("groupAlias", "Alias",      DataTypeKeys.TextIdentifier,    10));
        tab.PropertyTypes!.Add(Prop("navItems",   "Items",      DataTypeKeys.BlockListNavItems, 20));
        ct.PropertyGroups.Add(tab);

        Cts.Save(ct);
    }

    private void EnsureReusableBlock(int folderId)
    {
        const string description = "Bloque reutilizable que puede insertarse desde distintas áreas del sitio.";
        if (TryPatchExistingContentType(ContentTypeKeys.ReusableBlock, "Reusable Block", folderId, description, "icon-layers-alt")) return;

        var ct = new ContentType(Ssh, folderId)
        {
            Key   = ContentTypeKeys.ReusableBlock,
            Name  = "Reusable Block",
            Description = description,
            Alias = ContentTypeKeys.Aliases.ReusableBlock,
            Icon  = "icon-layers-alt"
        };

        var tab = Tab("Content", "content", 0);
        tab.PropertyTypes!.Add(Prop("blockName",    "Block Name",    DataTypeKeys.TextTitle,          0, mandatory: true));
        tab.PropertyTypes!.Add(Prop("blockAlias",   "Alias",         DataTypeKeys.TextIdentifier,     10));
        tab.PropertyTypes!.Add(Prop("blockContent", "Block Content", DataTypeKeys.BlockGridReusable,  20,
            description: "Compose this reusable block with any elements."));
        ct.PropertyGroups.Add(tab);

        Cts.Save(ct);
    }

    private void EnsureSharedContentFolder(int folderId)
    {
        const string description = "Carpeta contenedora para contenido compartido del sitio: bloques reutilizables, grupos de navegación, autores y configuración de layout. No es navegable desde la web — solo organiza contenido editorial.";

        var existing = Cts.Get(ContentTypeKeys.SharedContentFolder);
        if (existing is not null)
        {
            var dirty = false;
            if (!string.Equals(existing.Name, "Shared Content Folder", StringComparison.Ordinal))
            { existing.Name = "Shared Content Folder"; dirty = true; }
            if (existing.ParentId != folderId)
            { existing.ParentId = folderId; dirty = true; }
            if (!string.Equals(existing.Description, description, StringComparison.Ordinal))
            { existing.Description = description; dirty = true; }

            // v11.0.1 — ensure no template is assigned. Prior migrations left a
            // TemplateId pointing at a missing .cshtml file, causing Umbraco to
            // route /<site>/config/ to a broken render instead of treating the
            // folder as a non-routable container.
            if (existing.AllowedTemplates?.Any() == true)
            { existing.AllowedTemplates = []; dirty = true; }
            if (existing.DefaultTemplate is not null)
            { existing.SetDefaultTemplate(null); dirty = true; }

            if (dirty) Cts.Save(existing);
            return;
        }

        var ct = new ContentType(Ssh, folderId)
        {
            Key   = ContentTypeKeys.SharedContentFolder,
            Name  = "Shared Content Folder",
            Description = description,
            Alias = ContentTypeKeys.Aliases.SharedContentFolder,
            Icon  = "icon-folder"
        };

        Cts.Save(ct);
    }

    private void EnsureAuthor(int folderId)
    {
        const string description = "Perfil reutilizable de autor para artículos y contenido editorial.";
        if (TryPatchExistingContentType(ContentTypeKeys.Author, "Author", folderId, description, "icon-user")) return;

        var ct = new ContentType(Ssh, folderId)
        {
            Key   = ContentTypeKeys.Author,
            Name  = "Author",
            Description = description,
            Alias = ContentTypeKeys.Aliases.Author,
            Icon  = "icon-user"
        };

        var tab = Tab("Profile", "profile", 0);
        tab.PropertyTypes!.Add(Prop("authorName",     "Name",     DataTypeKeys.TextTitle,    0, mandatory: true));
        tab.PropertyTypes!.Add(Prop("authorRole",     "Role",     DataTypeKeys.TextSubtitle, 10));
        tab.PropertyTypes!.Add(Prop("authorBio",      "Bio",      DataTypeKeys.TextAreaNotes, 20));
        tab.PropertyTypes!.Add(Prop("authorPhoto",    "Photo",    DataTypeKeys.MediaImage,   30));
        tab.PropertyTypes!.Add(Prop("authorEmail",    "Email",    DataTypeKeys.TextTitle,    40));
        tab.PropertyTypes!.Add(Prop("authorLinkedin", "LinkedIn", DataTypeKeys.TextUrl,      50));
        tab.PropertyTypes!.Add(Prop("authorTwitter",  "Twitter",  DataTypeKeys.TextUrl,      60));
        ct.PropertyGroups.Add(tab);

        Cts.Save(ct);
    }

    private void EnsureCategory(int folderId)
    {
        const string description = "Taxonomía reutilizable para clasificar contenido.";

        var existing = Cts.Get(ContentTypeKeys.Category);
        if (existing is not null)
        {
            var dirty = false;
            if (!string.Equals(existing.Name, "Category", StringComparison.Ordinal)) { existing.Name = "Category"; dirty = true; }
            if (existing.ParentId != folderId) { existing.ParentId = folderId; dirty = true; }
            if (!string.Equals(existing.Description, description, StringComparison.Ordinal)) { existing.Description = description; dirty = true; }
            dirty |= PatchIcon(existing, "icon-categories");

            // Enable culture variation for translatable category fields.
            dirty |= PatchCultureVariation(existing, "categoryName", "categoryDescription");

            if (dirty) Cts.Save(existing);
            return;
        }

        var ct = new ContentType(Ssh, folderId)
        {
            Key        = ContentTypeKeys.Category,
            Name       = "Category",
            Description = description,
            Alias      = ContentTypeKeys.Aliases.Category,
            Icon       = "icon-categories",
            Variations = ContentVariation.Culture
        };

        var tab = Tab("Category", "category", 0);

        var nameP = Prop("categoryName",        "Name",        DataTypeKeys.TextTitle,      0, mandatory: true);
        nameP.Variations = ContentVariation.Culture;
        tab.PropertyTypes!.Add(nameP);

        tab.PropertyTypes!.Add(Prop("categorySlug",  "Slug", DataTypeKeys.TextIdentifier, 10));

        var descP = Prop("categoryDescription", "Description", DataTypeKeys.TextAreaNotes, 20);
        descP.Variations = ContentVariation.Culture;
        tab.PropertyTypes!.Add(descP);

        tab.PropertyTypes!.Add(Prop("categoryImage", "Image", DataTypeKeys.MediaImage,    30));
        tab.PropertyTypes!.Add(Prop("categoryColor", "Color", DataTypeKeys.TextHexColor,  40, description: "Hex color for badge, e.g. #1A73E8"));
        ct.PropertyGroups.Add(tab);

        Cts.Save(ct);
    }

    // ── PlatformRoot ──────────────────────────────────────────────────────────

    private void EnsurePlatformRoot(int folderId)
    {
        var template = EnsureTemplate("Platform Root", "PlatformRoot");
        const string description = "Nodo raíz de la plataforma multi-sitio. Contiene sitios y configuración compartida.";

        var existing = Cts.Get(ContentTypeKeys.PlatformRoot);
        if (existing is not null)
        {
            var dirty = false;
            if (!string.Equals(existing.Name, "Platform Root", StringComparison.Ordinal))
            { existing.Name = "Platform Root"; dirty = true; }
            if (!string.Equals(existing.Description, description, StringComparison.Ordinal))
            { existing.Description = description; dirty = true; }
            if (existing.ParentId != folderId)
            { existing.ParentId = folderId; dirty = true; }
            dirty |= PatchIcon(existing, "icon-globe");
            if (existing.AllowedTemplates?.Any(t => t.Id == template.Id) != true)
            { existing.AllowedTemplates = new[] { template }; existing.SetDefaultTemplate(template); dirty = true; }
            if (dirty) Cts.Save(existing);
            return;
        }

        var ct = new ContentType(Ssh, folderId)
        {
            Key              = ContentTypeKeys.PlatformRoot,
            Name             = "Platform Root",
            Description      = description,
            Alias            = ContentTypeKeys.Aliases.PlatformRoot,
            Icon             = "icon-globe",
            AllowedAsRoot    = true,
            AllowedTemplates = new[] { template }
        };
        ct.SetDefaultTemplate(template);

        var tab = Tab("Platform", "platform", 0);
        tab.PropertyTypes!.Add(Prop("platformName", "Platform Name", DataTypeKeys.TextTitle, 0, mandatory: true));
        ct.PropertyGroups.Add(tab);

        Cts.Save(ct);
    }

    private ITemplate EnsureTemplate(string name, string alias)
    {
        var existing = _fs.GetTemplate(alias);
        if (existing is not null) return existing;

        var template = new Template(Ssh, name, alias);
        _fs.SaveTemplate(template);
        return _fs.GetTemplate(alias)!;
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Allowed children
    // ══════════════════════════════════════════════════════════════════════════

    private void PatchPlatformAllowedChildren()
    {
        // PlatformRoot → SiteRoot, GlobalSettings, SharedContentFolder
        SetAllowedChildren(ContentTypeKeys.PlatformRoot,
            ContentTypeKeys.SiteRoot,
            ContentTypeKeys.GlobalSettings,
            ContentTypeKeys.SharedContentFolder);

        // SiteRoot → PageBase, SiteSettings, ThemeSettings, BlogHome, LayoutProfile, SharedContentFolder (Config folder)
        SetAllowedChildren(ContentTypeKeys.SiteRoot,
            ContentTypeKeys.PageBase,
            ContentTypeKeys.SiteSettings,
            ContentTypeKeys.ThemeSettings,
            ContentTypeKeys.BlogHome,
            ContentTypeKeys.LayoutProfile,
            ContentTypeKeys.SharedContentFolder);

        // SharedContentFolder (also reused as per-site Config/) → legacy children + Layout/Navigation containers
        // Note: Category moved to BlogHome children (BlogInitializer manages Category→BlogPost hierarchy).
        SetAllowedChildren(ContentTypeKeys.SharedContentFolder,
            ContentTypeKeys.ReusableBlock,
            ContentTypeKeys.NavigationGroup,
            ContentTypeKeys.Author,
            ContentTypeKeys.FormDefinition,
            ContentTypeKeys.LayoutFolder,
            ContentTypeKeys.NavigationFolder);
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

    // TryPatchExistingContentType is inherited from SchemaInitializerBase.
}

