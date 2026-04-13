namespace Synergos.CMS.Domain.Compositions.Visibility;

/// <summary>
/// Modelo de dominio para la composición Comp.Visibility.
/// Visibilidad editorial: scheduling, ocultado manual y condiciones.
/// Distinto de DomVisibilityModel (CSS/responsive).
/// </summary>
public sealed record VisibilityModel(
    bool      IsHidden,
    DateTime? VisibilityStart,
    DateTime? VisibilityEnd,
    string?   VisibilityCondition);
