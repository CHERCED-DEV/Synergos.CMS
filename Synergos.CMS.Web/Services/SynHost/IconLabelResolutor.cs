using Synergos.CMS.Interfaces.SynHost;
using Umbraco.Cms.Core.Models.PublishedContent;

namespace Synergos.CMS.Web.Services.SynHost;

/// <summary><c>elementSynIconLabel</c> → <see cref="IconLabelProps"/>.</summary>
public sealed class IconLabelResolutor : IResolutorSynHost<IconLabelProps>
{
    private readonly IPublishedValueFallback _fallback;

    public IconLabelResolutor(IPublishedValueFallback fallback) => _fallback = fallback;

    public ElementoResuelto<IconLabelProps> Resolver(IPublishedElement elemento)
    {
        var editor = new LectorDelEditor(elemento, _fallback);
        return new ElementoResuelto<IconLabelProps>(new IconLabelProps(
            IconName: editor.Texto("iconKey"),
            LabelText: editor.Texto("labelText")));
    }
}
