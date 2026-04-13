namespace Synergos.CMS.Configuration;

/// <summary>
/// Definition of a single page created by <c>ContentSeeder</c> under the SiteRoot.
/// Only the <see cref="Name"/> is mandatory — other fields fill empty properties
/// via <c>SetIfEmpty</c> and are never overwritten once the editor sets a value.
///
/// Override per deployment via appsettings.json section "Synergos:Seed:Pages":
/// <code>
/// "Pages": [
///   { "Name": "Home", "Subtitle": "...", "SeoTitle": "...", "SeoDescription": "...", "OgTitle": "...", "OgDescription": "..." },
///   ...
/// ]
/// </code>
/// </summary>
public sealed class SeedPage
{
    /// <summary>Node name used for both the Umbraco node and the pageTitle property.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Optional subtitle shown below the page H1. Culture-variant.</summary>
    public string Subtitle { get; init; } = string.Empty;

    /// <summary>SEO title used in the &lt;title&gt; tag. Falls back to pageTitle when empty.</summary>
    public string SeoTitle { get; init; } = string.Empty;

    /// <summary>Meta description for search engines.</summary>
    public string SeoDescription { get; init; } = string.Empty;

    /// <summary>Open Graph title for social sharing. Falls back to SeoTitle when empty.</summary>
    public string OgTitle { get; init; } = string.Empty;

    /// <summary>Open Graph description for social sharing. Falls back to SeoDescription when empty.</summary>
    public string OgDescription { get; init; } = string.Empty;
}
