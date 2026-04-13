namespace Synergos.CMS.Domain.Sections;

/// <summary>
/// Contrato central del sistema de composición de páginas.
///
/// Toda sección que pueda aparecer en el Block Grid de una página
/// implementa esta interfaz. Elimina el uso de 'object' como tipo
/// de colección y garantiza coherencia en tiempo de compilación.
///
/// El dispatcher de Razor resuelve la vista a partir de ViewName
/// sin necesidad de conocer los tipos concretos ni hacer pattern matching.
/// </summary>
public interface ISection
{
    /// <summary>
    /// Ruta del partial de Razor que renderiza esta sección.
    /// </summary>
    string ViewName { get; }

    /// <summary>
    /// Clase BEM del bloque externo (sg-block--{name}).
    /// El dispatcher la aplica en el wrapper, no en el partial interno.
    /// </summary>
    string BlockClass { get; }
}
