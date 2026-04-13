namespace Synergos.CMS.Domain.Compositions.Content;

/// <summary>
/// Embed composition — mirrors UI ContentEmbed contract.
/// </summary>
public sealed record ContentEmbed(
    string? EmbedUrl = null,
    string? EmbedType = null,
    string? EmbedTitle = null);
