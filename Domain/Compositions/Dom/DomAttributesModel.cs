namespace Synergos.CMS.Domain.Compositions.Dom;

/// <summary>
/// Atributos HTML declarativos: id, data-*, aria-label y role.
/// Clave para accesibilidad, targeting analítico y scraping semántico.
/// DataAttributes se almacena como JSON y el renderer lo expande.
/// </summary>
public sealed record DomAttributesModel(
    string? ElementId,
    string? DataAttributes,
    string? AriaLabel,
    string? Role);
