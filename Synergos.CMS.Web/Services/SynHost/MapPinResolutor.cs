using Synergos.CMS.Interfaces.SynHost;
using Umbraco.Cms.Core.Models.PublishedContent;

namespace Synergos.CMS.Web.Services.SynHost;

/// <summary><c>elementSynMapPin</c> → <see cref="MapPinProps"/>.</summary>
/// <remarks>
/// El centro y el zoom viajan como números (<see cref="LectorDelEditor.Numero"/> /
/// <see cref="LectorDelEditor.Entero"/>) y cada pin con sus coordenadas leídas por
/// <see cref="LectorDelEditor.NumeroDe"/>: una sola lectura es-CO para todo el lector.
/// </remarks>
public sealed class MapPinResolutor : IResolutorSynHost<MapPinProps>
{
    private readonly IPublishedValueFallback _fallback;
    private readonly ILogger<MapPinResolutor> _log;

    public MapPinResolutor(IPublishedValueFallback fallback, ILogger<MapPinResolutor> log)
    {
        _fallback = fallback;
        _log = log;
    }

    public ElementoResuelto<MapPinProps> Resolver(IPublishedElement elemento)
    {
        var editor = new LectorDelEditor(elemento, _fallback, _log);
        return new ElementoResuelto<MapPinProps>(new MapPinProps(
            CenterLat: editor.Numero("centerLat"),
            CenterLng: editor.Numero("centerLng"),
            ZoomLevel: editor.Entero("zoomLevel"),
            Pins: Pines(editor)));
    }

    private static IReadOnlyList<MapPinItem>? Pines(LectorDelEditor editor)
    {
        var lista = editor.ListaJson("pinsJson", editor.Texto("pinsJson"));
        if (lista is null)
        {
            return null;
        }

        var pines = new List<MapPinItem>();
        foreach (var entrada in lista)
        {
            var lat = editor.NumeroDe("pinsJson", entrada, "lat");
            var lng = editor.NumeroDe("pinsJson", entrada, "lng");
            if (lat is { } latitud && lng is { } longitud)
            {
                pines.Add(new MapPinItem(
                    latitud,
                    longitud,
                    LectorDelEditor.Cadena(entrada, "title"),
                    LectorDelEditor.Cadena(entrada, "description")));
                continue;
            }

            // Una coordenada presente e ilegible ya la anotó NumeroDe; acá se anota la que falta.
            if (!LectorDelEditor.Tiene(entrada, "lat") || !LectorDelEditor.Tiene(entrada, "lng"))
            {
                editor.NoEsValido("pinsJson", entrada.GetRawText(), "un pin con lat y lng");
            }
        }

        return pines.Count > 0 ? pines : null;
    }
}
