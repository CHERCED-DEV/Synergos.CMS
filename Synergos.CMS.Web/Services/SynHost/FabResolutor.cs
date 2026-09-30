using Synergos.CMS.Interfaces.SynHost;
using Umbraco.Cms.Core.Models.PublishedContent;

namespace Synergos.CMS.Web.Services.SynHost;

/// <summary><c>elementSynFab</c> → <see cref="FabProps"/>.</summary>
/// <remarks>
/// El enlace viaja como su destino y dónde abre; el nombre accesible, del <c>ariaLabel</c> del
/// bloque o, si está vacío, del texto del enlace.
/// </remarks>
public sealed class FabResolutor : IResolutorSynHost<FabProps>
{
    private readonly IPublishedValueFallback _fallback;
    private readonly ILogger<FabResolutor> _log;

    public FabResolutor(IPublishedValueFallback fallback, ILogger<FabResolutor> log)
    {
        _fallback = fallback;
        _log = log;
    }

    public ElementoResuelto<FabProps> Resolver(IPublishedElement elemento)
    {
        var editor = new LectorDelEditor(elemento, _fallback, _log);
        var accion = editor.Enlace("actionLink");

        return new ElementoResuelto<FabProps>(new FabProps(
            IconKey: editor.Texto("iconKey"),
            ActionLink: accion?.Url,
            Target: accion?.Destino,
            Position: editor.Texto("position"),
            Label: editor.Texto("ariaLabel") ?? accion?.Nombre));
    }
}
