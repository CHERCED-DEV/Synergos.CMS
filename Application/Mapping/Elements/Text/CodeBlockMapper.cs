using Umbraco.Cms.Core.Models.PublishedContent;
using Synergos.CMS.Application.Elements;
using Synergos.CMS.Application.Mapping.Compositions.Dom;
using Synergos.CMS.Application.Output;
using Synergos.CMS.Domain.Sections;

namespace Synergos.CMS.Application.Mapping.Elements;

public sealed class CodeBlockMapper(DomClassReader cls) : ISectionMapper
{
    public string SupportedAlias => "elementTextCodeBlock";
    public ISection? Map(IPublishedElement element) => new ComponentSection(new CodeBlockViewModel
    {
        Language = element.Value<string>("codeLanguage"),
        Content  = element.Value<string>("codeContent"),
        DomClass = cls.Read(element)
    });
}
