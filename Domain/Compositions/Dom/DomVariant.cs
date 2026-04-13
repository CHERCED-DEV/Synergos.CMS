namespace Synergos.CMS.Domain.Compositions.Dom;

/// <summary>
/// Visual variant/theme composition — mirrors UI DomVariant contract.
/// </summary>
public sealed record DomVariant(
    string? Variant = null,
    string? Theme = null);
