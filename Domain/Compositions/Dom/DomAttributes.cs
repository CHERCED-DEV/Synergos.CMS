namespace Synergos.CMS.Domain.Compositions.Dom;

/// <summary>
/// HTML attributes composition — mirrors UI DomAttributes contract.
/// </summary>
public sealed record DomAttributes(
    string? ElementId = null,
    string? DataAttributes = null,
    string? AriaLabel = null,
    string? Role = null);
