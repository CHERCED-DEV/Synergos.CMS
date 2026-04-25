using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Application.Proxies.Impl;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Notifications;
using Synergos.CMS.Web.Services;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.Notifications;

namespace Synergos.CMS.Web.Composers;

/// <summary>
/// Wires the extension seams declared in
/// <c>Synergos.CMS.Interfaces</c> to their Ola 1 defaults and Ola 3
/// adapters, and registers the dictionary notification handler.
/// </summary>
/// <remarks>
/// Per ADR 0005 all <see cref="IComposer"/> implementations live in
/// <c>Synergos.CMS.Web/Composers/</c>. This composer extracts
/// <c>IOptions&lt;T&gt;.Value</c> and injects the POCO into defaults,
/// honouring the decision taken in Ola 1 not to add
/// <c>Microsoft.Extensions.Options</c> as a reference of
/// <c>Synergos.CMS.Application</c>.
/// </remarks>
[ComposeAfter(typeof(OptionsComposer))]
public sealed class SeamComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        var services = builder.Services;

        // Ola 1 defaults — POCOs snapshotted at service-activation time.
        services.AddSingleton<IBrandingProvider>(sp =>
            new DefaultBrandingProvider(
                sp.GetRequiredService<IOptions<BrandingSettings>>().Value));

        services.AddSingleton<IFeatureGate>(sp =>
            new AppsettingsFeatureGate(
                sp.GetRequiredService<IOptions<FeatureFlagsSettings>>().Value));

        services.AddSingleton<IDictionaryCache, DictionaryCache>();

        // Ola 8.5 — SynHost stack (ADR 0015 draft).
        // StubBundleRegistryClient is the dev-time default while the CDN
        // team has not published the real registry contract (ADR 0012 +
        // docs/umbraco/cdn-contract.md). It always resolves to null; the
        // SynHost emitter handles that by emitting a placeholder HTML
        // comment and still producing the custom element tag. When the
        // real adapter arrives, this registration swaps to
        // HttpBundleRegistryClient behind a Synergos:Cdn:Mode switch.
        services.AddSingleton<IBundleRegistryClient, StubBundleRegistryClient>();
        services.AddSingleton<ISynHostEmitter, DefaultSynHostEmitter>();

        // Ola 3 — Web-side adapters.
        services.AddSingleton<IContentContextAccessor, UmbracoContentContextAccessor>();

        // Ola 37 — Brand theme provider reads themeSettings nodes from
        // the Umbraco content tree (ADR 0020). Transient because it
        // depends on the per-request IUmbracoContextAccessor; the cost
        // is negligible (single projection).
        services.AddTransient<IBrandThemeProvider, DefaultBrandThemeProvider>();

        // Ola 49 — Page render context resolver. Cascada page →
        // siteRoot → defaults inline para chrome/theme/Alex (ADR 0022).
        // Transient por la misma razón que el theme provider.
        services.AddTransient<IPageRenderContextResolver, DefaultPageRenderContextResolver>();

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

        // Health probes. Each registers as an ISchemaHealthProbe; the
        // HealthController resolves them as IEnumerable<ISchemaHealthProbe>.
        services.AddSingleton<ISchemaHealthProbe>(_ => new SchemaVersionProbe());
        services.AddSingleton<ISchemaHealthProbe>(sp =>
        {
            var env = sp.GetRequiredService<IWebHostEnvironment>();
            var absolutePath = Path.Combine(env.ContentRootPath, "uSync", "v9");
            return new UsyncFolderProbe(absolutePath);
        });

        // MVC controller discovery for attribute-routed endpoints
        // (HealthController, future controllers). AddControllers is
        // idempotent so calling it here is safe even if Umbraco already
        // registered MVC.
        services.AddControllers();

        // Notification handler: dictionary invalidation.
        builder.AddNotificationHandler<
            DictionaryItemSavedNotification,
            DictionaryCacheInvalidator>();
        builder.AddNotificationHandler<
            DictionaryItemDeletedNotification,
            DictionaryCacheInvalidator>();

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
    }
}
