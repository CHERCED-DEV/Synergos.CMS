namespace Synergos.CMS.Domain.Compositions.Dom;

/// <summary>
/// CSS class list composition — mirrors UI DomClass contract.
/// </summary>
public sealed record DomClass(
    string? ClassList = null);
