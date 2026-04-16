using Umbraco.Cms.Core.Models.PublishedContent;
using Synergos.CMS.Application.Elements;
using Synergos.CMS.Application.Mapping.Compositions.Dom;
using Synergos.CMS.Application.Output;
using Synergos.CMS.Domain.Sections;

namespace Synergos.CMS.Application.Mapping.Elements;

public sealed class ContainerStructMapper(
    DomLayoutReader  layout,
    DomSpacingReader spacing,
    DomVariantReader variant) : ISectionMapper
{
    public string SupportedAlias => "elementStructContainer";
    public ISection? Map(IPublishedElement e) => new ComponentSection(new ContainerViewModel
    {
        DomLayout  = layout.Read(e),
        DomSpacing = spacing.Read(e),
        DomVariant = variant.Read(e)
    });
}
