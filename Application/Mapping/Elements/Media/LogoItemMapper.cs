using Umbraco.Cms.Core.Models.PublishedContent;
using Synergos.CMS.Application.Elements;
using Synergos.CMS.Application.Mapping.Compositions.Content;
using Synergos.CMS.Application.Mapping.Compositions.Dom;
using Synergos.CMS.Application.Output;
using Synergos.CMS.Domain.Sections;

namespace Synergos.CMS.Application.Mapping.Elements;

public sealed class LogoItemMapper(
    ContentMediaReader mediaReader,
    ContentTextReader  textReader,
    ContentCtaReader   ctaReader,
    DomClassReader     cls) : ISectionMapper
{
    public string SupportedAlias => "elementMediaLogoItem";
    public ISection? Map(IPublishedElement e) => new ComponentSection(ReadAsItem(e));

    public LogoItemViewModel ReadAsItem(IPublishedElement e) => new()
    {
        Media    = mediaReader.Read(e),
        Text     = textReader.Read(e),
        Cta      = ctaReader.Read(e),
        DomClass = cls.Read(e)
    };
}
