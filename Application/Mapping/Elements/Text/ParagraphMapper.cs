using Umbraco.Cms.Core.Models.PublishedContent;
using Synergos.CMS.Application.Elements;
using Synergos.CMS.Application.Mapping.Compositions.Content;
using Synergos.CMS.Application.Mapping.Compositions.Dom;
using Synergos.CMS.Application.Output;
using Synergos.CMS.Domain.Sections;

namespace Synergos.CMS.Application.Mapping.Elements;

public sealed class ParagraphMapper(
    ContentTextReader textReader,
    DomClassReader    cls) : ISectionMapper
{
    public string SupportedAlias => "elementTextParagraph";
    public ISection? Map(IPublishedElement e) => new ComponentSection(new ParagraphViewModel
    {
        Text     = textReader.Read(e),
        DomClass = cls.Read(e)
    });
}
