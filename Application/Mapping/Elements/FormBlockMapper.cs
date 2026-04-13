using Umbraco.Cms.Core.Models.PublishedContent;
using Synergos.CMS.Application.Elements;
using Synergos.CMS.Application.Output;
using Synergos.CMS.Domain.Sections;

namespace Synergos.CMS.Application.Mapping.Elements;

public sealed class FormBlockMapper : ISectionMapper
{
    public string SupportedAlias => "elementCompFormBlock";

    public ISection? Map(IPublishedElement e) => new ComponentSection(new FormBlockViewModel
    {
        FormAlias      = e.Value<string>("formAlias"),
        FormTitle      = e.Value<string>("formTitle"),
        SubmitLabel    = e.Value<string>("submitLabel"),
        SuccessMessage = e.Value<string>("successMessage")
    });
}
