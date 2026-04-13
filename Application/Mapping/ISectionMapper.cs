using Umbraco.Cms.Core.Models.PublishedContent;
using Synergos.CMS.Domain.Sections;

namespace Synergos.CMS.Application.Mapping;

/// <summary>
/// Contrato para los mappers de sección.
///
/// Cada implementación es responsable de un único tipo de sección.
/// El SectionMapperDispatcher resuelve la implementación correcta
/// por alias de Content Type, siguiendo Open/Closed Principle:
/// agregar una sección nueva = agregar una clase, no modificar ninguna existente.
/// </summary>
public interface ISectionMapper
{
    /// <summary>
    /// Alias del Content Type de Umbraco que este mapper sabe convertir.
    /// Ej: "macroHero", "macroCards".
    /// </summary>
    string SupportedAlias { get; }

    /// <summary>
    /// Convierte un elemento de Umbraco en una sección del dominio.
    /// Devuelve null si el elemento no tiene datos suficientes para
    /// construir una sección válida (campos requeridos vacíos).
    /// La visibilidad ya fue evaluada antes de llamar a este método.
    /// </summary>
    ISection? Map(IPublishedElement element);
}
