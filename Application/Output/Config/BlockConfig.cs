using System.Text.Json;

namespace Synergos.CMS.Application.Output.Config;

/// <summary>
/// Representación serializable de un bloque de contenido.
///
/// Type      → alias del Element Type ("elementHero", "elementCard", etc.)
/// BlockClass → clase BEM del bloque contenedor ("sg-component--hero")
/// Data      → propiedades del ViewModel serializadas en camelCase.
///             Angular puede discriminar por Type y deserializar Data al tipo correcto.
/// </summary>
public sealed record BlockConfig(
    string      Type,
    string      BlockClass,
    JsonElement Data
);
