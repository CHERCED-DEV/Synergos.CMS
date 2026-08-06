namespace Synergos.CMS.Interfaces;

/// <summary>
/// Estado del expediente del trámite — la máquina de estados auditada que es el
/// diferenciador del dominio Gobierno (doc gobierno.md §6). El objeto NO es una orden
/// que confirma y cierra: es un expediente de larga vida que transita estados atendido
/// por un funcionario. Los slugs de cara pública (que consume la UI) son
/// <c>submitted → in-review → info-requested → approved / rejected</c>.
/// </summary>
public enum CaseStatus
{
    /// <summary>Radicado (estado inicial tras enviar la solicitud) — cara pública <c>submitted</c>.</summary>
    Radicado,
    /// <summary>En revisión por el funcionario — cara pública <c>in-review</c>.</summary>
    EnRevision,
    /// <summary>Requiere subsanación — cara pública <c>info-requested</c>.</summary>
    Subsanacion,
    /// <summary>Resuelto favorable (terminal) — cara pública <c>approved</c>.</summary>
    Resuelto,
    /// <summary>Rechazado (terminal) — cara pública <c>rejected</c>.</summary>
    Rechazado,
}

/// <summary>Prioridad del expediente en la cola del funcionario.</summary>
public enum CasePriority
{
    Low,
    Normal,
    High,
}

/// <summary>
/// Una entrada del timeline del expediente — un hito con su etiqueta, fecha y estado
/// visual (<c>done | current | pending</c>). El timeline es la proyección del ciclo de
/// vida que alimenta el seguimiento del ciudadano y el detalle del funcionario; los
/// hitos futuros aún no alcanzados se emiten como <c>pending</c>.
/// </summary>
public sealed record CaseTimelineEntry(
    string Id,
    string Label,
    DateTimeOffset Date,
    string State,
    string Note);

/// <summary>
/// La resolución de un expediente (cara funcionario): el desenlace + la nota + quién y
/// cuándo. <see cref="Outcome"/> es el slug de cara pública <c>approve | reject |
/// request-info</c>. Null mientras el expediente no tenga decisión final.
/// </summary>
public sealed record CaseDecision(
    string Outcome,
    string Note,
    DateTimeOffset DecidedAt,
    string DecidedBy);

/// <summary>
/// Vista completa de un expediente: el radicado + trámite + solicitante + respuestas +
/// documentos adjuntos + estado actual + prioridad/SLA + timeline + decisión (si la
/// hay). Es lo que la bandeja del ciudadano (seguimiento) y el detalle del funcionario
/// renderizan.
/// </summary>
public sealed record CaseDetail(
    string CaseId,
    string Radicado,
    string TramiteId,
    string TramiteName,
    GovCitizen Citizen,
    IReadOnlyDictionary<string, string> FormData,
    IReadOnlyList<CitizenDocumentRef> Documents,
    CaseStatus Status,
    string CurrentStage,
    CasePriority Priority,
    int SlaDaysLeft,
    decimal FeeMinor,
    string Currency,
    DateTimeOffset RadicadoAt,
    IReadOnlyList<CaseTimelineEntry> Timeline,
    CaseDecision? Decision);

/// <summary>
/// Ítem de la bandeja (cola) — la forma compacta del expediente para listar. El
/// ciudadano ve los suyos; el funcionario ve la cola de trabajo con prioridad + SLA.
/// </summary>
public sealed record CaseInboxItem(
    string CaseId,
    string Radicado,
    string TramiteName,
    string CitizenName,
    CaseStatus Status,
    string CurrentStage,
    CasePriority Priority,
    int SlaDaysLeft,
    DateTimeOffset RadicadoAt);

/// <summary>
/// Seguimiento de expedientes del vertical Gobierno (doc gobierno.md §4). Es la pieza
/// del MOTOR que expone el ciclo de vida del expediente a las dos bandejas:
/// <see cref="GetCaseAsync"/> → expediente + estado + timeline (seguimiento del
/// ciudadano / detalle del funcionario); <see cref="ListForCitizenAsync"/> → los
/// expedientes del ciudadano; <see cref="GetQueueAsync"/> → la cola del funcionario
/// filtrada por entidad + estado.
/// </summary>
/// <remarks>
/// Lógica pura en <c>Synergos.CMS.Application</c> — cero dependencia de
/// Umbraco/AspNetCore (ADR 0002). NO duplica el estado: lo COMPONE (DIP) del agregado de
/// <see cref="IApplicationService"/> / <see cref="ICaseWorkflowService"/> — el tracking
/// es el diferenciador del dominio (research GOV.CO). El adapter real (DB / sistema de
/// la entidad) implementa la misma seam. ADR 0075 (filter es el caso central: ciudadano
/// vs cola por entidad/estado).
/// </remarks>
public interface ICaseTrackingProvider
{
    /// <summary>Devuelve el expediente por id de caso o radicado, o null si no existe.</summary>
    Task<CaseDetail?> GetCaseAsync(string caseId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Devuelve los expedientes cuyo solicitante es el member <paramref name="memberKey"/>,
    /// ordenados por fecha de radicación descendente. Vacía (no lanza) si no tiene ninguno.
    /// </summary>
    /// <remarks>
    /// <b>Por memberKey y NO por email, y ése es el punto.</b> Antes filtraba por
    /// <c>Citizen.Email</c>, que lo teclea quien radica: cualquiera listaba los expedientes
    /// de otro sabiendo su correo (<c>?citizen=…</c>). El memberKey lo sella el servidor
    /// desde la sesión. Es el IDOR que ADR 0103 cerró en Tienda, cerrado aquí.
    ///
    /// <para>Los expedientes de INVITADO (<c>Citizen.MemberKey == null</c>) no entran nunca
    /// — no tienen dueño a quien pertenecer. Mismo criterio que el historial de órdenes.</para>
    /// </remarks>
    Task<IReadOnlyList<CaseInboxItem>> ListForMemberAsync(Guid memberKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Devuelve la cola del funcionario filtrada por <paramref name="agency"/> (entidad,
    /// null/vacío = todas) y <paramref name="status"/> (slug público, null/vacío =
    /// todos), ordenada por prioridad (alta primero) y luego por SLA (menos días primero).
    /// </summary>
    Task<IReadOnlyList<CaseInboxItem>> GetQueueAsync(string? agency, string? status, CancellationToken cancellationToken = default);
}
