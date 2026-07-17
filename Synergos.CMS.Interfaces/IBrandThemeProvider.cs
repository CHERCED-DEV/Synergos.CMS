namespace Synergos.CMS.Interfaces;

/// <summary>
/// Resolves the theming values (colours, fonts, logo URLs) for a given
/// brand key. Complements <see cref="IBrandingProvider"/> which exposes
/// the brand identity; this provider exposes the visual surface
/// sourced from the Umbraco Settings tree (ADR 0020).
/// </summary>
/// <remarks>
/// Default implementation <c>DefaultBrandThemeProvider</c> lives in
/// <c>Synergos.CMS.Web</c> because it reads the Umbraco content tree
/// (<c>themeSettings</c> nodes with matching <c>brandKey</c>). Per
/// ADR 0010, consumers MUST NOT branch on <see cref="BrandTheme"/>
/// fields with if/else per brand; branching belongs in a custom
/// <see cref="IBrandThemeProvider"/> impl, not in core code.
/// </remarks>
public interface IBrandThemeProvider
{
    /// <summary>
    /// Resolves the theme for a brand key. Returns <c>null</c> when
    /// no <c>themeSettings</c> node matches — the caller should fall
    /// back to design-system defaults.
    /// </summary>
    BrandTheme? GetThemeForBrand(string brandKey);
}

/// <summary>
/// Immutable projection of a <c>themeSettings</c> node (Ola 31, ADR 0020).
/// All fields are nullable because editors can leave them blank — the
/// consumer must degrade gracefully.
/// </summary>
/// <param name="PrimaryColor">Hex color (e.g. <c>"#ff6600"</c>).</param>
/// <param name="SecondaryColor">Hex color.</param>
/// <param name="AccentColor">Hex color.</param>
/// <param name="BackgroundColor">Hex color.</param>
/// <param name="TextColor">Hex color.</param>
/// <param name="HeadingFontFamily">CSS font-family stack for headings.</param>
/// <param name="BodyFontFamily">CSS font-family stack for body text.</param>
/// <param name="LogoLightUrl">Resolved URL for the light-background logo.</param>
/// <param name="LogoDarkUrl">Resolved URL for the dark-background logo.</param>
/// <param name="FaviconUrl">Resolved URL for the favicon.</param>
public sealed record BrandTheme(
    string? PrimaryColor,
    string? SecondaryColor,
    string? AccentColor,
    string? BackgroundColor,
    string? TextColor,
    string? HeadingFontFamily,
    string? BodyFontFamily,
    string? LogoLightUrl,
    string? LogoDarkUrl,
    string? FaviconUrl);
