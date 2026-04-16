using Umbraco.Cms.Core.Models.PublishedContent;
using Synergos.CMS.Application.Elements;
using Synergos.CMS.Application.Mapping.Compositions.Content;
using Synergos.CMS.Application.Mapping.Compositions.Dom;
using Synergos.CMS.Application.Output;
using Synergos.CMS.Domain.Sections;

namespace Synergos.CMS.Application.Mapping.Elements;

public sealed class HeadingMapper(
    ContentHeadingReader heading,
    DomClassReader       cls,
    DomAttributesReader  attrs) : ISectionMapper
{
    public string SupportedAlias => "elementTextHeading";
    public ISection? Map(IPublishedElement e) => new ComponentSection(new HeadingViewModel
    {
        Heading       = heading.Read(e),
        DomClass      = cls.Read(e),
        DomAttributes = attrs.Read(e)
    });
}
