using Synergos.CMS.Interfaces.SynHost;
using Umbraco.Cms.Core.Models.PublishedContent;

namespace Synergos.CMS.Web.Services.SynHost;

/// <summary>
/// <c>elementSynSeparator</c> → <see cref="SeparatorProps"/>. El estilo viaja con el nombre que
/// tenía (<c>style</c>), en minúsculas, que es el vocabulario del selector.
/// </summary>
public sealed class SeparatorResolutor : IResolutorSynHost<SeparatorProps>
{
    private readonly IPublishedValueFallback _fallback;

    public SeparatorResolutor(IPublishedValueFallback fallback) => _fallback = fallback;

    public ElementoResuelto<SeparatorProps> Resolver(IPublishedElement elemento)
    {
        var editor = new LectorDelEditor(elemento, _fallback);
        return new ElementoResuelto<SeparatorProps>(new SeparatorProps(
            Style: editor.Texto("style")?.ToLowerInvariant()));
    }
}
