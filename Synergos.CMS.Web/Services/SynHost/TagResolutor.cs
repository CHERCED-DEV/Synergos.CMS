using Synergos.CMS.Interfaces.SynHost;
using Umbraco.Cms.Core.Models.PublishedContent;

namespace Synergos.CMS.Web.Services.SynHost;

/// <summary><c>elementSynTag</c> → <see cref="TagProps"/>.</summary>
public sealed class TagResolutor : IResolutorSynHost<TagProps>
{
    private readonly IPublishedValueFallback _fallback;

    public TagResolutor(IPublishedValueFallback fallback) => _fallback = fallback;

    public ElementoResuelto<TagProps> Resolver(IPublishedElement elemento)
    {
        var editor = new LectorDelEditor(elemento, _fallback);
        return new ElementoResuelto<TagProps>(new TagProps(
            Label: editor.Texto("tagLabel"),
            Color: editor.Texto("tagColor")?.ToLowerInvariant()));
    }
}
