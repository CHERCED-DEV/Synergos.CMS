using Synergos.CMS.Domain.Sections;

namespace Synergos.CMS.Application.Output;

/// <summary>
/// Contrato de salida del SectionMapperDispatcher.
///
/// Combina una sección de dominio tipada con sus atributos de presentación HTML.
/// Vivir en Application/Output/ garantiza que Application no dependa de Presentation:
/// la capa que produce el tipo es la que lo define.
///
/// Presentation consume este tipo; nunca al revés.
/// </summary>
public sealed record SectionView(
    ISection Section,
    string?  ElementId,
    string?  CssClasses);
