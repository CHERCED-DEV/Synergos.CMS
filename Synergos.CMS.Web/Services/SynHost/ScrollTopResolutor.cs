using Synergos.CMS.Interfaces.SynHost;
using Umbraco.Cms.Core.Models.PublishedContent;

namespace Synergos.CMS.Web.Services.SynHost;

/// <summary>
/// <c>elementSynScrollTop</c> → <see cref="ScrollTopProps"/>. El <c>ariaLabel</c> de
/// <c>compDomAttributes</c> viaja como <c>label</c>, que es el nombre accesible que el elemento lee.
/// </summary>
public sealed class ScrollTopResolutor : IResolutorSynHost<ScrollTopProps>
{
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
            Position: editor.Texto("position"),
            Label: editor.Texto("ariaLabel")));
    }
}
