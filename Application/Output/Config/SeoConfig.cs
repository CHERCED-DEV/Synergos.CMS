namespace Synergos.CMS.Application.Output.Config;

/// <summary>
/// SEO metadata serializable para consumo desde Angular.
/// Mapea desde Domain.Shared.SeoMetadata.
/// </summary>
public sealed record SeoConfig(
    string? Title,
    string? Description,
    string? ImageUrl,
    bool    NoIndex,
    string? CanonicalUrl
);
