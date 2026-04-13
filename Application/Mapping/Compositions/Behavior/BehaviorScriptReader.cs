using Umbraco.Cms.Core.Models.PublishedContent;
using Synergos.CMS.Domain.Compositions.Behavior;
using static Synergos.CMS.Application.Mapping.Compositions.PropertyAliases.Script;

namespace Synergos.CMS.Application.Mapping.Compositions.Behavior;

public sealed class BehaviorScriptReader : ICompositionReader<BehaviorScriptModel>
{
    public BehaviorScriptModel Read(IPublishedElement element) => new(
        ScriptType:    element.Value<string>(ScriptType),
        ScriptContent: element.Value<string>(ScriptContent));
}
