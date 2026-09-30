using Synergos.CMS.Interfaces.SynHost;
using Umbraco.Cms.Core.Models.PublishedContent;

namespace Synergos.CMS.Web.Services.SynHost;

/// <summary>
/// <c>elementSynRangeSlider</c> → <see cref="RangeSliderProps"/>. Los TextBox numéricos viajan
/// como enteros con los nombres que el elemento lee, y el valor inicial como <c>high</c>.
/// </summary>
/// <remarks>
/// No se acota nada: que el máximo supere al mínimo o que el inicial caiga dentro es regla del
/// elemento, que es quien pinta el rango (y ya la aplica).
/// </remarks>
public sealed class RangeSliderResolutor : IResolutorSynHost<RangeSliderProps>
{
    private readonly IPublishedValueFallback _fallback;
    private readonly ILogger<RangeSliderResolutor> _log;

    public RangeSliderResolutor(IPublishedValueFallback fallback, ILogger<RangeSliderResolutor> log)
    {
        _fallback = fallback;
        _log = log;
    }

    public ElementoResuelto<RangeSliderProps> Resolver(IPublishedElement elemento)
    {
        var editor = new LectorDelEditor(elemento, _fallback, _log);
        return new ElementoResuelto<RangeSliderProps>(new RangeSliderProps(
            Label: editor.Texto("label"),
            Min: editor.Entero("minValue"),
            Max: editor.Entero("maxValue"),
            Step: editor.Entero("step"),
            High: editor.Entero("initialValue")));
    }
}
