using Umbraco.Cms.Core.Models.PublishedContent;
using Synergos.CMS.Domain.Compositions.Content;
using static Synergos.CMS.Application.Mapping.Compositions.PropertyAliases.Pricing;

namespace Synergos.CMS.Application.Mapping.Compositions.Content;

/// <summary>
/// Reads <c>compContentPricing</c> properties from any element that composes the
/// Pricing composition. Stateless singleton — registered in
/// <see cref="Application.ServiceCollectionExtensions.AddSynergosCompositions"/>.
/// </summary>
public sealed class ContentPricingReader : ICompositionReader<ContentPricingModel>
{
    public ContentPricingModel Read(IPublishedElement element) => new(
        PriceAmount:      element.Value<string>(PriceAmount),
        PriceCurrency:    element.Value<string>(PriceCurrency),
        PriceOriginal:    element.Value<string>(PriceOriginal),
        PricePeriod:      element.Value<string>(PricePeriod),
        PriceSuffix:      element.Value<string>(PriceSuffix),
        PriceTaxIncluded: element.Value<bool>(PriceTaxIncluded));
}
