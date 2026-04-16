using Umbraco.Cms.Core.Models.PublishedContent;
using Synergos.CMS.Application.Elements;
using Synergos.CMS.Application.Mapping.Compositions.Behavior;
using Synergos.CMS.Application.Mapping.Compositions.Content;
using Synergos.CMS.Application.Mapping.Compositions.Dom;
using Synergos.CMS.Application.Output;
using Synergos.CMS.Domain.Sections;

namespace Synergos.CMS.Application.Mapping.Elements;

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
