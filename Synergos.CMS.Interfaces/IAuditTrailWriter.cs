namespace Synergos.CMS.Interfaces;

/// <summary>
/// Seam append-only para registrar acciones del admin dashboard
/// (approve/reject/delete/bulk-undo/etc) con quién las ejecutó y
/// cuándo. Soporta forensic review post-incident sin requerir logs
/// del proceso (que rotan/expire).
/// </summary>
/// <remarks>
/// Append-only por diseño — eventos no se editan ni borran. Para
/// retention (purge older than N days), implementación-específica.
///
/// La impl por defecto vive en
/// <c>Synergos.CMS.Web.Services.FileSystemAuditTrailWriter</c> y
/// persiste JSONL (one event per line) en
/// <c>App_Data/syn-audit/{yyyy-MM-dd}.jsonl</c>. Reads agregan
/// archivos en memoria (paginate post-fetch).
///
/// Para volumen alto o multi-instance load-balanced, swap por adapter
/// sobre DB / event log externo (deferred).
/// </remarks>
public interface IAuditTrailWriter
{
    /// <summary>
    /// Persiste un evento. Idempotent por
    /// <see cref="AuditEvent.Id"/> — mismo Id no se escribe dos veces
    /// en la misma día (file-based dedupe).
    /// </summary>
    Task WriteAsync(AuditEvent evt, CancellationToken cancellationToken);

    /// <summary>
    /// Devuelve los eventos más recientes (ordenados por
    /// <see cref="AuditEvent.OccurredAtUtc"/> desc). Sin paginación
    /// stateful — el caller pasa <paramref name="maxItems"/> en cada
    /// query. Filters opcionales aplicados in-memory.
    /// </summary>
    IReadOnlyList<AuditEvent> GetRecent(
        int maxItems,
        string? actorEmailFilter = null,
        string? actionFilter = null);

    /// <summary>
    /// Devuelve eventos en un rango fechas UTC (inclusive). Útil para
    /// CSV export con date range arbitrario, no constraint a los 7
    /// archivos diarios más recientes. Filters opcionales aplicados
    /// in-memory. Ordenados por <see cref="AuditEvent.OccurredAtUtc"/>
    /// desc. (Ola 163)
    /// </summary>
    IReadOnlyList<AuditEvent> GetByDateRange(
        DateTime fromUtc,
        DateTime toUtc,
        int maxItems,
        string? actorEmailFilter = null,
        string? actionFilter = null);

    /// <summary>
    /// Devuelve un evento específico por su <see cref="AuditEvent.Id"/>.
    /// Búsqueda hasta los últimos 30 días para no impactar performance.
    /// Devuelve null si no existe en esa ventana. (Ola 193)
    /// </summary>
    AuditEvent? GetById(string id);
}

/// <summary>
/// Snapshot inmutable de una acción admin. Persistido como JSON line.
/// </summary>
/// <param name="Id">Identificador único del evento — usado para
///   dedupe + correlation. Recomendado <c>Guid.NewGuid().ToString("N")</c>.</param>
/// <param name="OccurredAtUtc">Cuándo ocurrió la acción (no cuándo se
///   persistió — pueden diferir por buffering).</param>
/// <param name="ActorEmail">Email del Member que ejecutó la acción.
///   Vacío si la acción fue del sistema (background job).</param>
/// <param name="ActorName">Display name del Member. Fallback al
///   email si vacío.</param>
/// <param name="Action">Acción ejecutada — slug estable estilo
///   <c>"comment.approve"</c>, <c>"form.delete"</c>, <c>"member.lock"</c>.
///   Convención: <c>{resource}.{verb}</c>.</param>
/// <param name="Resource">Identificador del recurso afectado —
///   <c>commentId</c>, <c>formKey/storageId</c>, <c>memberKey</c>, etc.
///   Formato libre, pero lo bastante específico para seguirlo.</param>
/// <param name="Outcome">Outcome de la acción — <c>"success"</c> o
///   <c>"failure"</c> o <c>"partial"</c> (para bulk).</param>
/// <param name="Detail">Detalle libre — counts, error messages,
///   undo tokens, etc. Vacío si no aplica.</param>
/// <param name="Assertion">Con qué fuerza se afirmó que el actor era quien
///   dice ser (<see cref="IdentityAssertions"/>). <b>Vacío significa «no
///   consta»</b>, no «lo más débil»: es la verdad sobre todo asiento escrito
///   antes de que esta noción existiera, y sobre los caminos que no la
///   registran. Rellenarlo por defecto inventaría una comprobación que nadie
///   hizo. (HU #15)</param>
public sealed record AuditEvent(
    string Id,
    DateTime OccurredAtUtc,
    string ActorEmail,
    string ActorName,
    string Action,
    string Resource,
    string Outcome,
    string Detail,
    string Assertion = IdentityAssertions.None);
