using Synergos.CMS.Domain.Shared;

namespace Synergos.CMS.Domain.Compositions.Content;

/// <summary>
/// Autoría editorial: nombre, rol e imagen del autor.
/// Usado principalmente en artículos y contenido de blog.
/// </summary>
public sealed record ContentAuthorModel(
    string? AuthorName,
    string? AuthorRole,
    Image?  AuthorImage);
