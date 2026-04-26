namespace Synergos.CMS.Application.Constants;

/// <summary>
/// Const string mirrors of the most-used DTSelect* DataType options
/// (Ola 74). Provides compile-time check + IDE autocomplete to runtime
/// code that branches on dropdown values, replacing magic strings like
/// <c>renderCtx.ChromeMode == "bare"</c> with
/// <c>renderCtx.ChromeMode == DropdownOptions.ChromeMode.Bare</c>.
/// </summary>
/// <remarks>
/// <para>
/// SOURCE OF TRUTH: the XML files in
/// <c>Synergos.CMS.Web/uSync/v9/DataTypes/DTSelect*.config</c>. If you
/// add/remove an option there, mirror it here. ADR 0042 documents this
/// dual-write trade-off (small maintenance cost in exchange for typed
/// runtime code).
/// </para>
/// <para>
/// Only the 13 DTSelects that runtime C# branches on are mirrored.
/// Editorial-only dropdowns (e.g. DTSelectAriaRole, DTSelectAspectRatio)
/// stay as raw strings — the value is just passed through to HTML
/// attributes without conditional logic.
/// </para>
/// </remarks>
public static class DropdownOptions
{
    /// <summary>Mirror de <c>DTSelectPageThemeVariant</c>.</summary>
    public static class PageThemeVariant
    {
        public const string Inherit = "inherit";
        public const string Light = "light";
        public const string Dark = "dark";
        public const string SilverGold = "silverGold";
        public const string Brand = "brand";
    }

    /// <summary>Mirror de <c>DTSelectPageSurface</c>.</summary>
    public static class PageSurface
    {
        public const string Default = "default";
        public const string Muted = "muted";
        public const string Contrast = "contrast";
        public const string Brand = "brand";
        public const string Transparent = "transparent";
    }

    /// <summary>Mirror de <c>DTSelectChromeMode</c>.</summary>
    public static class ChromeMode
    {
        public const string Inherit = "inherit";
        public const string Full = "full";
        public const string Minimal = "minimal";
        public const string Bare = "bare";
        public const string Embedded = "embedded";
        public const string None = "none";
    }

    /// <summary>Mirror de <c>DTSelectHeaderMode</c>.</summary>
    public static class HeaderMode
    {
        public const string Inherit = "inherit";
        public const string Visible = "visible";
        public const string Hidden = "hidden";
        public const string Transparent = "transparent";
        public const string Sticky = "sticky";
        public const string TransparentSticky = "transparentSticky";
    }

    /// <summary>Mirror de <c>DTSelectFooterMode</c>.</summary>
    public static class FooterMode
    {
        public const string Inherit = "inherit";
        public const string Visible = "visible";
        public const string Hidden = "hidden";
        public const string Minimal = "minimal";
    }

    /// <summary>Mirror de <c>DTSelectAlertVariant</c>.</summary>
    public static class AlertVariant
    {
        public const string Info = "info";
        public const string Success = "success";
        public const string Warning = "warning";
        public const string Danger = "danger";
        public const string Promo = "promo";
        public const string Neutral = "neutral";
        public const string Brand = "brand";
    }

    /// <summary>Mirror de <c>DTSelectAlertTone</c>.</summary>
    public static class AlertTone
    {
        public const string Soft = "soft";
        public const string Solid = "solid";
        public const string Outline = "outline";
        public const string Glass = "glass";
        public const string Minimal = "minimal";
    }

    /// <summary>Mirror de <c>DTSelectBannerPlacement</c>.</summary>
    public static class BannerPlacement
    {
        public const string Top = "top";
        public const string Bottom = "bottom";
    }

    /// <summary>Mirror de <c>DTSelectModalTrigger</c>.</summary>
    public static class ModalTrigger
    {
        public const string Immediate = "immediate";
        public const string Scroll = "scroll";
        public const string Exit = "exit";
        public const string Manual = "manual";
    }

    /// <summary>Mirror de <c>DTSelectModalFrequency</c>.</summary>
    public static class ModalFrequency
    {
        public const string Always = "always";
        public const string Once = "once";
        public const string Daily = "daily";
        public const string Session = "session";
    }

    /// <summary>Mirror de <c>DTSelectContainerType</c>.</summary>
    public static class ContainerType
    {
        public const string FullBleed = "full-bleed";
        public const string Normal = "normal";
        public const string Narrow = "narrow";
        public const string UltraNarrow = "ultra-narrow";
    }

    /// <summary>Mirror de <c>DTSelectSpacingScale</c>.</summary>
    public static class SpacingScale
    {
        public const string None = "none";
        public const string Xs = "xs";
        public const string Sm = "sm";
        public const string Md = "md";
        public const string Lg = "lg";
        public const string Xl = "xl";
    }

    /// <summary>Mirror de <c>DTSelectShopSort</c>.</summary>
    public static class ShopSort
    {
        public const string Name = "name";
        public const string PriceAsc = "price-asc";
        public const string PriceDesc = "price-desc";
        public const string DateDesc = "date-desc";
        public const string Popularity = "popularity";
    }
}
