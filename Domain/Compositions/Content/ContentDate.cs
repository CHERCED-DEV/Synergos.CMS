namespace Synergos.CMS.Domain.Compositions.Content;

/// <summary>
/// Date composition — mirrors UI ContentDate contract.
/// </summary>
public sealed record ContentDate(
    string? PublishDate = null,
    string? UpdateDate = null);
