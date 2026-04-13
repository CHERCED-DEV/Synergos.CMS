namespace Synergos.CMS.Configuration;

/// <summary>
/// Brand and contact defaults written by <c>ContentSeeder</c> on a fresh install.
/// Values only fill empty fields — they never overwrite data already set in the backoffice.
/// Bound from appsettings.json section "Synergos:Seed".
/// </summary>
public sealed class SeedConfig
{
    public const string SectionPath = "Synergos:Seed";

    // ── Toggle ───────────────────────────────────────────────────────────────
    /// <summary>
    /// Set to false to disable all content seeders on startup.
    /// The schema pipeline (Document Types, Data Types) always runs regardless.
    /// Use this when you want to build the content tree manually in the backoffice.
    /// </summary>
    public bool Enabled { get; init; } = true;

    // ── Identity ─────────────────────────────────────────────────────────────
    public string PlatformName    { get; init; } = "Synergos Platform";
    public string SiteName        { get; init; } = "Synergos";
    public string SiteTagline     { get; init; } = "Plataforma empresarial composable";
    public string SiteDisplayName { get; init; } = "Synergos";
    public string SiteTaglineExt  { get; init; } = "Transformación digital para empresas";
    /// <summary>
    /// ISO culture code (e.g. "es-CO", "en-US") assigned to culture-variant properties
    /// when seeding a fresh site. Must match one of the languages configured in Umbraco.
    /// </summary>
    public string SiteCulture     { get; init; } = "es-CO";

    // ── Contact ───────────────────────────────────────────────────────────────
    public string ContactEmail   { get; init; } = "contact@example.com";
    public string ContactPhone   { get; init; } = "+1 (555) 000-0000";
    public string ContactAddress { get; init; } = "City, Country";

    // ── Social ────────────────────────────────────────────────────────────────
    public string FacebookUrl { get; init; } = "https://www.facebook.com/yourpage";
    public string LinkedInUrl { get; init; } = "https://www.linkedin.com/company/yourcompany";
    public string TwitterUrl  { get; init; } = "https://twitter.com/yourhandle";
    public string YouTubeUrl  { get; init; } = "https://www.youtube.com/@yourchannel";

    // ── SEO ───────────────────────────────────────────────────────────────────
    public string SeoTitleSuffix        { get; init; } = " | Synergos";
    public string SeoDefaultDescription { get; init; } = "Plataforma empresarial composable para acelerar tu transformación digital.";
    public string GlobalSeoDescription  { get; init; } = "Soluciones digitales para transformar tu empresa. Consultoría, desarrollo y plataforma CMS empresarial.";

    // ── Forms ─────────────────────────────────────────────────────────────────
    public string FormRecipientEmail { get; init; } = "contact@example.com";

    // ── Header ────────────────────────────────────────────────────────────────
    public string HeaderCtaLabel { get; init; } = "Contáctanos";

    // ── Theme defaults (colors, fonts, spacing) ───────────────────────────────
    /// <summary>Brand theme values seeded into ThemeSettings on first boot.</summary>
    public SeedTheme Theme { get; init; } = new();

    // ── Business pages to create under SiteRoot ───────────────────────────────
    /// <summary>
    /// Ordered list of pages created by the seeder. Page <c>SortOrder</c> is the
    /// index in this list. Empty list disables business page seeding.
    /// </summary>
    public List<SeedPage> Pages { get; init; } =
    [
        new() {
            Name           = "Home",
            Subtitle       = "Bienvenidos a Synergos",
            SeoTitle       = "Inicio — Synergos",
            SeoDescription = "Plataforma empresarial composable para transformar tu negocio digital.",
            OgTitle        = "Synergos — Plataforma empresarial composable",
            OgDescription  = "Transformamos tu empresa con soluciones digitales composables, escalables y orientadas a resultados."
        },
        new() {
            Name           = "Nosotros",
            Subtitle       = "Conoce nuestra historia",
            SeoTitle       = "Nosotros — Synergos",
            SeoDescription = "Conoce la misión, visión y equipo detrás de Synergos.",
            OgTitle        = "Quiénes somos — Synergos",
            OgDescription  = "Conoce nuestra misión, visión y el equipo apasionado que construye Synergos."
        },
        new() {
            Name           = "Servicios",
            Subtitle       = "Soluciones digitales a la medida de tu empresa",
            SeoTitle       = "Servicios — Synergos",
            SeoDescription = "Descubre los servicios de consultoría y desarrollo que ofrece Synergos.",
            OgTitle        = "Servicios digitales — Synergos",
            OgDescription  = "Consultoría, desarrollo y soluciones CMS para acelerar la transformación digital de tu empresa."
        },
        new() {
            Name           = "Contacto",
            Subtitle       = "Hablemos de tu proyecto",
            SeoTitle       = "Contacto — Synergos",
            SeoDescription = "Contáctanos para iniciar tu proyecto de transformación digital.",
            OgTitle        = "Contacto — Synergos",
            OgDescription  = "Inicia tu proyecto de transformación digital. Nuestro equipo está listo para ayudarte."
        }
    ];
}
