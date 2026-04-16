using Umbraco.Cms.Core.Models.PublishedContent;
using Synergos.CMS.Application.Elements;
using Synergos.CMS.Application.Mapping.Compositions.Content;
using Synergos.CMS.Application.Mapping.Compositions.Dom;
using Synergos.CMS.Application.Output;
using Synergos.CMS.Domain.Sections;

namespace Synergos.CMS.Application.Mapping.Elements;

public sealed class CountdownClockMapper(
    ContentTextReader     text,
    ContentMetadataReader metadata,
    DomVariantReader      variant,
    DomClassReader        cls,
    DomAttributesReader   attrs) : ISectionMapper
{
    public string SupportedAlias => "experienceCountdownClock";
    public ISection? Map(IPublishedElement e) => new ComponentSection(new CountdownClockViewModel
    {
        Text          = text.Read(e),
        Metadata      = metadata.Read(e),
        DomVariant    = variant.Read(e),
        DomClass      = cls.Read(e),
        DomAttributes = attrs.Read(e)
    });
}
