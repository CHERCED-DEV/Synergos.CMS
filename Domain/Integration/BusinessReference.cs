namespace Synergos.CMS.Domain.Integration;

/// <summary>
/// Puente entre el Content Domain (Umbraco) y el Business Domain (APIs externas).
///
/// Umbraco almacena únicamente esta referencia. Angular la recibe como
/// atributo del Web Component y resuelve la entidad real desde la API de negocio.
/// Este diseño garantiza que datos regulados (pacientes, precios, historial clínico)
/// nunca entren al CMS.
///
/// La propiedad DisplayName es azúcar editorial: ayuda al editor a identificar
/// qué entidad está referenciando sin que el CMS deba entender el dominio de negocio.
/// </summary>
public sealed record BusinessReference
{
    public required string EntityId   { get; init; }
    public required string EntityType { get; init; }
    public string?         DisplayName { get; init; }

    /// <summary>
    /// Metadatos opcionales para que Angular pueda inicializar el componente
    /// antes de que llegue la respuesta de la API de negocio (skeleton, hints).
    /// Nunca almacenar datos sensibles aquí.
    /// </summary>
    public Dictionary<string, object?> Metadata { get; init; } = new();
}
