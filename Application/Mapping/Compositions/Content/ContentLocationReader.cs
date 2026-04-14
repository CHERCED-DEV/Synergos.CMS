using Umbraco.Cms.Core.Models.PublishedContent;
using Synergos.CMS.Domain.Compositions.Content;
using static Synergos.CMS.Application.Mapping.Compositions.PropertyAliases.Location;

namespace Synergos.CMS.Application.Mapping.Compositions.Content;

/// <summary>
/// Reads <c>compContentLocation</c> properties from any element that composes the
/// Location composition. Stateless singleton — registered in
/// <see cref="Application.ServiceCollectionExtensions.AddSynergosCompositions"/>.
/// </summary>
public sealed class ContentLocationReader : ICompositionReader<ContentLocationModel>
{
    public ContentLocationModel Read(IPublishedElement element) => new(
        AddressLine:   element.Value<string>(AddressLine),
        City:          element.Value<string>(City),
        Region:        element.Value<string>(Region),
        PostalCode:    element.Value<string>(PostalCode),
        Country:       element.Value<string>(Country),
        Latitude:      element.Value<string>(Latitude),
        Longitude:     element.Value<string>(Longitude),
        GoogleMapsUrl: element.Value<string>(GoogleMapsUrl));
}
