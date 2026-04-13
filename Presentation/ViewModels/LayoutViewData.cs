namespace Synergos.CMS.Presentation.ViewModels;

/// <summary>
/// Stable string constants for ViewData keys shared between page views and _Layout.cshtml.
/// Use these instead of raw string literals to prevent typo-driven silent failures.
/// </summary>
public static class LayoutViewData
{
    /// <summary>Value applied to the <body> id attribute.</summary>
    public const string BodyId    = "BodyId";

    /// <summary>CSS class(es) applied to the <body> element.</summary>
    public const string BodyClass = "BodyClass";

    /// <summary>Override for the page <title> and OG title resolved in SeoMeta.cshtml.</summary>
    public const string SeoTitle  = "SeoTitle";

    /// <summary>
    /// Extra CSS class added to the &lt;main&gt; element from LayoutProfile.mainWrapperStyle.
    /// Set by page views that have the CompDomLayoutProfile composition; null otherwise.
    /// </summary>
    public const string MainWrapperStyle = "MainWrapperStyle";
}
