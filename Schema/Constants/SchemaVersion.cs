namespace Synergos.CMS.Schema.Constants;

/// <summary>
/// Idempotency key stored in Umbraco's key-value store.
/// Increment the version string to force a re-run of schema initialization.
/// </summary>
public static class SchemaVersion
{
    public const string Key   = "Synergos.Schema.Version";
    public const string Value = "11.1.0"; // 11.1.0 — Tier 1 completeness sweep: (1) Cleanup de retired doctypes (HomePage/AboutPage/ServicesPage/ContactPage/LandingPage eliminados de ContentTypeKeys; también desbloqueó GuidUniquenessTests que fallaba por colisión PageBare/LandingPage d2000007); (2) FlowDemoSiteSeeder ahora publica per-culture es-CO (fix 6 páginas demo que fallaban con FailedPublishContentInvalid); (3) Ported ContentAuthorReader + BehaviorFeatureFlagReader desde epicfail (composition readers faltantes para Author + FeatureFlag models; registrados en ServiceCollectionExtensions); (4) FeatureGateSettings + FeatureGateMiddleware nuevos — middleware declarativo que short-circuita 404 para route trees bajo flag disabled, configurable vía appsettings (lista PathPrefix/FeatureFlag pairs); bypass para /umbraco, /api, /App_Plugins, health endpoints. Previous 11.0.2: ContentSeeder patches (clear TemplateId + republish drafts) + icon sweep en 13 doctypes visibles.
}
