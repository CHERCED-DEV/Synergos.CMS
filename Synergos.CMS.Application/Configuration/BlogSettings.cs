namespace Synergos.CMS.Application.Configuration;

/// <summary>
/// Typed POCO bound from <c>appsettings.*.json</c> section <c>Synergos:Blog</c>
/// (ADR 0027). Ajustes de presentación del blog: tamaño de página de categoría
/// y cuántos posts relacionados mostrar.
/// </summary>
public sealed class BlogSettings
{
    /// <summary>Posts por página en la landing de categoría. Default 12. Mínimo efectivo 1.</summary>
    public int CategoryPageSize { get; init; } = 12;

    /// <summary>Cuántos posts relacionados mostrar al pie de un post. Default 3. 0 = ocultar.</summary>
    public int RelatedPostsCount { get; init; } = 3;
}
