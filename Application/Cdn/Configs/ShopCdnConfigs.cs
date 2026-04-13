namespace Synergos.CMS.Application.Cdn.Configs;

// ── CDN Shop Configs ──────────────────────────────────────────────────────────
// Mirror of vitals/contracts/src/element-config.contract.ts — Shop tier.
// Translations: assembled at render time from Umbraco dictionary; never a macro param.
// GUIDs: cf000001–cf000008  (see ContentTypeKeys.cs)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Mirror of ProductCardElementConfig. SKU is fetched from API at runtime.</summary>
public sealed record ProductCardCdnConfig(
    string?  ProductSku      = null,
    string?  Name            = null,   // editorial override — API value used if null
    string?  ImageSrc        = null,   // editorial override
    string?  ImageAlt        = null,
    bool?    ShowPrice       = null,
    bool?    ShowBadge       = null,
    string?  Layout          = null,   // vertical | horizontal
    string?  Theme           = null,
    string?  Variant         = null,
    IReadOnlyDictionary<string, string>? Translations = null);

/// <summary>Mirror of ProductGridElementConfig.</summary>
public sealed record ProductGridCdnConfig(
    string?  HeadingText     = null,
    string?  CategoryAlias   = null,   // /api/shop/categories/{alias}
    int?     MaxItems        = null,
    int?     Columns         = null,   // 2 | 3 | 4
    bool?    ShowFilters     = null,
    string?  SortOrder       = null,   // relevance | newest | price-asc | price-desc
    string?  Theme           = null,
    string?  Variant         = null,
    IReadOnlyDictionary<string, string>? Translations = null);

/// <summary>Mirror of ProductDetailElementConfig.</summary>
public sealed record ProductDetailCdnConfig(
    string?  ProductSku          = null,
    bool?    ShowVariantPicker   = null,
    bool?    ShowQuantitySelector = null,
    bool?    ShowRating          = null,
    string?  Layout              = null,   // imageLeft | imageRight | imageTop
    string?  Theme               = null,
    string?  Variant             = null,
    IReadOnlyDictionary<string, string>? Translations = null);

/// <summary>Mirror of CartSummaryElementConfig.</summary>
public sealed record CartSummaryCdnConfig(
    bool?    ShowCoupon          = null,
    string?  CheckoutUrl         = null,
    string?  ContinueShoppingUrl = null,
    string?  Theme               = null,
    string?  Variant             = null,
    IReadOnlyDictionary<string, string>? Translations = null);

/// <summary>Mirror of CartItemElementConfig. Rendered internally by CartSummary.</summary>
public sealed record CartItemCdnConfig(
    string?  Theme               = null,
    string?  Variant             = null,
    IReadOnlyDictionary<string, string>? Translations = null);

/// <summary>Mirror of PriceDisplayElementConfig.</summary>
public sealed record PriceDisplayCdnConfig(
    bool?    ShowOriginalPrice   = null,
    bool?    ShowDiscount        = null,
    string?  PriceSize          = null,   // sm | md | lg
    string?  Currency           = null,   // ISO 4217 override
    string?  Theme               = null,
    string?  Variant             = null,
    IReadOnlyDictionary<string, string>? Translations = null);

/// <summary>Mirror of QuantitySelectorElementConfig.</summary>
public sealed record QuantitySelectorCdnConfig(
    int?     Min                 = null,   // default: 1
    int?     Max                 = null,   // default: 99
    int?     Step                = null,   // default: 1
    string?  Theme               = null,
    string?  Variant             = null,
    IReadOnlyDictionary<string, string>? Translations = null);

/// <summary>Mirror of VariantPickerElementConfig.</summary>
public sealed record VariantPickerCdnConfig(
    string?  VariantType         = null,   // color | size | storage | custom
    string?  DisplayAs           = null,   // buttons | swatches | dropdown
    string?  Theme               = null,
    string?  Variant             = null,
    IReadOnlyDictionary<string, string>? Translations = null);
