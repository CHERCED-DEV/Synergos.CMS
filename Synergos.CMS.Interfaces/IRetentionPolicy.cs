namespace Synergos.CMS.Interfaces;

/// <summary>
/// Policy de retention para un store filesystem. Cap-270 Batch B
/// (Olas 273-275). Generaliza el pattern del
/// <c>AuditRetentionHostedService</c> existente (ADR 0070) para
/// aplicarse a todos los stores que crecen sin límite (comments,
/// form-submissions, search-analytics, carts).
/// </summary>
/// <remarks>
/// Cada implementación es responsable de:
/// - Leer su propia configuración (días de retention, paths).
/// - Decidir cuándo es no-op (retention 0 = nunca purgar).
/// - Iterar el directorio físico, filtrar archivos viejos, eliminar.
/// - Devolver el count de items purgados para logging del sweeper.
///
/// El <c>RetentionSweepHostedService</c> itera todas las policies
/// registradas en DI con try-catch por policy (un store roto no
/// afecta al resto).
///
/// **Idempotent** por diseño — segunda ejecución sobre el mismo
/// estado retorna 0 sin modificar nada.
/// </remarks>
public interface IRetentionPolicy
{
    /// <summary>
    /// Identificador friendly para logs. Ej. <c>"audit"</c>,
    /// <c>"form-submissions"</c>. NO incluye prefijo <c>syn-</c>.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Ejecuta un sweep. Retorna count de items purgados (archivos
    /// borrados, comments anonimizados, etc.). Retorna 0 si la policy
    /// está disabled o no hay nada que purgar.
    /// </summary>
    Task<int> SweepAsync(CancellationToken cancellationToken);
}
