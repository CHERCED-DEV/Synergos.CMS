using Microsoft.Extensions.DependencyInjection;
using Synergos.CMS.Application.Blog;
using Synergos.CMS.Application.Orchestration;
using Synergos.CMS.Application.Mapping;
using Synergos.CMS.Application.Mapping.Elements;
using Synergos.CMS.Application.Services;
using Synergos.CMS.Application.Mapping.Compositions.Behavior;
using Synergos.CMS.Application.Mapping.Compositions.Content;
using Synergos.CMS.Application.Mapping.Compositions.Dom;
using Synergos.CMS.Application.Mapping.Compositions.Seo;
using Synergos.CMS.Application.Mapping.Compositions.Visibility;
using Synergos.CMS.Application.Rendering;
using Synergos.CMS.Domain.Orchestration;
using Synergos.CMS.Domain.Services;

namespace Synergos.CMS.Application;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers all Synergos CMS application services:
    /// composition readers, element mappers, assemblers, and render engine.
    /// </summary>
    public static IServiceCollection AddSynergosApplication(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddSynergosCompositions();
        services.AddSynergosMappers();
        services.AddSynergosRendering();
        services.AddSynergosBlog();
        services.AddSynergosForms();
        services.AddSynergosOrchestration();
        // IDictionaryCache implementation is framework-specific — registered from Program.cs
        // alongside the other UmbracoXxxService implementations to preserve Clean Architecture
        // (Application must not know Infrastructure types).
        return services;
    }

    /// <summary>
    /// Blog services — BlogService (Singleton) + BlogAssembler (Transient).
    /// </summary>
    public static IServiceCollection AddSynergosBlog(this IServiceCollection services)
    {
        services.AddSingleton<BlogService>();
        services.AddTransient<BlogAssembler>();
        return services;
    }

    /// <summary>
    /// Forms — FormRenderService is registered in AddSynergosRendering.
    /// IFormEmailService implementation is framework-specific — registered from Program.cs.
    /// </summary>
    public static IServiceCollection AddSynergosForms(this IServiceCollection services)
    {
        return services;
    }

    /// <summary>
    /// Composition readers — stateless singletons that read Umbraco composition properties.
    /// </summary>
    public static IServiceCollection AddSynergosCompositions(this IServiceCollection services)
    {
        // Content
        services.AddSingleton<ContentHeadingReader>();
        services.AddSingleton<ContentTextReader>();
        services.AddSingleton<ContentMediaReader>();
        services.AddSingleton<ContentCtaReader>();
        services.AddSingleton<ContentBadgeReader>();
        services.AddSingleton<ContentCollectionReader>();
        services.AddSingleton<ContentAuthorReader>();
        services.AddSingleton<ContentDateReader>();
        services.AddSingleton<ContentMetadataReader>();
        services.AddSingleton<ContentEmbedReader>();

        // Dom
        services.AddSingleton<DomClassReader>();
        services.AddSingleton<DomAttributesReader>();
        services.AddSingleton<DomLayoutReader>();
        services.AddSingleton<DomSpacingReader>();
        services.AddSingleton<DomVisibilityReader>();
        services.AddSingleton<DomVariantReader>();

        // Behavior
        services.AddSingleton<BehaviorTrackingReader>();
        services.AddSingleton<BehaviorInteractionReader>();
        services.AddSingleton<BehaviorNavigationReader>();
        services.AddSingleton<BehaviorFeatureFlagReader>();
        services.AddSingleton<BehaviorAsyncReader>();
        services.AddSingleton<BehaviorScriptReader>();

        // Cross-cutting
        services.AddSingleton<SeoReader>();
        services.AddSingleton<VisibilityReader>();

        return services;
    }

    /// <summary>
    /// ISectionMapper implementations — one per element type, resolved by alias via dispatcher.
    /// </summary>
    public static IServiceCollection AddSynergosMappers(this IServiceCollection services)
    {
        // Structural (c3*)
        services.AddSingleton<ISectionMapper, SectionStructMapper>();
        services.AddSingleton<ISectionMapper, ContainerStructMapper>();
        services.AddSingleton<ISectionMapper, GridStructMapper>();
        services.AddSingleton<ISectionMapper, ColumnStructMapper>();
        services.AddSingleton<ISectionMapper, StackStructMapper>();
        services.AddSingleton<ISectionMapper, DividerStructMapper>();
        services.AddSingleton<ISectionMapper, SpacerStructMapper>();
        services.AddSingleton<ISectionMapper, LayoutPreset1ColMapper>();
        services.AddSingleton<ISectionMapper, LayoutPreset2ColEqualMapper>();
        services.AddSingleton<ISectionMapper, LayoutPreset3ColEqualMapper>();
        services.AddSingleton<ISectionMapper, LayoutPreset4ColEqualMapper>();
        services.AddSingleton<ISectionMapper, LayoutPresetMainSidebarMapper>();
        // Textual (c4*)
        services.AddSingleton<ISectionMapper, HeadingMapper>();
        services.AddSingleton<ISectionMapper, ParagraphMapper>();
        services.AddSingleton<ISectionMapper, RichTextElementMapper>();
        services.AddSingleton<ISectionMapper, EyebrowMapper>();
        services.AddSingleton<ISectionMapper, QuoteMapper>();
        services.AddSingleton<ISectionMapper, LabelMapper>();
        services.AddSingleton<ISectionMapper, TextBlockMapper>();
        services.AddSingleton<ISectionMapper, CodeBlockMapper>();
        services.AddSingleton<ISectionMapper, AttributedQuoteMapper>();
        // Action (c5*)
        services.AddSingleton<ISectionMapper, ButtonMapper>();
        services.AddSingleton<ISectionMapper, LinkMapper>();
        services.AddSingleton<ISectionMapper, CtaGroupMapper>();
        services.AddSingleton<ISectionMapper, ButtonGroupMapper>();
        services.AddSingleton<ISectionMapper, ButtonContainerMapper>();
        // Media (c6*)
        services.AddSingleton<ISectionMapper, ImageMapper>();
        services.AddSingleton<ISectionMapper, VideoElementMapper>();
        services.AddSingleton<ISectionMapper, IconMapper>();
        services.AddSingleton<ISectionMapper, GalleryItemMapper>();
        services.AddSingleton<ISectionMapper, LogoItemMapper>();
        services.AddSingleton<ISectionMapper, AvatarMapper>();
        // Informational (c7*)
        services.AddSingleton<ISectionMapper, BadgeMapper>();
        services.AddSingleton<ISectionMapper, StatMapper>();
        services.AddSingleton<ISectionMapper, FeatureItemMapper>();
        services.AddSingleton<ISectionMapper, KeyValueMapper>();
        services.AddSingleton<ISectionMapper, TimelineItemMapper>();
        services.AddSingleton<ISectionMapper, FaqItemMapper>();
        services.AddSingleton<ISectionMapper, TestimonialItemMapper>();
        services.AddSingleton<ISectionMapper, PricingCardMapper>();
        // Composition (c8*)
        services.AddSingleton<ISectionMapper, CardElementMapper>();
        services.AddSingleton<ISectionMapper, HeroElementMapper>();
        services.AddSingleton<ISectionMapper, FeatureGridMapper>();
        services.AddSingleton<ISectionMapper, CtaBannerMapper>();
        services.AddSingleton<ISectionMapper, FaqListMapper>();
        services.AddSingleton<ISectionMapper, TestimonialListMapper>();
        services.AddSingleton<ISectionMapper, LogoCloudMapper>();
        services.AddSingleton<ISectionMapper, InfoBlockElementMapper>();
        services.AddSingleton<ISectionMapper, BannerMapper>();
        services.AddSingleton<ISectionMapper, MediaTextSplitMapper>();
        services.AddSingleton<ISectionMapper, AccordionMapper>();
        // Integration (c9*)
        services.AddSingleton<ISectionMapper, ScriptEmbedMapper>();
        services.AddSingleton<ISectionMapper, IframeEmbedMapper>();
        services.AddSingleton<ISectionMapper, ExternalWidgetMapper>();
        services.AddSingleton<ISectionMapper, MacroHostMapper>();
        // Corporate (ca*)
        services.AddSingleton<ISectionMapper, TabGroupMapper>();
        services.AddSingleton<ISectionMapper, AlertBarMapper>();
        services.AddSingleton<ISectionMapper, AlertBoxMapper>();
        services.AddSingleton<ISectionMapper, BannerSliderMapper>();
        services.AddSingleton<ISectionMapper, NewsletterFormMapper>();
        services.AddSingleton<ISectionMapper, SocialShareMapper>();
        services.AddSingleton<ISectionMapper, DataTableMapper>();
        services.AddSingleton<ISectionMapper, ContactInfoMapper>();
        services.AddSingleton<ISectionMapper, MapEmbedMapper>();
        services.AddSingleton<ISectionMapper, MissionBlockMapper>();
        // Forms (cc*)
        services.AddSingleton<ISectionMapper, FormEmbedMapper>();
        services.AddSingleton<ISectionMapper, FormBlockMapper>();
        // Blog blocks (c8* extensions)
        services.AddSingleton<ISectionMapper, BlogHighlightMapper>();
        services.AddSingleton<ISectionMapper, ArticleListMapper>();
        // Experiences (ce*)
        services.AddSingleton<ISectionMapper, FeatureJourneyMapper>();
        services.AddSingleton<ISectionMapper, InsightExplorerMapper>();
        services.AddSingleton<ISectionMapper, MediaExplorerMapper>();
        services.AddSingleton<ISectionMapper, ContentCarouselMapper>();
        services.AddSingleton<ISectionMapper, QuizFlowMapper>();
        services.AddSingleton<ISectionMapper, FilterBoardMapper>();
        services.AddSingleton<ISectionMapper, RatingWidgetMapper>();
        services.AddSingleton<ISectionMapper, CountdownClockMapper>();
        services.AddSingleton<ISectionMapper, NotificationStackMapper>();

        // Generic fallback mapper — handles element types with no specific ISectionMapper.
        // Registered separately (not as ISectionMapper) so SectionMapperDispatcher can
        // inject it directly as a typed fallback rather than mixing it into the IEnumerable.
        services.AddSingleton<GenericElementMapper>();

        return services;
    }

    /// <summary>
    /// Flow Engine — FlowConfigurationService (Scoped).
    /// IFlowWebhookDispatcher implementation is framework-specific (HTTP) —
    /// registered from Program.cs (see Infrastructure/Http).
    /// </summary>
    public static IServiceCollection AddSynergosOrchestration(this IServiceCollection services)
    {
        services.AddScoped<FlowConfigurationService>();
        return services;
    }

    /// <summary>
    /// Core rendering pipeline: dispatcher, assembler, element resolver, macro host.
    /// </summary>
    public static IServiceCollection AddSynergosRendering(this IServiceCollection services)
    {
        services.AddSingleton<SectionMapperDispatcher>();
        services.AddTransient<PageAssembler>();
        services.AddTransient<PageConfigAssembler>();
        services.AddTransient<ElementResolver>();
        services.AddSingleton<Services.FormRenderService>();
        // IArtifactResolver and IElementUrlResolver implementations are framework-specific —
        // registered from Program.cs (see Infrastructure/Cdn).
        return services;
    }
}
