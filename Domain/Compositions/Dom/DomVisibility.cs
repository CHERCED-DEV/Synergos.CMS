namespace Synergos.CMS.Domain.Compositions.Dom;

/// <summary>
/// Responsive visibility composition — mirrors UI DomVisibility contract.
/// </summary>
public sealed record DomVisibility(
    bool HideOnMobile = false,
    bool HideOnDesktop = false);
