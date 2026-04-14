namespace Synergos.CMS.Schema.Constants.BlockGrid;

/// <summary>
/// HTML heading levels surfaced as a typed enum so seeders and assemblers
/// don't sprinkle "h1"/"h2" string literals through the codebase.
///
/// Stored value in Umbraco DropDown.Flexible properties: lowercase tag name
/// (e.g. "h2") wrapped in a single-element JSON array (e.g. <c>["h2"]</c>).
/// Use <see cref="HeadingLevelExtensions.ToAlias"/> to obtain the storage
/// representation.
/// </summary>
public enum HeadingLevel
{
    H1,
    H2,
    H3,
    H4,
    H5,
    H6
}

/// <summary>
/// Extensions for converting <see cref="HeadingLevel"/> to and from the
/// string representation Umbraco uses in dropdown property values.
/// </summary>
public static class HeadingLevelExtensions
{
    /// <summary>
    /// Returns the lowercase HTML tag name ("h1", "h2", …) used as the
    /// dropdown value in Umbraco.
    /// </summary>
    public static string ToAlias(this HeadingLevel level) => level switch
    {
        HeadingLevel.H1 => "h1",
        HeadingLevel.H2 => "h2",
        HeadingLevel.H3 => "h3",
        HeadingLevel.H4 => "h4",
        HeadingLevel.H5 => "h5",
        HeadingLevel.H6 => "h6",
        _               => "h2"
    };

    /// <summary>
    /// Parses a dropdown alias ("h1"…"h6") back to a <see cref="HeadingLevel"/>.
    /// Returns <see cref="HeadingLevel.H2"/> when the alias is unknown or null.
    /// </summary>
    public static HeadingLevel FromAlias(string? alias) => alias switch
    {
        "h1" => HeadingLevel.H1,
        "h2" => HeadingLevel.H2,
        "h3" => HeadingLevel.H3,
        "h4" => HeadingLevel.H4,
        "h5" => HeadingLevel.H5,
        "h6" => HeadingLevel.H6,
        _    => HeadingLevel.H2
    };
}
