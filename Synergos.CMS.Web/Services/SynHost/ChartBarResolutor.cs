using Synergos.CMS.Interfaces.SynHost;
using Umbraco.Cms.Core.Models.PublishedContent;

namespace Synergos.CMS.Web.Services.SynHost;

/// <summary><c>elementSynChartBar</c> → <see cref="ChartBarProps"/>.</summary>
/// <remarks>
/// Parsea en el servidor el JSON de barras y lee cada <c>value</c> con
/// <see cref="LectorDelEditor.NumeroDe"/>: la misma lectura es-CO que el resto del lector.
/// </remarks>
public sealed class ChartBarResolutor : IResolutorSynHost<ChartBarProps>
{
    private readonly IPublishedValueFallback _fallback;
    private readonly ILogger<ChartBarResolutor> _log;

    public ChartBarResolutor(IPublishedValueFallback fallback, ILogger<ChartBarResolutor> log)
    {
        _fallback = fallback;
        _log = log;
    }

    public ElementoResuelto<ChartBarProps> Resolver(IPublishedElement elemento)
    {
        var editor = new LectorDelEditor(elemento, _fallback, _log);
        return new ElementoResuelto<ChartBarProps>(new ChartBarProps(
            Title: editor.Texto("chartTitle"),
            Orientation: editor.Texto("orientation"),
            Data: Barras(editor)));
    }

    private static IReadOnlyList<ChartBarEntry>? Barras(LectorDelEditor editor)
    {
        var lista = editor.ListaJson("dataJson", editor.Texto("dataJson"));
        if (lista is null)
        {
            return null;
        }

        var barras = new List<ChartBarEntry>();
        foreach (var entrada in lista)
        {
            var etiqueta = LectorDelEditor.Cadena(entrada, "label");
            var valor = editor.NumeroDe("dataJson", entrada, "value");
            if (etiqueta is not null && valor is { } numero)
            {
                barras.Add(new ChartBarEntry(etiqueta, numero));
                continue;
            }

            // Un valor presente e ilegible ya lo anotó NumeroDe; acá se anota lo que falta.
            if (etiqueta is null || !LectorDelEditor.Tiene(entrada, "value"))
            {
                editor.NoEsValido("dataJson", entrada.GetRawText(), "una barra con label y value");
            }
        }

        return barras.Count > 0 ? barras : null;
    }
}
