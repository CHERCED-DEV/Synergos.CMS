namespace Synergos.CMS.Domain.Compositions.Content;

/// <summary>
/// Text composition — mirrors UI ContentText contract.
/// </summary>
public sealed record ContentText(
    string? Title = null,
    string? Subtitle = null,
    string? Body = null,
    string? Summary = null,
    string? Caption = null);
