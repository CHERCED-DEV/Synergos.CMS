using Umbraco.Cms.Core.Models.PublishedContent;
using Synergos.CMS.Application.Elements;
using Synergos.CMS.Application.Mapping.Compositions.Content;
using Synergos.CMS.Application.Mapping.Compositions.Dom;
using Synergos.CMS.Application.Output;
using Synergos.CMS.Domain.Sections;

namespace Synergos.CMS.Application.Mapping.Elements;

public sealed class ContactInfoMapper(
    ContentTextReader     text,
    ContentCtaReader      cta,
    ContentLocationReader location,
    DomClassReader        cls,
    DomVariantReader      variant) : ISectionMapper
{
    public string SupportedAlias => "elementCorpContactInfo";
    public ISection? Map(IPublishedElement e) => new ComponentSection(new ContactInfoViewModel
    {
        Text       = text.Read(e),
        Cta        = cta.Read(e),
        Location   = location.Read(e),
        DomClass   = cls.Read(e),
        DomVariant = variant.Read(e)
    });
}
