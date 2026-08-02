using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Application.Proxies.Impl;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Notifications;
using Synergos.CMS.Web.Services;
using Synergos.CMS.Web.Services.Catalog;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Web;

namespace Synergos.CMS.Web.Composers;

public sealed partial class SeamComposer
{
    private void ComposeModerationDevAndNotifications(IUmbracoBuilder builder)
    {
        var services = builder.Services;

        // Ola 68 — Comments runtime (ADR 0038). FileSystemCommentRepository
        // persiste un JSON por nodo (App_Data/syn-comments/{nodeId}.json).
        // Singleton — solo depende de IOptions + IHostEnvironment + ILogger.
        // Para concurrent-heavy o > 1000 comments por nodo, swap por adapter
        // sobre DB.
        services.AddSingleton<ICommentRepository, FileSystemCommentRepository>();

        // Olas 89 + 90 — Comment moderation notifier composite.
        // El consumer (CommentsController) inyecta ICommentModerationNotifier;
        // el composite default itera todos los ICommentModerationNotifierChannel
        // registrados (email + webhook). Cada canal es no-op si su settings
        // están vacíos, por lo que registrar ambos es seguro by default.
        services.AddSingleton<ICommentModerationNotifierChannel, EmailCommentModerationNotifier>();
        services.AddHttpClient(WebhookCommentModerationNotifier.FactoryName).AddWebhookResilience();
        services.AddSingleton<ICommentModerationNotifierChannel, WebhookCommentModerationNotifier>();
        services.AddHttpClient(SlackCommentModerationNotifier.FactoryName).AddWebhookResilience();
        services.AddSingleton<ICommentModerationNotifierChannel, SlackCommentModerationNotifier>();
        services.AddHttpClient(DiscordCommentModerationNotifier.FactoryName).AddWebhookResilience();
        services.AddSingleton<ICommentModerationNotifierChannel, DiscordCommentModerationNotifier>();
        services.AddHttpClient(TeamsCommentModerationNotifier.FactoryName).AddWebhookResilience();
        services.AddSingleton<ICommentModerationNotifierChannel, TeamsCommentModerationNotifier>();
        services.AddSingleton<ICommentModerationNotifier, CompositeCommentModerationNotifier>();

        // Ola 91 — Form submission notifier composite (paralelo del de
        // comments). Reemplaza la lógica inline de email del controller
        // con un seam swappable; cada canal es no-op si su settings
        // están vacíos.
        services.AddSingleton<IFormSubmissionNotifierChannel, EmailFormSubmissionNotifier>();
        services.AddHttpClient(WebhookFormSubmissionNotifier.FactoryName).AddWebhookResilience();
        services.AddSingleton<IFormSubmissionNotifierChannel, WebhookFormSubmissionNotifier>();
        services.AddHttpClient(SlackFormSubmissionNotifier.FactoryName).AddWebhookResilience();
        services.AddSingleton<IFormSubmissionNotifierChannel, SlackFormSubmissionNotifier>();
        services.AddHttpClient(DiscordFormSubmissionNotifier.FactoryName).AddWebhookResilience();
        services.AddSingleton<IFormSubmissionNotifierChannel, DiscordFormSubmissionNotifier>();
        services.AddHttpClient(TeamsFormSubmissionNotifier.FactoryName).AddWebhookResilience();
        services.AddSingleton<IFormSubmissionNotifierChannel, TeamsFormSubmissionNotifier>();
        services.AddSingleton<IFormSubmissionNotifier, CompositeFormSubmissionNotifier>();

        // Ola 102 — Cart abandonment notifier composite (paralelo de
        // comments + forms). Hook en CartAbandonmentScannerHostedService
        // por cart detectado. Email + webhook channels con HMAC opt-in.
        services.AddSingleton<ICartAbandonmentNotifierChannel, EmailCartAbandonmentNotifier>();
        services.AddHttpClient(WebhookCartAbandonmentNotifier.FactoryName).AddWebhookResilience();
        services.AddSingleton<ICartAbandonmentNotifierChannel, WebhookCartAbandonmentNotifier>();
        services.AddHttpClient(SlackCartAbandonmentNotifier.FactoryName).AddWebhookResilience();
        services.AddSingleton<ICartAbandonmentNotifierChannel, SlackCartAbandonmentNotifier>();
        services.AddHttpClient(DiscordCartAbandonmentNotifier.FactoryName).AddWebhookResilience();
        services.AddSingleton<ICartAbandonmentNotifierChannel, DiscordCartAbandonmentNotifier>();
        services.AddHttpClient(TeamsCartAbandonmentNotifier.FactoryName).AddWebhookResilience();
        services.AddSingleton<ICartAbandonmentNotifierChannel, TeamsCartAbandonmentNotifier>();
        services.AddSingleton<ICartAbandonmentNotifier, CompositeCartAbandonmentNotifier>();

        // Ola 41 — Flow engine runtime. FlowResolver queries the content
        // tree (Transient for the same per-request reason as theme). The
        // FlowController is picked up by AddControllers() above;
        // IHttpContextAccessor is registered by ASP.NET Core and is
        // consumed by the elementFlowProgress renderer to read the
        // syn-flow-{flowKey} cookie.
        services.AddTransient<FlowResolver>();
        services.AddHttpContextAccessor();

        // Ola 47 — Dev tooling: content seeder para smoke-test. Gated
        // por Synergos:DevSeed:Enabled=true en appsettings. El controller
        // /dev/seed-test-site invoca el seeder. Scope: solo crea/borra
        // el siteRoot "Test Site", nunca otros árboles.
        services.AddTransient<DevTestContentSeeder>();
        services.AddTransient<SynergosIdentitySeeder>();

        // Autoría server-side de contenido (Umbraco 13 no tiene Management
        // API). SchemaBlockDefaults siembra props multi-value editor-safe;
        // DevContentFiller puebla el BlockGrid de páginas existentes.
        services.AddTransient<SchemaBlockDefaults>();
        services.AddTransient<DevMediaFactory>();
        services.AddTransient<DevContentFiller>();
        // Tooling dev-only: crea los member groups de dominio (funcionario/organizador/
        // doctor…) y los asigna. Sin esto, las consolas que T2-Gov/T2-Eventos/T9/T7
        // cerraron por rol quedan correctas pero IMPOSIBLES de demostrar.
        services.AddTransient<DevMemberRoleSeeder>();
        // T10 (ADR 0114): siembra de reseñas de demo. Tooling dev — el endpoint que lo usa
        // devuelve 404 sin el flag; nada corre en boot (ADR 0013).
        services.AddTransient<DevProductReviewSeeder>();
        services.AddTransient<DevPaidOrderSeeder>();

        // Health probes. Each registers as an ISchemaHealthProbe; the
        // HealthController resolves them as IEnumerable<ISchemaHealthProbe>.
        services.AddSingleton<ISchemaHealthProbe>(_ => new SchemaVersionProbe());
        services.AddSingleton<ISchemaHealthProbe>(sp =>
        {
            var env = sp.GetRequiredService<IWebHostEnvironment>();
            var absolutePath = Path.Combine(env.ContentRootPath, "uSync", "v9");
            return new UsyncFolderProbe(absolutePath);
        });

        // Cap-280 Batch C — BundleRegistry probe (ADR 0089). Reporta el
        // estado del adapter activo: Stub healthy con mensaje informativo,
        // FileSystem resuelve un probe tag canónico contra el registry,
        // Unknown mode reporta unhealthy. Visible vía /_health.
        services.AddSingleton<ISchemaHealthProbe, BundleRegistryProbe>();

        // MVC controller discovery for attribute-routed endpoints
        // (HealthController, future controllers). AddControllers is
        // idempotent so calling it here is safe even if Umbraco already
        // registered MVC.
        services.AddControllers();

        // Ola 42.5 — pre-fill Layout Preset blocks with sensible
        // defaults on first save so the editor doesn't face empty
        // dropdowns every time they drop a preset. See
        // LayoutPresetDefaults for the all-empty heuristic.
        builder.AddNotificationHandler<
            ContentSavingNotification,
            LayoutPresetDefaults>();

        // Ola 42.7 — starter scaffold: opt-in via
        // Synergos:LayoutComposer:EnableStarterScaffold. Seeds a
        // minimal Hero + 2ColEven on first save of a blank pageBase.
        builder.AddNotificationHandler<
            ContentSavingNotification,
            LayoutComposerStarterScaffold>();

        // Ola 52.C — Member gating: cuando el routing resuelve a un
        // PublishedContent que compone compMemberGating con
        // requiresAuth=true, el handler verifica IMemberAccessGate y
        // redirige a /login con returnUrl si el miembro no califica.
        builder.AddNotificationHandler<
            RoutingRequestNotification,
            MemberGatingHandler>();
    }
}
