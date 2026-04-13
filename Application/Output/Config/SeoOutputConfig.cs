namespace Synergos.CMS.Application.Output.Config;

/// <summary>
/// SEO output for API responses — mirrors UI SeoConfig contract.
/// </summary>
public sealed record SeoOutputConfig(
    string? Title = null,
    string? Description = null,
    string? ImageUrl = null,
    bool NoIndex = false,
    string? CanonicalUrl = null);
