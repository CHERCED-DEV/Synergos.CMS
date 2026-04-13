namespace Synergos.CMS.Domain.Compositions.Dom;

/// <summary>
/// Spacing composition — mirrors UI DomSpacing contract.
/// </summary>
public sealed record DomSpacing(
    string? Margin = null,
    string? Padding = null,
    string? Gap = null);
