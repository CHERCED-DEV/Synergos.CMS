using Umbraco.Cms.Core.Models.PublishedContent;
using Synergos.CMS.Application.Elements;
using Synergos.CMS.Application.Mapping.Compositions.Content;
using Synergos.CMS.Application.Mapping.Compositions.Dom;
using Synergos.CMS.Application.Output;
using Synergos.CMS.Domain.Sections;

namespace Synergos.CMS.Application.Mapping.Elements;

public sealed class AlertBoxMapper(
    ContentTextReader text,
    DomClassReader    cls) : ISectionMapper
{
    public string SupportedAlias => "elementCorpAlertBox";
    public ISection? Map(IPublishedElement element) => new ComponentSection(new AlertBoxViewModel
    {
        Text        = text.Read(element),
        AlertType   = element.Value<string>("alertType"),
        Dismissible = element.Value<bool?>("dismissible"),
        DomClass    = cls.Read(element)
    });
}
