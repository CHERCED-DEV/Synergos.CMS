using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.PropertyEditors;
using Umbraco.Cms.Core.Serialization;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;
using Synergos.CMS.Configuration;
using Synergos.CMS.Schema.Constants;
using Synergos.CMS.Schema.Initializers;
using Synergos.CMS.Schema.Seeders;

namespace Synergos.CMS.Schema;

/// <summary>
/// Wires the schema initialization pipeline into Umbraco startup.
/// Registered as a Composer so Umbraco discovers it automatically via AddComposers().
/// </summary>
public sealed class SynergosSchemaComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.AddNotificationHandler<UmbracoApplicationStartedNotification, SynergosSchemaInitializer>();
    }
}

/// <summary>
/// Aggregates all schema pipeline dependencies into a single record to keep constructor
/// parameter counts under the Umbraco DI limit and simplify testing.
/// </summary>
internal sealed record SchemaServices(
    IDataTypeService DataTypes,
    IContentTypeService ContentTypes,
    IMediaTypeService MediaTypes,
    IFileService Files,
    IShortStringHelper ShortString,
    PropertyEditorCollection PropertyEditors,
    IConfigurationEditorJsonSerializer Serializer);

/// <summary>
/// Aggregates all seeder dependencies into a single record.
/// </summary>
internal sealed record SeedDependencies(
    IContentService Content,
    IContentTypeService ContentTypes,
    IFileService Files,
    IOptions<SeedConfig> SeedConfigOptions,
    ILoggerFactory LoggerFactory);

/// <summary>
/// Orchestrates all schema phase initializers in strict dependency order.
/// Bump <see cref="SchemaVersion.Value"/> to force a full re-run on next startup.
/// </summary>
internal sealed class SynergosSchemaInitializer
    : INotificationHandler<UmbracoApplicationStartedNotification>
{
    private readonly IKeyValueService _keyValueService;
    private readonly SchemaServices _schema;
    private readonly SeedDependencies _seed;
    private readonly IMacroService _macroService;
    private readonly ILocalizationService _localization;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<SynergosSchemaInitializer> _logger;

    public SynergosSchemaInitializer(
        IKeyValueService keyValueService,
        SchemaServices schema,
        SeedDependencies seed,
        IMacroService macroService,
        ILocalizationService localization,
        IWebHostEnvironment env,
        ILogger<SynergosSchemaInitializer> logger)
    {
        _keyValueService = keyValueService;
        _schema = schema;
        _seed = seed;
        _macroService = macroService;
        _localization = localization;
        _env = env;
        _logger = logger;
    }

    public void Handle(UmbracoApplicationStartedNotification notification)
    {
        var currentVersion = _keyValueService.GetValue(SchemaVersion.Key);
        if (currentVersion == SchemaVersion.Value)
        {
            _logger.LogDebug("Synergos schema up to date ({Version}).", SchemaVersion.Value);
            EnsureContentVariations(); // guard: uSync or partial runs may reset Variations
            RunSeeder();
            return;
        }

        _logger.LogInformation(
            "Synergos schema mismatch (stored={Stored}, expected={Expected}). Running pipeline.",
            currentVersion ?? "none", SchemaVersion.Value);

        try
        {
            // Phase 0 — languages must exist before any ContentVariation.Culture patch
            new CultureInitializer(_localization).Initialize();
            // Phase 1a — all Data Types
            new DataTypeInitializer(_schema.DataTypes, _schema.PropertyEditors, _schema.Serializer).Initialize();
            // Phase 1b — core compositions (Lifecycle, Base, Ownership, Tenant, Access, Versioning, Audit)
            new CompositionInitializer(_schema.ContentTypes, _schema.DataTypes, _schema.ShortString).Initialize();
            // Phase 2 — content compositions (Heading, Text, Media, Cta, Badge, Collection, Author, Date, Metadata, Embed)
            new ContentCompositionInitializer(_schema.ContentTypes, _schema.DataTypes, _schema.ShortString).Initialize();
            // Phase 3 — tagging composition
            new TaggingCompositionInitializer(_schema.ContentTypes, _schema.DataTypes, _schema.ShortString).Initialize();
            // Phase 4 — DOM compositions (Class, Attributes, Layout, Spacing, Visibility, Variant, LayoutPreset, LayoutProfile)
            new DomCompositionInitializer(_schema.ContentTypes, _schema.DataTypes, _schema.ShortString).Initialize();
            // Phase 5 — behavior compositions (Tracking, Interaction, Navigation, FeatureFlag, Async, Script)
            new BehaviorCompositionInitializer(_schema.ContentTypes, _schema.DataTypes, _schema.ShortString).Initialize();
            // Phase 6a — SEO composition
            new SeoCompositionInitializer(_schema.ContentTypes, _schema.DataTypes, _schema.ShortString).Initialize();
            // Phase 6b — integration compositions (Integration, AngularMount, MfMount)
            new IntegrationCompositionInitializer(_schema.ContentTypes, _schema.DataTypes, _schema.ShortString).Initialize();
            // Phase 6c — visibility composition
            new VisibilityCompositionInitializer(_schema.ContentTypes, _schema.DataTypes, _schema.ShortString).Initialize();
            // Phase 6d — ensure all compositions have IsElement = true
            PatchCompositionsIsElement();
            // Phase 7 — element types (structural, textual, action, media, informational, composition, integration, corporate, etc.)
            new ElementTypeInitializer(_schema.ContentTypes, _schema.DataTypes, _schema.ShortString).Initialize();
            // Phase 7.5 — patch BlockListMountParams to include ElementMountParam
            PatchMountParamsBlockList();
            // Phase 8 — media types
            new MediaTypeInitializer(_schema.MediaTypes, _schema.DataTypes, _schema.ShortString).Initialize();
            // Phase 9a — core document types (SiteRoot, PageBase) + shop types
            new DocumentTypeInitializer(
                _schema.ContentTypes,
                _schema.DataTypes,
                _schema.Files,
                _schema.ShortString,
                _schema.PropertyEditors,
                _schema.Serializer,
                _env.ContentRootPath).Initialize();
            // Phase 9b — platform infrastructure (PlatformRoot, SiteSettings, ThemeSettings, GlobalSettings, etc.)
            new PlatformInitializer(
                _schema.ContentTypes,
                _schema.DataTypes,
                _schema.Files,
                _schema.ShortString,
                _schema.PropertyEditors,
                _schema.Serializer).Initialize();
            // Phase 9c — taxonomy (PageTag, PageTagsFolder)
            new TaxonomyInitializer(_schema.ContentTypes, _schema.DataTypes, _schema.ShortString).Initialize();
            // Phase 9d — blog document types (BlogHome, BlogPost — Category is created by PlatformInitializer)
            new BlogInitializer(_schema.ContentTypes, _schema.DataTypes, _schema.Files, _schema.ShortString).Initialize();
            // Phase 10 — macros
            new MacroInitializer(_macroService, _schema.ShortString).Initialize();
            // Phase 11 — dictionary
            new DictionaryInitializer(_localization).Initialize();
            // Phase 12 — Flow Engine document types
            new FlowEngineInitializer(_schema.ContentTypes, _schema.DataTypes, _schema.ShortString).Initialize();

            _keyValueService.SetValue(SchemaVersion.Key, SchemaVersion.Value);
            _logger.LogInformation("Synergos schema complete ({Version}).", SchemaVersion.Value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Synergos schema initialization failed. CMS may be in partial state.");
            return;
        }

        EnsureContentVariations(); // guard: re-assert after full pipeline
        RunSeeder();
    }

    /// <summary>
    /// Ensures all compositions have IsElement = true (idempotent batch patch).
    /// </summary>
    private void PatchCompositionsIsElement()
    {
        Guid[] compositionKeys =
        [
            // Core
            ContentTypeKeys.CompCoreLifecycle, ContentTypeKeys.CompCoreBase,
            ContentTypeKeys.CompCoreOwnership, ContentTypeKeys.CompCoreTenant,
            ContentTypeKeys.CompCoreAccess, ContentTypeKeys.CompCoreVersioning,
            ContentTypeKeys.CompCoreAudit,
            // Content
            ContentTypeKeys.CompContentHeading, ContentTypeKeys.CompContentText,
            ContentTypeKeys.CompContentMedia, ContentTypeKeys.CompContentCta,
            ContentTypeKeys.CompContentBadge, ContentTypeKeys.CompContentCollection,
            ContentTypeKeys.CompContentAuthor, ContentTypeKeys.CompContentDate,
            ContentTypeKeys.CompContentMetadata, ContentTypeKeys.CompContentEmbed,
            ContentTypeKeys.CompContentPricing, ContentTypeKeys.CompContentLocation,
            // Taxonomy
            ContentTypeKeys.CompTagging,
            // DOM
            ContentTypeKeys.CompDomClass, ContentTypeKeys.CompDomAttributes,
            ContentTypeKeys.CompDomLayout, ContentTypeKeys.CompDomSpacing,
            ContentTypeKeys.CompDomVisibility, ContentTypeKeys.CompDomVariant,
            // Behavior
            ContentTypeKeys.CompBehaviorTracking, ContentTypeKeys.CompBehaviorInteraction,
            ContentTypeKeys.CompBehaviorNavigation, ContentTypeKeys.CompBehaviorFeatureFlag,
            ContentTypeKeys.CompBehaviorAsync, ContentTypeKeys.CompBehaviorScript,
            // Seo / Integration / Visibility
            ContentTypeKeys.CompSeo, ContentTypeKeys.CompIntegration,
            ContentTypeKeys.CompAngularMount, ContentTypeKeys.CompMfMount,
            ContentTypeKeys.CompVisibility,
        ];

        var dirty = new List<IContentType>();
        foreach (var key in compositionKeys)
        {
            var ct = _schema.ContentTypes.Get(key);
            if (ct is null || ct.IsElement) continue;
            ct.IsElement = true;
            dirty.Add(ct);
        }

        if (dirty.Count == 0) return;

        _schema.ContentTypes.Save(dirty);
        _logger.LogInformation("Patched {Count} compositions to IsElement=true.", dirty.Count);
    }

    /// <summary>
    /// Phase 7.5 — configures BlockListMountParams to allow ElementMountParam blocks.
    /// Runs after Phase 7 which creates ElementMountParam.
    /// </summary>
    private void PatchMountParamsBlockList()
    {
        var blockListDt = _schema.DataTypes.GetDataType(DataTypeKeys.BlockListMountParams);
        if (blockListDt?.Configuration is not BlockListConfiguration cfg) return;

        if (cfg.Blocks.Any(b => b.ContentElementTypeKey == ContentTypeKeys.ElementMountParam)) return;

        cfg.Blocks =
        [
            new BlockListConfiguration.BlockConfiguration
            {
                ContentElementTypeKey = ContentTypeKeys.ElementMountParam,
                Label                 = "{{paramKey}}: {{paramValue}}"
            }
        ];
        _schema.DataTypes.Save(blockListDt);
    }

    /// <summary>
    /// Unconditional guard: ensures culture-variant content types have
    /// ContentVariation.Culture set on every startup.
    /// Protects against uSync imports or partial schema runs resetting Variations.
    /// </summary>
    private void EnsureContentVariations()
    {
        PatchTypeVariation(ContentTypeKeys.PageBase,
            "pageTitle", "pageSubtitle", "pageSections");

        PatchTypeVariation(ContentTypeKeys.BlogHome,
            "pageTitle", "pageSubtitle", "pageSections");

        PatchTypeVariation(ContentTypeKeys.BlogPost,
            "postTitle", "postSubtitle", "postBody", "postExcerpt",
            "featuredImage", "postAuthor", "readingTime",
            "postTags", "relatedPosts", "pageSections");
    }

    private void PatchTypeVariation(Guid key, params string[] propertyAliases)
    {
        var ct = _schema.ContentTypes.Get(key);
        if (ct is null) return;

        var dirty = false;
        if (ct.Variations != ContentVariation.Culture)
        {
            ct.Variations = ContentVariation.Culture;
            dirty = true;
        }
        foreach (var alias in propertyAliases)
        {
            var prop = ct.PropertyTypes.FirstOrDefault(p => p.Alias == alias);
            if (prop is null || prop.Variations == ContentVariation.Culture) continue;
            prop.Variations = ContentVariation.Culture;
            dirty = true;
        }
        if (!dirty) return;

        _schema.ContentTypes.Save(ct);
        _logger.LogWarning(
            "EnsureContentVariations: '{Alias}' ContentVariation was reset externally — re-patched to Culture.",
            ct.Alias);
    }

    private void RunSeeder()
    {
        if (!_seed.SeedConfigOptions.Value.Enabled)
        {
            _logger.LogInformation("Synergos seeders disabled (Synergos:Seed:Enabled=false). Skipping content seeding.");
            return;
        }

        try
        {
            new ContentSeeder(
                _seed.Content,
                _seed.ContentTypes,
                _seed.Files,
                _seed.LoggerFactory.CreateLogger<ContentSeeder>(),
                _seed.SeedConfigOptions.Value).Seed();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ContentSeeder failed. Create SiteRoot manually in backoffice.");
        }

        try
        {
            new PageBlockGridSeeder(_seed.Content, _logger).SeedAll();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PageBlockGridSeeder failed.");
        }

        // FlowDemoSiteSeeder must run first — creates the 'Flow Engine Demo' siteRoot
        // that FlowEngineDemoSeeder needs as parent for FlowSettingsRoot.
        try
        {
            new FlowDemoSiteSeeder(_seed.Content, _seed.ContentTypes, _seed.Files, _logger).Seed();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FlowDemoSiteSeeder failed. Create demo site manually in backoffice.");
        }

        try
        {
            new FlowEngineDemoSeeder(_seed.Content, _seed.ContentTypes, _logger).Seed();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FlowEngineDemoSeeder failed. Create FlowDefinitions manually in backoffice.");
        }
    }
}
