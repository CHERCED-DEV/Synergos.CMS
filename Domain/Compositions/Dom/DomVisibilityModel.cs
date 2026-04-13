namespace Synergos.CMS.Domain.Compositions.Dom;

/// <summary>
/// Visibilidad responsive declarada desde el CMS.
/// El renderer aplica clases utilitarias — nunca inline style.
/// </summary>
public sealed record DomVisibilityModel(
    bool HideOnMobile,
    bool HideOnDesktop);
