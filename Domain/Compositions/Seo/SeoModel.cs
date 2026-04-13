using Synergos.CMS.Domain.Shared;

namespace Synergos.CMS.Domain.Compositions.Seo;

/// <summary>
/// Modelo de dominio para la composición Comp.Seo.
/// Agrupa metadatos SEO técnicos y Open Graph social.
/// </summary>
public sealed record SeoModel(
    string? SeoTitle,
    string? SeoDescription,
    string? OgTitle,
    string? OgDescription,
    Image?  OgImage,
    string? Canonical,
    string? Robots);
