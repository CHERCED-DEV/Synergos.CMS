using Umbraco.Cms.Core.Models.Blocks;
using Umbraco.Cms.Core.Models.PublishedContent;
using Synergos.CMS.Application.Elements;
using Synergos.CMS.Application.Mapping.Compositions.Content;
using Synergos.CMS.Application.Mapping.Compositions.Dom;
using Synergos.CMS.Application.Output;
using Synergos.CMS.Domain.Sections;
using static Synergos.CMS.Application.Mapping.Compositions.PropertyAliases.Collection;

namespace Synergos.CMS.Application.Mapping.Elements;

public sealed class FeatureGridMapper(
    ContentCollectionReader collection,
    ContentTextReader       textReader,
    ContentMediaReader      mediaReader,
    DomLayoutReader         layout,
    DomSpacingReader        spacing,
    DomClassReader          cls,
    DomVariantReader        variant) : ISectionMapper
{
    public string SupportedAlias => "elementCompFeatureGrid";

    public ISection? Map(IPublishedElement e)
    {
        // CTA on individual feature items within a grid is intentionally omitted
        // to stay within the 7-param limit (S107). Use FeatureItemMapper directly
        // when a standalone feature with a CTA link is needed.
        var items = e.Value<BlockListModel>(Items)
            ?.Select(b => new FeatureViewModel
            {
                Text     = textReader.Read(b.Content),
                Media    = mediaReader.Read(b.Content),
                DomClass = cls.Read(b.Content)
            })
            .ToList() ?? [];

        return new ComponentSection(new FeatureGridViewModel
        {
            Collection = collection.Read(e),
            Items      = items,
            DomLayout  = layout.Read(e),
            DomSpacing = spacing.Read(e),
            DomClass   = cls.Read(e),
            DomVariant = variant.Read(e)
        });
    }
}
