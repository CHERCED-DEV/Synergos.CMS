namespace Synergos.CMS.Interfaces;

/// <summary>
/// Resuelve los componentes globales del sitio (alertas, banners, avisos
/// de footer, modales) configurados desde el árbol Settings y consumidos
/// por cualquier template Razor sin que la página tenga que componer
/// schema por sí misma.
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
/// Pattern transversal: cada cfg* nuevo añade un método hermano
/// y reusa la misma fuente de verdad — el BlockList
/// <c>globalComponents</c> de <c>siteConfigSettings</c>. Los métodos
/// no comparten lógica de filtrado entre sí; cada uno puede tener
/// reglas propias (ej. modal con frecuencia/trigger).
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

    /// <summary>
    /// Devuelve el primer banner activo y aplicable al request actual,
    /// respetando suppress por página, flag <c>bannerActive</c> y
    /// ventana de fechas. <c>null</c> si ninguno aplica.
    /// </summary>
    CfgBanner? GetActiveBanner();

    /// <summary>
    /// Devuelve el primer aviso de footer activo y aplicable al request
    /// actual, respetando suppress por página, flag
    /// <c>footerNoteActive</c> y ventana de fechas. <c>null</c> si
    /// ninguno aplica.
    /// </summary>
    CfgFooterNote? GetActiveFooterNote();

    /// <summary>
    /// Devuelve el primer modal activo y aplicable al request actual,
    /// respetando suppress por página, flag <c>modalActive</c> y ventana
    /// de fechas. La frecuencia (always/once/daily/session) y el
    /// disparador (immediate/scroll/exit/manual) los aplica el cliente
    /// JS — el resolver solo decide si el modal aplica al request.
    /// </summary>
    CfgModal? GetActiveModal();
}

/// <summary>
/// Snapshot inmutable de una alerta global resuelta. Producido por
/// <see cref="IGlobalComponentResolver"/>.
/// </summary>
public sealed record CfgAlert(
    string Message,
    string? Variant,
    string? Tone,
    string? Icon,
    string? CtaLabel,
    string? CtaUrl,
    bool CtaOpenInNewTab,
    bool Dismissible);

/// <summary>
/// Snapshot inmutable de un banner promocional global resuelto.
/// </summary>
/// <param name="Placement">"top" o "bottom" — dónde se inserta en
///   relación al cuerpo de la página.</param>
public sealed record CfgBanner(
    string Message,
    string? ImageUrl,
    string? CtaLabel,
    string? CtaUrl,
    bool CtaOpenInNewTab,
    string Placement);

/// <summary>
/// Snapshot inmutable de un aviso de footer global resuelto.
/// </summary>
public sealed record CfgFooterNote(
    string Text,
    string? CtaLabel,
    string? CtaUrl,
    bool CtaOpenInNewTab);

/// <summary>
/// Snapshot inmutable de un modal global resuelto. La lógica de
/// frecuencia y disparador se ejecuta en el cliente JS; el resolver
/// solo determina si el modal aplica al request actual.
/// </summary>
/// <param name="Trigger">immediate / scroll / exit / manual.</param>
/// <param name="Frequency">always / once / daily / session.</param>
public sealed record CfgModal(
    string Title,
    string? Body,
    string? ImageUrl,
    string? CtaLabel,
    string? CtaUrl,
    bool CtaOpenInNewTab,
    string Trigger,
    string Frequency);
