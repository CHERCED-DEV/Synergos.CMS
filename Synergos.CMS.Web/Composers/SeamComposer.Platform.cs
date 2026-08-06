using Microsoft.Extensions.DependencyInjection.Extensions;
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
    private void ComposePlatform(IUmbracoBuilder builder)
    {
        var services = builder.Services;

        // Olas 146-147 — Resilience tuning hot-reloadable. WebhookResilienceSettings
        // bind via AddOptions().Bind(...) registra el IConfigurationChangeTokenSource
        // automático — cuando appsettings.json cambia, IOptionsMonitor<...> fire,
        // y el siguiente handler rotation (HttpClientFactory default ~2min) re-
        // ejecuta Configure<TDep> con los valores frescos. Sin rebuild manual de
        // pipelines.
        services.AddOptions<WebhookResilienceSettings>()
            .Bind(builder.Config.GetSection("Synergos:Admin:WebhookResilience"));

        // Ola 54.1 — IBrandingProvider resuelve el brand activo según el
        // hostname del request, matchéandolo contra los siteConfigSettings
        // publicados (cada uno con su brandKey via compBranding). Si no
        // hay match, fallback al brand de BrandingSettings (config
        // estática). Singleton — depende de accessors per-request.
        services.AddSingleton<IBrandingProvider>(sp =>
            new HostBasedBrandingProvider(
                sp.GetRequiredService<IHttpContextAccessor>(),
                sp.GetRequiredService<IUmbracoContextAccessor>(),
                sp.GetRequiredService<IOptions<BrandingSettings>>().Value));

        services.AddSingleton<IFeatureGate>(sp =>
            new AppsettingsFeatureGate(
                sp.GetRequiredService<IOptions<FeatureFlagsSettings>>().Value));


        // Ola 8.5 + Cap-280 Batch B — SynHost stack (ADR 0015 + 0089).
        // El IBundleRegistryClient se registra condicionalmente por
        // Synergos:BundleRegistry:Mode (default Stub):
        //   - Stub: siempre null. Default cuando no hay CDN.
        //   - FileSystem: lee registry.json + manifests del filesystem,
        //     hot-reload via FileSystemWatcher, SRI lazy compute.
        //     Útil con CDN local (e.g. C:\LOCAL_CDN).
        //   - Http: GET al registry publicado en el CDN. Mismo mapeo que
        //     FileSystem, distinto transporte; sirve del último snapshot
        //     bueno y refresca por detrás (HU #20, ADR 0132).
        // El reloj, inyectable. Lo pide HttpBundleRegistryClient para decidir si su snapshot
        // venció, y sin esto el fallo sería en el ARRANQUE y no en la compilación — la clase de
        // error que no se ve hasta que alguien cambia el Mode a Http en producción.
        // Lo que lleva el identificador de correlación al siguiente servicio (HU #28).
        // Transient porque un DelegatingHandler lo es por contrato: el pipeline de
        // IHttpClientFactory gestiona su vida, y hacerlo singleton lo ata a un solo cliente.
        services.AddHttpContextAccessor();
        services.AddTransient<CorrelationForwardingHandler>();

        services.TryAddSingleton(TimeProvider.System);

        var bundleRegistryMode = builder.Config["Synergos:BundleRegistry:Mode"] ?? "Stub";
        if (string.Equals(bundleRegistryMode, "FileSystem", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IBundleRegistryClient, FileSystemBundleRegistryClient>();
        }
        else if (string.Equals(bundleRegistryMode, "Http", StringComparison.OrdinalIgnoreCase))
        {
            // Cliente tipado: el HttpClient lo gestiona la fábrica (reuso de conexiones,
            // reciclado de DNS). Un HttpClient a mano dentro de un singleton es el clásico que
            // deja de ver un cambio de DNS del CDN hasta que se reinicia el proceso.
            services.AddHttpClient<IBundleRegistryClient, HttpBundleRegistryClient>((sp, http) =>
            {
                var s = sp.GetRequiredService<IOptions<BundleRegistrySettings>>().Value;
                http.Timeout = TimeSpan.FromSeconds(Math.Max(1, s.TimeoutSeconds));
            });
        }
        else
        {
            services.AddSingleton<IBundleRegistryClient, StubBundleRegistryClient>();
        }
        services.AddSingleton<ISynHostEmitter, DefaultSynHostEmitter>();

        // Warmup hosted service fuerza la construcción del IBundleRegistryClient
        // singleton al boot — sin esto el adapter queda lazy hasta el primer
        // SynHost render, ocultando logs de "registry loaded" + watcher status.
        services.AddHostedService<BundleRegistryWarmupHostedService>();

        // Ola 3 — Web-side adapters.
        services.AddSingleton<IContentContextAccessor, UmbracoContentContextAccessor>();

        // Ola 37 — Brand theme provider reads themeSettings nodes from
        // the Umbraco content tree (ADR 0020). Transient because it
        // depends on the per-request IUmbracoContextAccessor; the cost
        // is negligible (single projection).
        services.AddTransient<IBrandThemeProvider, DefaultBrandThemeProvider>();

        // Ola 49 — Page render context resolver. Cascada page →
        // siteRoot → defaults inline para chrome/theme (ADR 0022).
        // Transient por la misma razón que el theme provider.
        services.AddTransient<IPageRenderContextResolver, DefaultPageRenderContextResolver>();

        // Ola 50 — Global component resolver. Lee siteConfigSettings.
        // globalComponents (BlockList) y devuelve la pieza activa
        // aplicable. Ola 52.A extendió a 4 cfg* (Alert/Banner/
        // FooterNote/Modal). Cada cfg* nuevo añade un método hermano
        // en el resolver sin tocar la lógica existente.
        services.AddTransient<IGlobalComponentResolver, DefaultGlobalComponentResolver>();

        // Ola 52.C — Member access gate + handler que aplica
        // compMemberGating.requiresAuth + allowedRolesCsv en el
        // routing del request. Singleton porque el gate solo lee
        // del HttpContext via accessor.
        services.AddSingleton<IMemberAccessGate, DefaultMemberAccessGate>();

        // Ola 56.2 — Blog query service. Recorre el published cache
        // buscando postPage descendientes del siteRoot, aplica filtros
        // de categoría/tags y proyecta a PostSummary records.
        // Consumido por renderers ArticleList, BlogHighlight y
        // PostCategoryPage.cshtml. Transient — depende del
        // IUmbracoContextAccessor per-request.
        services.AddTransient<IBlogQuery, DefaultBlogQuery>();

        // Ola 57.1 — Cart service. Persiste cart en cookie HMAC-firmada
        // del visitante (sin DB, sin login required). Hidrata items
        // cruzando SKUs con productPage publicados. Transient porque
        // depende de IHttpContextAccessor + IUmbracoContextAccessor.
        services.AddTransient<ICartService, DefaultCartService>();

        // Ola 81 — Cart abandonment tracker (ADR 0044). Singleton por
        // diseño — el state in-memory persiste entre requests del
        // mismo proceso. Background scanner emite "cart.abandoned"
        // events via IAnalyticsTracker cada N minutos para los carts
        // que excedan el threshold.
        services.AddSingleton<ICartAbandonmentTracker, InMemoryCartAbandonmentTracker>();
        services.AddHostedService<CartAbandonmentScannerHostedService>();

        // Ola 57.2 — Shop query service. Recorre productPage descendientes,
        // aplica filtros de categoría/sort y proyecta a ProductSummary.
        // Consumido por ProductGrid block + ProductCategoryPage.cshtml.
        services.AddTransient<IShopQuery, DefaultShopQuery>();

        // Bucket B (B-1) — Price formatter es-CO centralizado. Unifica el
        // render del precio (miles con punto, sin decimales, + moneda) que
        // antes se duplicaba inline en 6+ renderers Razor de Shop. Lógica
        // pura en Application; el composer extrae IOptions<CartSettings>.Value
        // y la inyecta como POCO (mismo patrón que AppsettingsFeatureGate)
        // para no referenciar Microsoft.Extensions.Options desde Application.
        // Singleton — stateless, solo cierra sobre la moneda default.
        services.AddSingleton<IPriceFormatter>(sp =>
            new EsCoPriceFormatter(
                sp.GetRequiredService<IOptions<CartSettings>>().Value));

    }
}
