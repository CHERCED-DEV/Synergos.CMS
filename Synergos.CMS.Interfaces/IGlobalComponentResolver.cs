namespace Synergos.CMS.Interfaces;

/// <summary>
/// Resuelve los componentes globales del sitio (alertas, modales,
/// banners, ...) configurados desde el árbol Settings y consumidos por
/// cualquier template Razor sin que la página tenga que componer schema
/// por sí misma.
/// </summary>
/// <remarks>
/// La implementación por defecto vive en
/// <c>Synergos.CMS.Web.Services.DefaultGlobalComponentResolver</c>
/// porque depende de <c>IUmbracoContextAccessor</c>. La interfaz no
/// recibe parámetros — se asume invocada en un request vivo. Cuando no
/// hay request, no hay siteRoot resoluble o el componente no aplica
/// (apagado, fuera de schedule, suprimido por la página actual), los
/// métodos devuelven <c>null</c>; nunca lanzan.
///
/// Pattern transversal: cuando aterricen <c>cfgModal</c>,
/// <c>cfgBanner</c>, etc., se añaden métodos hermanos
/// (<c>GetActiveModal()</c>, <c>GetActiveBanner()</c>) y se reusa la
/// misma fuente de verdad — el BlockList <c>globalComponents</c> de
/// <c>siteConfigSettings</c>.
/// </remarks>
public interface IGlobalComponentResolver
{
    /// <summary>
    /// Devuelve la primera alerta activa y aplicable al request actual,
    /// respetando suppress por página, flag <c>alertActive</c> y ventana
    /// <c>alertScheduleStart</c>/<c>alertScheduleEnd</c>. <c>null</c> si
    /// ninguna aplica.
    /// </summary>
    CfgAlert? GetActiveAlert();
}

/// <summary>
/// Snapshot inmutable de una alerta global resuelta. Producido por
/// <see cref="IGlobalComponentResolver"/>. Las plantillas Razor lo
/// consumen para renderizar la cintilla superior sin tener que leer
/// propiedades Umbraco directamente.
/// </summary>
/// <param name="Message">Texto principal — siempre no-vacío cuando este
///   record llega al consumidor (el resolver filtra los vacíos).</param>
/// <param name="Variant">info / success / warning / danger / promo /
///   neutral / brand. Vacío implícito = "neutral".</param>
/// <param name="Tone">soft / solid / outline / glass / minimal. Vacío
///   implícito = "soft".</param>
/// <param name="Icon">Nombre de icono opcional (ej. "info").</param>
/// <param name="CtaLabel">Etiqueta del CTA opcional.</param>
/// <param name="CtaUrl">URL del CTA opcional.</param>
/// <param name="CtaOpenInNewTab">true si CtaUrl debe abrir en pestaña nueva.</param>
/// <param name="Dismissible">true si el visitante puede cerrar la cintilla.</param>
public sealed record CfgAlert(
    string Message,
    string? Variant,
    string? Tone,
    string? Icon,
    string? CtaLabel,
    string? CtaUrl,
    bool CtaOpenInNewTab,
    bool Dismissible);
