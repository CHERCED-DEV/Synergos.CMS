using Synergos.CMS.Domain.Shared;

namespace Synergos.CMS.Domain.Compositions.Content;

/// <summary>
/// Author composition — mirrors UI ContentAuthor contract.
/// </summary>
public sealed record ContentAuthor(
    string? AuthorName = null,
    string? AuthorRole = null,
    Image? AuthorImage = null);
