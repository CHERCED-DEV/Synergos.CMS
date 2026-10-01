using Umbraco.Cms.Core.Models.PublishedContent;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// La fecha que el editor puso, o <c>null</c> si dejó el campo vacío. UNA regla para todo el CMS.
/// </summary>
/// <remarks>
/// <para><b>Un campo de fecha vacío NO llega como <c>null</c></b> (#185). El conversor del date
/// picker de Umbraco devuelve <see cref="DateTime.MinValue"/> cuando no hay valor, así que
/// <c>Value&lt;DateTime?&gt;</c> da <c>0001-01-01</c> con <c>HasValue</c> en <c>true</c>, y
/// <c>Value&lt;DateTime&gt;</c> da el mismo año 1. Esa fecha no la escribe ningún editor: es la
/// ausencia.</para>
///
/// <para><b>La regla estaba escrita cuatro veces, de dos formas</b>, y faltaba en la quinta: el
/// <c>&lt;time&gt;</c> de <c>TimelineItem.cshtml</c> pintaba <c>datetime="0001-01-01"</c> y
/// «1/01/0001» en todo hito sin fecha (#188, medido en vivo con un hito vacío y su control con
/// fecha). Hoy toda lectura de fecha pasa por aquí, y hay gate (<c>FechasDelEditorTests</c>): un
/// <c>Value&lt;DateTime</c> fuera de este fichero rompe la suite.</para>
///
/// <para>Vale también para un <c>Umbraco.TextBox</c> que guarda una fecha
/// (<c>postPage.publishDate</c>, <c>YYYY-MM-DD</c> obligatorio): ahí el vacío llega como
/// <c>null</c> y el texto que no es fecha también —medido en vivo: no hay año 1 en el blog—, así
/// que pasar por la regla no cambia nada y deja una sola forma de leer fechas.</para>
/// </remarks>
public static class FechasDelEditor
{
    /// <summary>La fecha de <paramref name="alias"/>, o <c>null</c> si el editor la dejó vacía.</summary>
    public static DateTime? FechaDelEditor(this IPublishedElement elemento, string alias)
        => SinElAnoUno(elemento.Value<DateTime?>(alias));

    /// <summary>Igual, con el respaldo inyectado: es lo que deja probarla sin arrancar Umbraco.</summary>
    public static DateTime? FechaDelEditor(
        this IPublishedElement elemento, IPublishedValueFallback respaldo, string alias)
        => SinElAnoUno(elemento.Value<DateTime?>(respaldo, alias));

    private static DateTime? SinElAnoUno(DateTime? leida)
        => leida is null || leida.Value == DateTime.MinValue ? null : leida;
}
