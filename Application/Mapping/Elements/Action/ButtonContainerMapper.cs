using Umbraco.Cms.Core.Models.PublishedContent;
using Synergos.CMS.Application.Elements;
using Synergos.CMS.Application.Mapping.Compositions.Dom;
using Synergos.CMS.Application.Output;
using Synergos.CMS.Domain.Sections;
using UmbracoLink = Umbraco.Cms.Core.Models.Link;

namespace Synergos.CMS.Application.Mapping.Elements;

public sealed class ButtonContainerMapper(DomClassReader cls) : ISectionMapper
{
    public string SupportedAlias => "elementActionButtonContainer";
    public ISection? Map(IPublishedElement element) => new ComponentSection(new ButtonContainerViewModel
    {
        Label    = element.Value<string>("label"),
        Href     = element.Value<UmbracoLink>("href")?.Url,
        Target   = element.Value<string>("target"),
        DomClass = cls.Read(element)
    });
}
