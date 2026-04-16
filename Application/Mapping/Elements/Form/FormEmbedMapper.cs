using Umbraco.Cms.Core.Models.PublishedContent;
using Synergos.CMS.Application.Elements;
using Synergos.CMS.Application.Mapping.Compositions.Dom;
using Synergos.CMS.Application.Output;
using Synergos.CMS.Application.Services;
using Synergos.CMS.Domain.Sections;

namespace Synergos.CMS.Application.Mapping.Elements;

/// <summary>
/// Maps elementFormEmbed → FormEmbedViewModel.
/// Resolves the ContentPicker reference to a FormDefinition via FormRenderService.
/// </summary>
public sealed class FormEmbedMapper(
    FormRenderService formService,
    DomClassReader    cls,
    DomVariantReader  variant) : ISectionMapper
{
    public string SupportedAlias => "elementFormEmbed";

    public ISection? Map(IPublishedElement e)
    {
        var pickedForm = e.Value<IPublishedContent>("formPicker");
        var resolvedForm = formService.ResolveFromContent(pickedForm);

        return new ComponentSection(new FormEmbedViewModel
        {
            Form       = resolvedForm,
            Heading    = e.Value<string>("formHeading"),
            Subheading = e.Value<string>("formSubheading"),
            DomClass   = cls.Read(e),
            DomVariant = variant.Read(e)
        });
    }
}
