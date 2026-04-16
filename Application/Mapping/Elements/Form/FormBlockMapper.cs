using Umbraco.Cms.Core.Models.PublishedContent;
using Synergos.CMS.Application.Elements;
using Synergos.CMS.Application.Mapping.Compositions.Dom;
using Synergos.CMS.Application.Output;
using Synergos.CMS.Domain.Sections;

namespace Synergos.CMS.Application.Mapping.Elements;

/// <summary>
/// Maps elementCompFormBlock → FormBlockViewModel. Reads inline form config
/// fields (alias/title/submitLabel/successMessage) from the block element.
/// </summary>
public sealed class FormBlockMapper(
    DomClassReader   cls,
    DomVariantReader variant) : ISectionMapper
{
    public string SupportedAlias => "elementCompFormBlock";
    public ISection? Map(IPublishedElement e) => new ComponentSection(new FormBlockViewModel
    {
        FormAlias      = e.Value<string>("formAlias"),
        FormTitle      = e.Value<string>("formTitle"),
        SubmitLabel    = e.Value<string>("submitLabel"),
        SuccessMessage = e.Value<string>("successMessage"),
        DomClass       = cls.Read(e),
        DomVariant     = variant.Read(e)
    });
}
