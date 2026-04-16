using Umbraco.Cms.Core.Models.PublishedContent;
using Synergos.CMS.Application.Elements;
using Synergos.CMS.Application.Mapping.Compositions.Behavior;
using Synergos.CMS.Application.Mapping.Compositions.Content;
using Synergos.CMS.Application.Mapping.Compositions.Dom;
using Synergos.CMS.Application.Output;
using Synergos.CMS.Domain.Sections;

namespace Synergos.CMS.Application.Mapping.Elements;

public sealed class NewsletterFormMapper(
    ContentTextReader   text,
    ContentCtaReader    cta,
    BehaviorAsyncReader asyncReader,
    DomClassReader      cls) : ISectionMapper
{
    public string SupportedAlias => "elementCorpNewsletterForm";
    public ISection? Map(IPublishedElement e) => new ComponentSection(new NewsletterFormViewModel
    {
        Text     = text.Read(e),
        Cta      = cta.Read(e),
        Async    = asyncReader.Read(e),
        DomClass = cls.Read(e)
    });
}
