using Umbraco.Cms.Core.Models.PublishedContent;
using Synergos.CMS.Application.Elements;
using Synergos.CMS.Application.Mapping.Compositions.Content;
using Synergos.CMS.Application.Mapping.Compositions.Dom;
using Synergos.CMS.Application.Output;
using Synergos.CMS.Domain.Sections;

namespace Synergos.CMS.Application.Mapping.Elements;

public sealed class MissionBlockMapper(
    ContentTextReader  text,
    ContentMediaReader media,
    DomClassReader     cls,
    DomVariantReader   variant) : ISectionMapper
{
    public string SupportedAlias => "elementCorpMissionBlock";
    public ISection? Map(IPublishedElement e) => new ComponentSection(new MissionBlockViewModel
    {
        Text       = text.Read(e),
        Media      = media.Read(e),
        DomClass   = cls.Read(e),
        DomVariant = variant.Read(e)
    });
}
