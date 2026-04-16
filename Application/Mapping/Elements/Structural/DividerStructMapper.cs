using Umbraco.Cms.Core.Models.PublishedContent;
using Synergos.CMS.Application.Elements;
using Synergos.CMS.Application.Mapping.Compositions.Dom;
using Synergos.CMS.Application.Output;
using Synergos.CMS.Domain.Sections;

namespace Synergos.CMS.Application.Mapping.Elements;

public sealed class DividerStructMapper(
    DomClassReader   cls,
    DomVariantReader variant) : ISectionMapper
{
    public string SupportedAlias => "elementStructDivider";
    public ISection? Map(IPublishedElement e) => new ComponentSection(new DividerViewModel
    {
        DomClass   = cls.Read(e),
        DomVariant = variant.Read(e)
    });
}
