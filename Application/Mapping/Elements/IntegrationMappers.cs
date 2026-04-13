using Umbraco.Cms.Core.Models.PublishedContent;
using Synergos.CMS.Application.Elements;
using Synergos.CMS.Application.Mapping.Compositions.Content;
using Synergos.CMS.Application.Mapping.Compositions.Dom;
using Synergos.CMS.Application.Mapping.Compositions.Behavior;
using Synergos.CMS.Application.Output;
using Synergos.CMS.Domain.Sections;

namespace Synergos.CMS.Application.Mapping.Elements;

public sealed class ScriptEmbedMapper(
    BehaviorScriptReader  scriptReader,
    DomVisibilityReader   visibility) : ISectionMapper
{
    public string SupportedAlias => "elementIntScriptEmbed";
    public ISection? Map(IPublishedElement e) => new ComponentSection(new ScriptEmbedViewModel
    {
        Script        = scriptReader.Read(e),
        DomVisibility = visibility.Read(e)
    });
}

public sealed class IframeEmbedMapper(
    ContentEmbedReader embedReader,
    DomSpacingReader   spacing,
    DomVariantReader   variant) : ISectionMapper
{
    public string SupportedAlias => "elementIntIframeEmbed";
    public ISection? Map(IPublishedElement e) => new ComponentSection(new IframeEmbedViewModel
    {
        Embed      = embedReader.Read(e),
        DomSpacing = spacing.Read(e),
        DomVariant = variant.Read(e)
    });
}

public sealed class ExternalWidgetMapper(
    ContentEmbedReader   embedReader,
    BehaviorAsyncReader  asyncReader,
    DomClassReader       cls,
    DomVariantReader     variant) : ISectionMapper
{
    public string SupportedAlias => "elementIntExternalWidget";
    public ISection? Map(IPublishedElement e) => new ComponentSection(new ExternalWidgetViewModel
    {
        Embed      = embedReader.Read(e),
        Async      = asyncReader.Read(e),
        DomClass   = cls.Read(e),
        DomVariant = variant.Read(e)
    });
}

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
