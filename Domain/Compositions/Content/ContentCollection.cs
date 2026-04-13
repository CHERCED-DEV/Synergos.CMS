namespace Synergos.CMS.Domain.Compositions.Content;

/// <summary>
/// Collection composition — mirrors UI ContentCollection contract.
/// </summary>
public sealed record ContentCollection(
    string? CollectionTitle = null,
    string? CollectionDescription = null);
