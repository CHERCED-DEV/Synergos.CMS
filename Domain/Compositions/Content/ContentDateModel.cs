namespace Synergos.CMS.Domain.Compositions.Content;

/// <summary>
/// Fechas editoriales de publicación y actualización.
/// Nulas cuando el contenido no ha sido publicado o no tiene fecha de actualización.
/// </summary>
public sealed record ContentDateModel(
    DateTime? PublishDate,
    DateTime? UpdateDate);
