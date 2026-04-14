namespace Synergos.CMS.Schema.Constants.BlockGrid;

/// <summary>
/// Visual variants applied to component-style blocks (Hero, Banner, CtaBanner,
/// Card, Section, etc.). Maps to a CMS DropDown.Flexible value.
///
/// The available set is small and stable — typed enum prevents
/// "drak"/"darks"/"darkmode" type drift across seeders and content.
/// </summary>
public enum BlockVariant
{
    Light,
    Dark,
    Primary,
    Secondary,
    Accent,
    Muted
}

/// <summary>
/// Extensions for converting <see cref="BlockVariant"/> to and from
/// Umbraco dropdown values.
/// </summary>
public static class BlockVariantExtensions
{
    /// <summary>Returns the lowercase string Umbraco stores in the dropdown.</summary>
    public static string ToAlias(this BlockVariant variant) => variant switch
    {
        BlockVariant.Light     => "light",
        BlockVariant.Dark      => "dark",
        BlockVariant.Primary   => "primary",
        BlockVariant.Secondary => "secondary",
        BlockVariant.Accent    => "accent",
        BlockVariant.Muted     => "muted",
        _                       => "light"
    };

    /// <summary>
    /// Parses a stored variant alias back to <see cref="BlockVariant"/>.
    /// Returns <see cref="BlockVariant.Light"/> when unknown or null.
    /// </summary>
    public static BlockVariant FromAlias(string? alias) => alias switch
    {
        "light"     => BlockVariant.Light,
        "dark"      => BlockVariant.Dark,
        "primary"   => BlockVariant.Primary,
        "secondary" => BlockVariant.Secondary,
        "accent"    => BlockVariant.Accent,
        "muted"     => BlockVariant.Muted,
        _           => BlockVariant.Light
    };
}
