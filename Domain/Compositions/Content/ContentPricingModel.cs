namespace Synergos.CMS.Domain.Compositions.Content;

/// <summary>
/// Editor-authored pricing fields. Every value is the literal string the editor
/// typed — the view renders it as-is, no parsing, no formatting, no currency
/// symbol injection. Enables any format: "$99", "Gratis", "1.500.000 COP",
/// "Desde 19€ +IVA", "Consultar".
///
/// When <see cref="PriceAmount"/> is null/empty the entire block is expected
/// to hide itself (the element's renderer decides). <see cref="PriceTaxIncluded"/>
/// is advisory — viewers / views may render a badge "IVA incluido" when true.
/// </summary>
public sealed record ContentPricingModel(
    string? PriceAmount,
    string? PriceCurrency,
    string? PriceOriginal,
    string? PricePeriod,
    string? PriceSuffix,
    bool    PriceTaxIncluded);
