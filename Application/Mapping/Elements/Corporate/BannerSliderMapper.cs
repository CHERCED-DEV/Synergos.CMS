using Umbraco.Cms.Core.Models.Blocks;
using Umbraco.Cms.Core.Models.PublishedContent;
using Synergos.CMS.Application.Elements;
using Synergos.CMS.Application.Mapping.Compositions.Content;
using Synergos.CMS.Application.Mapping.Compositions.Dom;
using Synergos.CMS.Application.Output;
using Synergos.CMS.Domain.Sections;

namespace Synergos.CMS.Application.Mapping.Elements;

public sealed class BannerSliderMapper(
    ContentCollectionReader collection,
    ContentTextReader       text,
    ContentMediaReader      media,
    ContentCtaReader        cta,
    DomClassReader          cls,
    DomVariantReader        variant) : ISectionMapper
{
    public string SupportedAlias => "elementCorpBannerSlider";

    public ISection? Map(IPublishedElement e)
    {
        var slides = e.Value<BlockListModel>("slides")
            ?.Select(b => new BannerSlideViewModel
            {
                Text  = text.Read(b.Content),
                Media = media.Read(b.Content),
                Cta   = cta.Read(b.Content),
            })
            .ToList() ?? [];

        return new ComponentSection(new BannerSliderViewModel
        {
            Collection = collection.Read(e),
            Text       = text.Read(e),
            Slides     = slides,
            DomClass   = cls.Read(e),
            DomVariant = variant.Read(e)
        });
    }
}
