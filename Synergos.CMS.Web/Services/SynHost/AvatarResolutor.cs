using Synergos.CMS.Interfaces.SynHost;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Routing;

namespace Synergos.CMS.Web.Services.SynHost;

/// <summary><c>elementSynAvatar</c> → <see cref="AvatarProps"/>.</summary>
/// <remarks>
/// La foto viaja como la URL del medio; su nombre accesible, del <c>ariaLabel</c> del bloque o, si
/// está vacío, del texto alternativo del medio.
/// </remarks>
public sealed class AvatarResolutor : IResolutorSynHost<AvatarProps>
{
    private readonly IPublishedValueFallback _fallback;
    private readonly IPublishedUrlProvider _urls;
    private readonly ILogger<AvatarResolutor> _log;

    public AvatarResolutor(IPublishedValueFallback fallback, IPublishedUrlProvider urls, ILogger<AvatarResolutor> log)
    {
        _fallback = fallback;
        _urls = urls;
        _log = log;
    }

    public ElementoResuelto<AvatarProps> Resolver(IPublishedElement elemento)
    {
        var editor = new LectorDelEditor(elemento, _fallback, _log, _urls);
        var foto = editor.Medio("avatarImage");
        return new ElementoResuelto<AvatarProps>(new AvatarProps(
            Src: foto?.Url,
            Alt: editor.Texto("ariaLabel") ?? foto?.Alt));
    }
}
