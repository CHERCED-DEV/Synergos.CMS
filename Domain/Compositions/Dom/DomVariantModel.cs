namespace Synergos.CMS.Domain.Compositions.Dom;

/// <summary>
/// Variante visual y esquema de color del componente.
/// Variant y Theme son tokens abstractos — el renderer los resuelve a clases del design system.
/// </summary>
public sealed record DomVariantModel(
    string? Variant,
    string? Theme);
