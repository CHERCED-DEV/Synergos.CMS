using Umbraco.Cms.Core.Models.PublishedContent;
using Synergos.CMS.Application.Elements;
using Synergos.CMS.Application.Mapping.Compositions.Content;
using Synergos.CMS.Application.Mapping.Compositions.Dom;
using Synergos.CMS.Application.Output;
using Synergos.CMS.Domain.Sections;

namespace Synergos.CMS.Application.Mapping.Elements;

public sealed class AlertBarMapper(
    ContentTextReader text,
    ContentCtaReader  cta,
    DomClassReader    cls,
    DomVariantReader  variant) : ISectionMapper
{
    public string SupportedAlias => "elementCorpAlertBar";
    public ISection? Map(IPublishedElement e) => new ComponentSection(new AlertBarViewModel
    {
        Text        = text.Read(e),
        Cta         = cta.Read(e),
        DomClass    = cls.Read(e),
        DomVariant  = variant.Read(e),
        Dismissible = e.Value<bool?>("dismissible"),
    });
}
