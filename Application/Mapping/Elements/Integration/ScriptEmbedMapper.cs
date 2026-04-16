using Umbraco.Cms.Core.Models.PublishedContent;
using Synergos.CMS.Application.Elements;
using Synergos.CMS.Application.Mapping.Compositions.Behavior;
using Synergos.CMS.Application.Mapping.Compositions.Dom;
using Synergos.CMS.Application.Output;
using Synergos.CMS.Domain.Sections;

namespace Synergos.CMS.Application.Mapping.Elements;

public sealed class ScriptEmbedMapper(
    BehaviorScriptReader scriptReader,
    DomVisibilityReader  visibility) : ISectionMapper
{
    public string SupportedAlias => "elementIntScriptEmbed";
    public ISection? Map(IPublishedElement e) => new ComponentSection(new ScriptEmbedViewModel
    {
        Script        = scriptReader.Read(e),
        DomVisibility = visibility.Read(e)
    });
}
