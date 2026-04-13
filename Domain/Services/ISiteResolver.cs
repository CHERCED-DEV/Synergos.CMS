namespace Synergos.CMS.Domain.Services;

/// <summary>
/// Resolves the current site context from the incoming request.
/// Supports multi-site / multi-domain within a single Umbraco instance.
/// </summary>
public interface ISiteResolver
{
    /// <summary>Resolve site context for the current request.</summary>
    SiteContext Resolve();

    /// <summary>Fallback when no request context is available.</summary>
    SiteContext ResolveFallback();

    /// <summary>
    /// Returns available language/culture options for the culture switcher.
    /// Returns an empty list when only one SiteRoot exists (no switcher needed).
    /// </summary>
    IReadOnlyList<CultureOption> GetCultureOptions(string currentCulture);
}

/// <summary>Immutable snapshot of the resolved site.</summary>
public sealed record SiteContext(
    int RootNodeId,
    string SiteName,
    string Domain,
    string Culture);

/// <summary>A single language option for the culture switcher in the site header.</summary>
public sealed record CultureOption(
    string Label,
    string Culture,
    string Url,
    bool   IsActive);
