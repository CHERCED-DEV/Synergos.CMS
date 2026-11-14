using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.Blocks;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// La acción de una ficha que no se compra en línea (016-contenido-piezas): la del cierre que compone el editor
/// («Agendar visita», «Reservar ahora»), para mostrarla también junto al precio. Sin cierre con enlace, no hay botón:
/// un botón que no lleva a nada miente.
/// </summary>
public static class AccionDeLaFicha
{
    /// <summary>El rótulo y el enlace del primer cierre (<c>elementCompCtaBanner</c>) del cuerpo, en cualquier nivel.</summary>
    public static (string Rotulo, string Url)? Del(IEnumerable<BlockGridItem>? items)
    {
        if (items is null) { return null; }
        foreach (var item in items)
        {
            if (string.Equals(item.Content.ContentType.Alias, "elementCompCtaBanner", StringComparison.Ordinal)
                && item.Content.Value<Link>("ctaLink") is { Url: { Length: > 0 } url } enlace)
            {
                var rotulo = item.Content.Value<string>("ctaLabel");
                return (string.IsNullOrWhiteSpace(rotulo) ? enlace.Name ?? url : rotulo, url);
            }
            foreach (var area in item.Areas)
            {
                if (Del(area) is { } enArea) { return enArea; }
            }
        }
        return null;
    }
}
