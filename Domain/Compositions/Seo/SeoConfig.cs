namespace Synergos.CMS.Domain.Compositions.Seo;

/// <summary>
/// SEO configuration — mirrors UI SeoConfig contract.
/// Distinct from Domain/Shared/SeoMetadata which is the internal domain model.
/// This record is the serializable shape that cms-sync generates into TypeScript.
/// </summary>
public sealed record SeoConfig(
    string? Title = null,
    string? Description = null,
    string? ImageUrl = null,
    bool NoIndex = false,
    string? CanonicalUrl = null);
