using Synergos.CMS.Application.Configuration;
using Umbraco.Cms.Core.Composing;

namespace Synergos.CMS.Web.Composers;

/// <summary>
/// Binds typed POCOs from <c>appsettings.*.json</c> into the DI
/// container so that Application services can consume them via
/// <c>IOptions&lt;T&gt;</c> / <c>IOptionsMonitor&lt;T&gt;</c> at the Web
/// composition boundary.
/// </summary>
/// <remarks>
/// Per ADR 0005 all composers live in
/// <c>Synergos.CMS.Web/Composers/</c>. Per ADR 0002 the Application
/// project does not reference <c>Microsoft.Extensions.Options</c>;
/// bindings are resolved here, and Web-level wiring (done in a future
/// composer — see Ola 3) extracts <c>.Value</c> and injects the POCO
/// into defaults such as <c>DefaultBrandingProvider</c>.
///
/// Option sections:
/// <list type="bullet">
///   <item><c>Synergos:Branding</c> → <see cref="BrandingSettings"/> (ADR 0010)</item>
///   <item><c>Synergos:FeatureFlags</c> → <see cref="FeatureFlagsSettings"/> (ADR 0011)</item>
/// </list>
/// </remarks>
public sealed class OptionsComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services.Configure<BrandingSettings>(
            builder.Config.GetSection("Synergos:Branding"));

        builder.Services.Configure<FeatureFlagsSettings>(
            builder.Config.GetSection("Synergos:FeatureFlags"));

        builder.Services.Configure<LayoutComposerSettings>(
            builder.Config.GetSection("Synergos:LayoutComposer"));

        builder.Services.Configure<CdnSettings>(
            builder.Config.GetSection("Synergos:Cdn"));

        builder.Services.Configure<DevSeedSettings>(
            builder.Config.GetSection("Synergos:DevSeed"));

        builder.Services.Configure<MembersSettings>(
            builder.Config.GetSection("Synergos:Members"));

        builder.Services.Configure<CartSettings>(
            builder.Config.GetSection("Synergos:Cart"));

        builder.Services.Configure<CartAbandonmentSettings>(
            builder.Config.GetSection("Synergos:CartAbandonment"));

        builder.Services.Configure<FormsSettings>(
            builder.Config.GetSection("Synergos:Forms"));

        builder.Services.Configure<SearchSettings>(
            builder.Config.GetSection("Synergos:Search"));

        builder.Services.Configure<EmailSettings>(
            builder.Config.GetSection("Synergos:Email"));

        builder.Services.Configure<OutputCacheSettings>(
            builder.Config.GetSection("Synergos:OutputCache"));

        builder.Services.Configure<CommentsSettings>(
            builder.Config.GetSection("Synergos:Comments"));

        builder.Services.Configure<AdminSettings>(
            builder.Config.GetSection("Synergos:Admin"));

        // Olas 195-196 — Webhook telemetry alerts (ADR 0080).
        builder.Services.Configure<WebhookTelemetryAlertSettings>(
            builder.Config.GetSection("Synergos:Admin:WebhookTelemetryAlerts"));

        // Ola 216 — Host bridge (ADR 0083). Tuning de window.synergos.
        builder.Services.Configure<HostBridgeSettings>(
            builder.Config.GetSection("Synergos:HostBridge"));

        // Olas 257-258 — Data Protection multi-instance keyring (ADR 0087).
        // Vacío default = preserva comportamiento per-instance (no breaking).
        builder.Services.Configure<DataProtectionSettings>(
            builder.Config.GetSection("Synergos:DataProtection"));

        // Olas 273-275 — Retention policies generalizadas (ADR 0088).
        // 0 = nunca purgar (operador gestiona manualmente).
        builder.Services.Configure<RetentionSettings>(
            builder.Config.GetSection("Synergos:Retention"));

        // Olas 278-279 — SQLite maintenance pragmas (ADR 0088 Batch D).
        // Activo cuando Umbraco usa SQLite — no-op silent en SQL Server.
        builder.Services.Configure<SqliteMaintenanceSettings>(
            builder.Config.GetSection("Synergos:SqliteMaintenance"));

        // Olas 281-282 — Local CDN static files endpoint (ADR 0089).
        // Default Enabled=false: solo se monta si el operador configura
        // explícitamente el LocalPath + flag.
        builder.Services.Configure<LocalCdnSettings>(
            builder.Config.GetSection("Synergos:LocalCdn"));

        // Olas 283-285 — Bundle registry client (ADR 0089 Batch B).
        // Mode={Stub|FileSystem|Http} controla qué adapter resuelve los
        // bundles UI. Settings detallados via Synergos:BundleRegistry.
        builder.Services.Configure<BundleRegistrySettings>(
            builder.Config.GetSection("Synergos:BundleRegistry"));

        // ADR 0097 — Dashboard de métricas. Flush + retención de la
        // proyección pre-agregada. Default Enabled=true; el panel/identidad
        // viven en cfgDashboardSettings (uSync) + la app Angular.
        builder.Services.Configure<DashboardSettings>(
            builder.Config.GetSection("Synergos:Dashboard"));

        // ADR 0098 — Healthcare (vertical clínico PHI): disclaimer, zona horaria,
        // retención, 2FA de staff.
        builder.Services.Configure<HealthcareSettings>(
            builder.Config.GetSection("Synergos:Healthcare"));

        // ADR 0027 — Blog: tamaño de página de categoría + posts relacionados.
        builder.Services.Configure<BlogSettings>(
            builder.Config.GetSection("Synergos:Blog"));
    }
}
