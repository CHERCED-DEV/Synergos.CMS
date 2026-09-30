using System.Text.RegularExpressions;
using Synergos.CMS.Interfaces.SynHost;
using Umbraco.Cms.Core.Models.PublishedContent;

namespace Synergos.CMS.Web.Services.SynHost;

/// <summary><c>elementSynColorPicker</c> → <see cref="ColorPickerProps"/>.</summary>
/// <remarks>
/// Parsea en el servidor la paleta —una lista JSON de cadenas— y deja pasar sólo los colores hex,
/// que es lo que el elemento sabe pintar; el resto se anota.
/// </remarks>
public sealed class ColorPickerResolutor : IResolutorSynHost<ColorPickerProps>
{
    /// <summary>Un color hex como lo acepta el elemento: <c>#rgb</c> o <c>#rrggbb</c>, el <c>#</c> opcional.</summary>
    private static readonly Regex Hex = new(
        "^#?([0-9a-fA-F]{3}|[0-9a-fA-F]{6})$", RegexOptions.CultureInvariant);

    private const string EsperadoHex = "un color hex (#ff6600 o #f60)";

    private readonly IPublishedValueFallback _fallback;
    private readonly ILogger<ColorPickerResolutor> _log;

    public ColorPickerResolutor(IPublishedValueFallback fallback, ILogger<ColorPickerResolutor> log)
    {
        _fallback = fallback;
        _log = log;
    }

    public ElementoResuelto<ColorPickerProps> Resolver(IPublishedElement elemento)
    {
        var editor = new LectorDelEditor(elemento, _fallback, _log);
        return new ElementoResuelto<ColorPickerProps>(new ColorPickerProps(
            Label: editor.Texto("label"),
            InitialColor: ColorInicial(editor),
            Palette: Paleta(editor)));
    }

    private static string? ColorInicial(LectorDelEditor editor)
    {
        var color = editor.Texto("initialColor");
        if (color is null || Hex.IsMatch(color))
        {
            return color;
        }

        editor.NoEsValido("initialColor", color, EsperadoHex);
        return null;
    }

    private static IReadOnlyList<string>? Paleta(LectorDelEditor editor)
    {
        var lista = editor.ListaJson("paletteJson", editor.Texto("paletteJson"));
        if (lista is null)
        {
            return null;
        }

        var colores = new List<string>();
        foreach (var entrada in lista)
        {
            var color = LectorDelEditor.Cadena(entrada);
            if (color is not null && Hex.IsMatch(color))
            {
                colores.Add(color);
                continue;
            }

            editor.NoEsValido("paletteJson", entrada.GetRawText(), EsperadoHex);
        }

        return colores.Count > 0 ? colores : null;
    }
}
