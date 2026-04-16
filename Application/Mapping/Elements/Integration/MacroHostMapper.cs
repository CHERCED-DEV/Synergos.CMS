using Umbraco.Cms.Core.Models.PublishedContent;
using Synergos.CMS.Application.Elements;
using Synergos.CMS.Application.Output;
using Synergos.CMS.Domain.Sections;

namespace Synergos.CMS.Application.Mapping.Elements;

public sealed class MacroHostMapper : ISectionMapper
{
    public string SupportedAlias => "elementIntMacroHost";
    public ISection? Map(IPublishedElement e) => new ComponentSection(new MacroHostViewModel
    {
        MacroAlias     = e.Value<string>("macroAlias"),
        MacroTitle     = e.Value<string>("macroTitle"),
        MacroVariant   = e.Value<string>("macroVariant"),
        MacroTheme     = e.Value<string>("macroTheme"),
        MacroElementId = e.Value<string>("macroElementId"),
        MacroParams    = e.Value<string>("macroParams")
    });
}
