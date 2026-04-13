namespace Synergos.CMS.Domain.Compositions.Dom;

/// <summary>
/// Layout composition — mirrors UI DomLayout contract.
/// </summary>
public sealed record DomLayout(
    string? ContainerType = null,
    string? Alignment = null,
    string? Direction = null);
