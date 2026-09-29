using System.Globalization;
using Synergos.CMS.Interfaces.SynHost;
using Umbraco.Cms.Core.Dictionary;
using Umbraco.Cms.Core.Models.PublishedContent;

namespace Synergos.CMS.Web.Services.SynHost;

/// <summary>
/// <c>elementSynKpiCard</c> → <see cref="KpiCardProps"/>, y el respaldo SSR del mismo bloque.
/// </summary>
/// <remarks>
/// Traduce los alias del ElementType (<c>kpiLabel</c>…, lo que el editor edita) a los nombres que
/// el elemento lee (<c>label</c>…). Esa traducción vivía en ningún sitio: la vista mandaba los
/// alias tal cual y el elemento los tiraba al hidratar (D1).
/// </remarks>
public sealed class KpiCardResolutor : IResolutorSynHost<KpiCardProps>
{
    private readonly IPublishedValueFallback _fallback;
    private readonly ICultureDictionaryFactory _diccionarios;

    public KpiCardResolutor(IPublishedValueFallback fallback, ICultureDictionaryFactory diccionarios)
    {
        _fallback = fallback;
        _diccionarios = diccionarios;
    }

    public ElementoResuelto<KpiCardProps> Resolver(IPublishedElement elemento)
    {
        var editor = new LectorDelEditor(elemento, _fallback);

        var props = new KpiCardProps(
            Label: editor.Texto("kpiLabel"),
            Value: editor.Texto("kpiValue"),
            Trend: SynHostFallbackBuilder.TrendKey(editor.Texto("kpiTrend")),
            DeltaLabel: editor.Texto("kpiDelta"),
            Period: editor.Texto("kpiPeriod"));

        var respaldo = SynHostFallbackBuilder.KpiCard(
            props.Label, props.Value, props.Trend, props.DeltaLabel, props.Period, FraseDeTendencia(props.Trend));

        return new ElementoResuelto<KpiCardProps>(props, respaldo);
    }

    /// <summary>
    /// La frase de la tendencia para el respaldo SSR, del diccionario (<c>Synhost.Kpi.Trend.*</c>):
    /// una flecha sola no le dice nada a un lector de pantalla.
    /// </summary>
    private string? FraseDeTendencia(string? tendencia)
    {
        var (clave, respaldo) = tendencia switch
        {
            "up" => ("Synhost.Kpi.Trend.Up", "Tendencia al alza"),
            "down" => ("Synhost.Kpi.Trend.Down", "Tendencia a la baja"),
            "flat" => ("Synhost.Kpi.Trend.Flat", "Tendencia estable"),
            _ => (null, null),
        };

        if (clave is null)
        {
            return null;
        }

        // Umbraco devuelve cadena vacía cuando la clave no existe, y algunos proveedores la clave
        // misma: ninguna de las dos es una frase.
        var traducida = _diccionarios.CreateDictionary(CultureInfo.CurrentUICulture)[clave];
        return string.IsNullOrWhiteSpace(traducida) || string.Equals(traducida, clave, StringComparison.Ordinal)
            ? respaldo
            : traducida;
    }
}
