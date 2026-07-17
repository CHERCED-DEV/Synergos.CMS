namespace Synergos.CMS.Interfaces;

/// <summary>
/// Resuelve el <see cref="PageRenderContext"/> de la página actualmente
/// en request. Aplica la cascada page → siteRoot → defaults inline para
/// chrome, theme y orquestación visual de la página (ADR 0022).
/// </summary>
/// <remarks>
/// La implementación por defecto <c>DefaultPageRenderContextResolver</c>
/// vive en <c>Synergos.CMS.Web</c> porque depende del
/// <c>IUmbracoContextAccessor</c>. La interfaz no toma parámetros: se
/// asume invocada dentro de un request Razor con la página resuelta. Si
/// no hay request o página, el resolver devuelve
/// <see cref="PageRenderContext.Defaults"/>. Los componentes globales
/// (alertas, modales, banners) se resuelven en <c>IGlobalComponentResolver</c>.
/// </remarks>
public interface IPageRenderContextResolver
{
    /// <summary>
    /// Resuelve el contexto de render de la página actual. Nunca lanza;
    /// degrada a <see cref="PageRenderContext.Defaults"/>.
    /// </summary>
    PageRenderContext Resolve();
}

/// <summary>
/// Snapshot inmutable de las decisiones de chrome, theme y orquestación
/// para una página individual. Las plantillas Razor lo consumen para
/// decidir qué partials renderizar y con qué clases.
/// </summary>
/// <remarks>
/// Los valores string usan los aliases de los DT.Select.* (ej.
/// <c>ChromeMode = "full"</c>). Los booleanos son derivados (ej.
/// <c>ShowHeader</c> = chrome ≠ none/bare/embedded ∧ header ≠ hidden)
/// para evitar repetir la lógica en cada vista. Vive en
/// <c>Synergos.CMS.Interfaces</c> junto a su resolver, mismo patrón que
/// <see cref="BrandTheme"/>.
/// </remarks>
public sealed record PageRenderContext(
    string ChromeMode,
    string HeaderMode,
    string FooterMode,
    bool ShowHeader,
    bool ShowFooter,
    bool ShowTitle,
    bool ShowBreadcrumbs,
    string ThemeVariant,
    string PageSurface,
    string VisualProfile,
    string ContainerType,
    string SpacingScale)
{
    /// <summary>
    /// Defaults aplicados cuando no hay siteRoot resuelto o cuando la
    /// página renderiza fuera de un request Umbraco (ej. error pages).
    /// </summary>
    public static PageRenderContext Defaults() => new(
        ChromeMode: "full",
        HeaderMode: "visible",
        FooterMode: "visible",
        ShowHeader: true,
        ShowFooter: true,
        ShowTitle: true,
        ShowBreadcrumbs: false,
        ThemeVariant: "light",
        PageSurface: "default",
        VisualProfile: "institutional",
        ContainerType: "default",
        SpacingScale: "normal");
}
