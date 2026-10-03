using Synergos.CMS.Interfaces.SynHost;
using Umbraco.Cms.Core.Models.PublishedContent;

namespace Synergos.CMS.Web.Services.SynHost;

/// <summary>
/// <c>elementSynScrollTop</c> → <see cref="ScrollTopProps"/>. El <c>ariaLabel</c> de
/// <c>compDomAttributes</c> viaja como <c>label</c>, que es el nombre accesible que el elemento lee.
/// </summary>
public sealed class ScrollTopResolutor : IResolutorSynHost<ScrollTopProps>
{
    /// <summary>
    /// <c>DTSelectScreenPosition</c> lo comparte con <c>fab</c>, que sí va arriba; un «volver
    /// arriba» vive en el pie, así que las posiciones de arriba se pintan en su lado de abajo
    /// (#192, caso 8). Antes caían todas a la esquina inferior derecha.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> PosicionEnElElemento =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["top-left"] = "bottom-left",
            ["top-center"] = "bottom-center",
            ["top-right"] = "bottom-right",
        };

    private readonly IPublishedValueFallback _fallback;
    private readonly ILogger<ScrollTopResolutor> _log;

    public ScrollTopResolutor(IPublishedValueFallback fallback, ILogger<ScrollTopResolutor> log)
    {
        _fallback = fallback;
        _log = log;
    }

    public ElementoResuelto<ScrollTopProps> Resolver(IPublishedElement elemento)
    {
        var editor = new LectorDelEditor(elemento, _fallback, _log);
        return new ElementoResuelto<ScrollTopProps>(new ScrollTopProps(
            ScrollThreshold: editor.Entero("scrollThreshold"),
            Position: Posicion(editor.Texto("position")),
            Label: editor.Texto("ariaLabel")));
    }

    private static string? Posicion(string? posicion)
        => posicion is not null && PosicionEnElElemento.TryGetValue(posicion, out var abajo) ? abajo : posicion;
}
