using System.Globalization;
using Microsoft.AspNetCore.Hosting;
using uSync.BackOffice;
using Synergos.CMS.Application;
using Synergos.CMS.Application.Rendering;
using Synergos.CMS.Application.Services;
using Synergos.CMS.Configuration;
using Synergos.CMS.Domain.Services;
using Synergos.CMS.Domain.Orchestration;
using Synergos.CMS.Infrastructure.Cdn;
using Synergos.CMS.Infrastructure.Configuration;
using Synergos.CMS.Infrastructure.Email;
using Synergos.CMS.Infrastructure.Http;
using Synergos.CMS.Infrastructure.Umbraco;
using Synergos.CMS.Infrastructure.Umbraco.Services;
using Synergos.CMS.Presentation.Filters;
using Synergos.CMS.Presentation.Middlewares;
using Synergos.CMS.Schema;

// ── Kestrel: when explicit endpoints are declared in appsettings, clear the
// environment-injected ASPNETCORE_URLS so Kestrel doesn't emit address warnings.
bool hasConfiguredKestrelEndpoints = HasConfiguredKestrelEndpoints();
if (hasConfiguredKestrelEndpoints)
{
    Environment.SetEnvironmentVariable("ASPNETCORE_URLS", null);
    Environment.SetEnvironmentVariable("DOTNET_URLS", null);
}

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
ApplyConfiguredUiCulture(builder.Configuration);
if (hasConfiguredKestrelEndpoints)
    builder.WebHost.UseSetting(WebHostDefaults.ServerUrlsKey, string.Empty);

// ── Health checks (liveness + readiness) ──────────────────────────────────
// /healthz = is the process alive? /readyz = is Umbraco ready to serve?
builder.Services.AddHealthChecks()
    .AddCheck("self", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy(), tags: ["live"])
    .AddCheck<Synergos.CMS.Infrastructure.Health.SchemaVersionHealthCheck>("schema-version", tags: ["ready"])
    .AddCheck<Synergos.CMS.Infrastructure.Health.USyncFolderHealthCheck>("usync-folder", tags: ["ready"]);

// ── Response compression (brotli + gzip for Accept-Encoding clients) ──────
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<Microsoft.AspNetCore.ResponseCompression.BrotliCompressionProvider>();
    options.Providers.Add<Microsoft.AspNetCore.ResponseCompression.GzipCompressionProvider>();
    options.MimeTypes = Microsoft.AspNetCore.ResponseCompression.ResponseCompressionDefaults
        .MimeTypes
        .Concat(["application/xml", "application/manifest+json", "image/svg+xml"]);
});

// ── Output cache (profiles bound from Synergos:OutputCache) ───────────────
var outputCacheConfig = builder.Configuration.GetSection(OutputCacheSettings.SectionPath).Get<OutputCacheSettings>()
                        ?? new OutputCacheSettings();
builder.Services.AddOutputCache(options =>
{
    foreach (var profile in outputCacheConfig.Profiles)
    {
        options.AddPolicy(profile.Name, builder =>
        {
            builder.Expire(TimeSpan.FromSeconds(profile.DurationSeconds));
            if (profile.VaryByQueryKeys.Length > 0)   builder.SetVaryByQuery(profile.VaryByQueryKeys);
            if (profile.VaryByHeaderNames.Length > 0) builder.SetVaryByHeader(profile.VaryByHeaderNames);
            if (profile.Tags.Length > 0)              builder.Tag(profile.Tags);
        });
    }
});

// ── Configuration (strongly-typed, bound from appsettings.json) ───────────
builder.Services.Configure<ElementServersSettings>(
    builder.Configuration.GetSection(ElementServersSettings.SectionPath));
builder.Services.Configure<StaticAssetsSettings>(
    builder.Configuration.GetSection(StaticAssetsSettings.SectionPath));
builder.Services.Configure<RuntimeSettings>(
    builder.Configuration.GetSection(RuntimeSettings.SectionPath));
builder.Services.Configure<FlowEngineSettings>(
    builder.Configuration.GetSection(FlowEngineSettings.SectionPath));
builder.Services.Configure<SecuritySettings>(
    builder.Configuration.GetSection(SecuritySettings.SectionPath));
builder.Services.Configure<SeedConfig>(
    builder.Configuration.GetSection(SeedConfig.SectionPath));
builder.Services.Configure<CacheSettings>(
    builder.Configuration.GetSection(CacheSettings.SectionPath));

// ── CORS — Synergos app endpoints (API, forms, CDN-hosted elements) ───────
// Umbraco manages its own CORS policy for the backoffice — this does not interfere.
var runtimeCfg = builder.Configuration
    .GetSection(RuntimeSettings.SectionPath)
    .Get<RuntimeSettings>() ?? new RuntimeSettings();

builder.Services.AddCors(options =>
    options.AddPolicy("SynergosPolicy", policy => policy
        .WithOrigins(runtimeCfg.AllowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()));

// ── Schema DI groups (reduce constructor parameter count) ─────────────────
builder.Services.AddTransient<SchemaServices>();
builder.Services.AddTransient<SeedDependencies>();

// ── Content context abstraction (confines IUmbracoContextAccessor to Infrastructure) ──
builder.Services.AddSingleton<IContentContextAccessor, UmbracoContentContextAccessor>();

// ── Site resolution & navigation ──────────────────────────────────────────
builder.Services.AddScoped<ISiteResolver,      UmbracoSiteResolver>();
builder.Services.AddScoped<INavigationService, UmbracoNavigationService>();

// ── Focused layout services (ISP) ─────────────────────────────────────────
// Inject the focused interface when only one concern is needed; use ILayoutService
// (or the composite UmbracoLayoutService) when multiple concerns are required.
builder.Services.AddScoped<IThemeService,    UmbracoThemeService>();
builder.Services.AddScoped<IHeaderService,   UmbracoHeaderService>();
builder.Services.AddScoped<IFooterService,   UmbracoFooterService>();
builder.Services.AddScoped<IAlertBarService, UmbracoAlertBarService>();
builder.Services.AddScoped<IBannerService,   UmbracoBannerService>();
builder.Services.AddScoped<ITrackingService, UmbracoTrackingService>();
builder.Services.AddScoped<ISeoService,      UmbracoSeoService>();

// Composite — delegates to the seven focused services above.
builder.Services.AddScoped<ILayoutService, UmbracoLayoutService>();

// ── Dictionary cache (framework-specific) ─────────────────────────────────
builder.Services.AddSingleton<IDictionaryCache, UmbracoDictionaryCache>();

// ── Email (framework-specific) ────────────────────────────────────────────
builder.Services.AddTransient<IFormEmailService, UmbracoFormEmailService>();

// ── Flow Engine webhook (framework-specific: HTTP) ────────────────────────
builder.Services.AddTransient<IFlowWebhookDispatcher, HttpFlowWebhookDispatcher>();

// ── HTTP clients ──────────────────────────────────────────────────────────
builder.Services.AddHttpClient("FormWebhook");
builder.Services.AddHttpClient("FlowEngine");

// ── Application services, mappers, rendering pipeline ────────────────────
builder.Services.AddSynergosApplication();
builder.Services.AddSingleton<StaticUrlBuilder>();

// ── CDN resolvers (framework-specific: filesystem-backed) ─────────────────
builder.Services.AddSingleton<IArtifactResolver,   FileSystemArtifactResolver>();
builder.Services.AddSingleton<IElementUrlResolver, FileSystemElementUrlResolver>();
// Request-scoped dedup: each bundle emits its <script> at most once per page,
// even if the editor drops N macros of the same element (CdnCard × 4 → 1 script).
builder.Services.AddScoped<IEmittedBundleTracker, EmittedBundleTracker>();
// Per-request memo of CDN config serializations: same JSON config repeated N
// times on a page serializes once (xxHash3-keyed). Win on listings/grids.
builder.Services.AddScoped<ICdnConfigCache, CdnConfigCache>();

// ── Feature flags (typed config + reflection-driven dynamic accessor) ─────
builder.Services.Configure<FeatureFlagsSettings>(
    builder.Configuration.GetSection(FeatureFlagsSettings.SectionPath));
builder.Services.Configure<FeatureGateSettings>(
    builder.Configuration.GetSection(FeatureGateSettings.SectionPath));
builder.Services.AddSingleton<IFeatureFlags, FeatureFlagsService>();

// ── Presentation ──────────────────────────────────────────────────────────
builder.Services.AddScoped<ApiKeyAuthFilter>();

// ── Umbraco ───────────────────────────────────────────────────────────────
builder.CreateUmbracoBuilder()
    .AddBackOffice()
    .AddWebsite()
    .AddDeliveryApi()
    .AddComposers()
    .AdduSync()
    .Build();

WebApplication app = builder.Build();

await app.BootUmbracoAsync();

// ── Global error page ─────────────────────────────────────────────────────
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    // Production: route unhandled errors to /error (returns 500 with safe body,
    // logs the full exception). Also catches 4xx/5xx status codes to show a
    // friendlier page than ASP.NET's default.
    app.UseExceptionHandler("/error");
    app.UseStatusCodePagesWithReExecute("/error/{0}");
    app.UseHsts(); // Strict-Transport-Security — 30 days by default, production only.
}

// ── Correlation IDs (must run before any log-enabling middleware) ─────────
app.UseMiddleware<Synergos.CMS.Presentation.Middlewares.CorrelationIdMiddleware>();

// ── Feature gate: short-circuit 404 for disabled route trees ──────────────
app.UseMiddleware<Synergos.CMS.Presentation.Middlewares.FeatureGateMiddleware>();

// ── Security headers ──────────────────────────────────────────────────────
var runtimeForSec = app.Configuration.GetSection(RuntimeSettings.SectionPath).Get<RuntimeSettings>()
                    ?? new RuntimeSettings();
var cspBase =
    "default-src 'self'; " +
    "img-src 'self' data: https:; " +
    "style-src 'self' 'unsafe-inline' https:; " +
    "script-src 'self' 'unsafe-inline' https:; " +
    "font-src 'self' data: https:; " +
    "connect-src 'self' https:; " +
    "frame-ancestors 'self'; " +
    "object-src 'none'; " +
    "base-uri 'self'";
var csp = string.IsNullOrWhiteSpace(runtimeForSec.CspAdditionalDirectives)
    ? cspBase
    : $"{cspBase}; {runtimeForSec.CspAdditionalDirectives!.TrimEnd(';', ' ')}";

app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["X-Content-Type-Options"]   = "nosniff";
    ctx.Response.Headers["X-Frame-Options"]          = "SAMEORIGIN";
    ctx.Response.Headers["Referrer-Policy"]          = "strict-origin-when-cross-origin";
    ctx.Response.Headers["Permissions-Policy"]       = "camera=(), microphone=(), geolocation=()";
    // Skip CSP on backoffice + plugin routes so Umbraco's inline scripts work.
    var path = ctx.Request.Path.Value ?? string.Empty;
    if (!path.StartsWith("/umbraco/", StringComparison.OrdinalIgnoreCase)
        && !path.StartsWith("/App_Plugins/", StringComparison.OrdinalIgnoreCase))
    {
        ctx.Response.Headers["Content-Security-Policy"] = csp;
    }
    await next();
});

app.UseResponseCompression();

// ── Static file caching — wwwroot assets (CSS/JS) ────────────────────────
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        var path = ctx.Context.Request.Path.Value ?? "";
        if (path.StartsWith("/css", StringComparison.OrdinalIgnoreCase)
         || path.StartsWith("/js",  StringComparison.OrdinalIgnoreCase))
        {
            // 7 days — no immutable because files are not content-hashed yet
            ctx.Context.Response.Headers["Cache-Control"] = "public, max-age=604800";
        }
    }
});

app.UseCors("SynergosPolicy");
app.UseOutputCache();

// ── Request timeout guard (after CORS+OutputCache so cached hits skip it) ─
var runtimeSettings = app.Configuration.GetSection(RuntimeSettings.SectionPath).Get<RuntimeSettings>()
                      ?? new RuntimeSettings();
if (runtimeSettings.RequestTimeoutSeconds > 0)
{
    app.UseSynergosTimeout(
        TimeSpan.FromSeconds(runtimeSettings.RequestTimeoutSeconds),
        runtimeSettings.TimeoutExcludedPaths);
}

// Align request UI culture with the configured Umbraco DefaultUILanguage.
app.Use((_, next) =>
{
    ApplyConfiguredUiCulture(app.Configuration);
    return next();
});

app.UseUmbraco()
    .WithMiddleware(u =>
    {
        u.UseBackOffice();
        u.UseWebsite();
    })
    .WithEndpoints(u =>
    {
        u.UseInstallerEndpoints();
        u.UseBackOfficeEndpoints();
        u.UseWebsiteEndpoints();
    });

// ── Health endpoints (unauthenticated, cheap) ─────────────────────────────
// /healthz = liveness (K8s livenessProbe): is the process alive?
// /readyz  = readiness (K8s readinessProbe): is Umbraco ready to serve traffic?
app.MapHealthChecks("/healthz", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live")
});
app.MapHealthChecks("/readyz", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => true
});

await app.RunAsync();

// ─────────────────────────────────────────────────────────────────────────
// Local helpers (top-level)
// ─────────────────────────────────────────────────────────────────────────

static void ApplyConfiguredUiCulture(IConfiguration configuration)
{
    var configuredUiLanguage = configuration["Umbraco:CMS:Global:DefaultUILanguage"];
    if (string.IsNullOrWhiteSpace(configuredUiLanguage))
        return;

    try
    {
        var uiCulture = CultureInfo.GetCultureInfo(configuredUiLanguage);
        CultureInfo.DefaultThreadCurrentUICulture = uiCulture;
        Thread.CurrentThread.CurrentUICulture = uiCulture;
    }
    catch (CultureNotFoundException)
    {
        // Ignore invalid UI language config — keep host defaults.
    }
}

static bool HasConfiguredKestrelEndpoints()
{
    var environmentName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
        ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
        ?? Environments.Production;

    IConfiguration bootstrapConfiguration = new ConfigurationBuilder()
        .SetBasePath(Directory.GetCurrentDirectory())
        .AddJsonFile("appsettings.json", optional: true)
        .AddJsonFile($"appsettings.{environmentName}.json", optional: true)
        .Build();

    return bootstrapConfiguration
        .GetSection("Kestrel:Endpoints")
        .GetChildren()
        .Any(endpoint => !string.IsNullOrWhiteSpace(endpoint["Url"]));
}
