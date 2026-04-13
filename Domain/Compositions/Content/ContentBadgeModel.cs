namespace Synergos.CMS.Domain.Compositions.Content;

/// <summary>
/// Etiqueta de estado o categoría asociada a un componente.
/// BadgeType mapea a tokens visuales (info, success, warning) en el renderer.
/// </summary>
public sealed record ContentBadgeModel(
    string? BadgeText,
    string? BadgeType);
