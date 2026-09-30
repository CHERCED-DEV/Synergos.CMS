using Synergos.CMS.Interfaces.SynHost;
using Umbraco.Cms.Core.Models.PublishedContent;

namespace Synergos.CMS.Web.Services.SynHost;

/// <summary>
/// <c>elementSynColorSwatches</c> → <see cref="ColorSwatchesProps"/>. Parsea en el servidor el JSON
/// de muestras que el editor escribe en <c>swatchesJson</c>.
/// </summary>
/// <remarks>
/// Acepta lo que documenta el ElementType (<c>hex</c>/<c>name</c>) y los nombres que el elemento
/// lee (<c>color</c>/<c>label</c>), y sale con los del elemento. Una muestra sin color no viaja y
/// se anota. Si el color es CSS válido lo decide el elemento, que es quien lo pinta.
/// </remarks>
public sealed class ColorSwatchesResolutor : IResolutorSynHost<ColorSwatchesProps>
{
    private readonly IPublishedValueFallback _fallback;
    private readonly ILogger<ColorSwatchesResolutor> _log;

    public ColorSwatchesResolutor(IPublishedValueFallback fallback, ILogger<ColorSwatchesResolutor> log)
    {
        _fallback = fallback;
        _log = log;
    }

    public ElementoResuelto<ColorSwatchesProps> Resolver(IPublishedElement elemento)
    {
        var editor = new LectorDelEditor(elemento, _fallback, _log);
        return new ElementoResuelto<ColorSwatchesProps>(new ColorSwatchesProps(
            Swatches: Muestras(editor, editor.Texto("swatchesJson")),
            Shape: editor.Texto("shape")?.ToLowerInvariant()));
    }

    private static IReadOnlyList<ColorSwatchesItem>? Muestras(LectorDelEditor editor, string? json)
    {
        var lista = editor.ListaJson("swatchesJson", json);
        if (lista is null)
        {
            return null;
        }

        var muestras = new List<ColorSwatchesItem>();
        foreach (var entrada in lista)
        {
            var color = LectorDelEditor.Cadena(entrada, "hex") ?? LectorDelEditor.Cadena(entrada, "color");
            if (color is null)
            {
                editor.NoEsValido("swatchesJson", entrada.GetRawText(), "una muestra con hex");
                continue;
            }

            muestras.Add(new ColorSwatchesItem(
                color,
                LectorDelEditor.Cadena(entrada, "name") ?? LectorDelEditor.Cadena(entrada, "label")));
        }

        return muestras.Count > 0 ? muestras : null;
    }
}
