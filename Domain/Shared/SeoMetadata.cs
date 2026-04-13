namespace Synergos.CMS.Domain.Shared;

/// <summary>
/// Metadatos SEO de una página. Agrupa todo lo necesario para
/// construir las etiquetas meta, Open Graph y canonical en la vista.
/// </summary>
public sealed record SeoMetadata(
    string?  Title,
    string?  Description,
    Image?   Image,
    bool     NoIndex,
    string?  CanonicalUrl,
    string?  TitleSuffix = null);
