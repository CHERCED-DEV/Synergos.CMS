namespace Synergos.CMS.Domain.Compositions.Dom;

/// <summary>
/// Comportamiento estructural del layout: tipo de contenedor, alineación y dirección.
/// Los valores son tokens semánticos — el renderer los mapea a clases o CSS variables.
/// </summary>
public sealed record DomLayoutModel(
    string? ContainerType,
    string? Alignment,
    string? Direction);
