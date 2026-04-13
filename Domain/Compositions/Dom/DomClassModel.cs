namespace Synergos.CMS.Domain.Compositions.Dom;

/// <summary>
/// Clases CSS declarativas asignadas desde el CMS.
/// El renderer las aplica al elemento raíz sin interpretación adicional.
/// Usar tokens del design system, no valores ad-hoc.
/// </summary>
public sealed record DomClassModel(string? ClassList);
