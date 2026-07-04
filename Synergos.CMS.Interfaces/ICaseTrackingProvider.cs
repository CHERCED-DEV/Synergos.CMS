namespace Synergos.CMS.Interfaces;

/// <summary>
/// Estado del expediente del trámite — la máquina de estados auditada que es el
/// diferenciador del dominio Gobierno (doc gobierno-app-spec §6). El objeto NO es una
/// orden que confirma y cierra: es un expediente de larga vida que transita estados
/// atendido por un funcionario.
/// </summary>
public enum CaseStatus
{
    /// <summary>Radicado (estado inicial tras enviar la solicitud) — espera revisión.</summary>
    Radicado,
    /// <summary>En revisión por el funcionario.</summary>
    EnRevision,
    /// <summary>Requiere subsanación — el ciudadano debe completar/corregir.</summary>
    Subsanacion,
    /// <summary>Resuelto (favorable) — estado terminal.</summary>
    Resuelto,
    /// <summary>Rechazado — estado terminal.</summary>
    Rechazado,
}

/// <summary>
/// Una entrada del timeline del expediente — una transición de estado con su fecha,
/// el actor que la ejecutó y una nota. El timeline es append-only (espejo del rastro
/// de <see cref="IAuditTrailWriter"/>) y alimenta el seguimiento del ciudadano.
/// </summary>
public sealed record CaseTimelineEntry(
    CaseStatus Status,
    DateTimeOffset OccurredAt,
    string Actor,
    string Note);

/// <summary>
/// Vista completa de un expediente: el radicado + trámite + solicitante + respuestas +
/// documentos adjuntos + estado actual + timeline de transiciones. Es lo que la bandeja
/// del ciudadano (seguimiento) y el detalle del funcionario renderizan.
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
    decimal Fee,
    string Currency,
    DateTimeOffset RadicadoAt,
    IReadOnlyList<CaseTimelineEntry> Timeline);

/// <summary>
/// Ítem de la bandeja (cola) — la forma compacta del expediente para listar. El
/// ciudadano ve los suyos; el funcionario ve la cola de trabajo.
/// </summary>
public sealed record CaseInboxItem(
    string CaseId,
    string Radicado,
    string TramiteName,
    string CitizenName,
    CaseStatus Status,
    DateTimeOffset RadicadoAt);

/// <summary>
/// Seguimiento de expedientes del vertical Gobierno (doc gobierno-app-spec §4). Es la
/// pieza del MOTOR que expone el ciclo de vida del expediente a las dos bandejas:
/// <see cref="GetCaseAsync"/> → expediente + estado + timeline (seguimiento del
/// ciudadano / detalle del funcionario); <see cref="GetInboxAsync"/> → la cola filtrada
/// por actor + rol (el ciudadano ve los suyos por email; el funcionario ve toda la cola).
/// </summary>
/// <remarks>
/// Lógica pura en <c>Synergos.CMS.Application</c> — cero dependencia de
/// Umbraco/AspNetCore (ADR 0002). NO duplica el estado: lo COMPONE (DIP) del agregado de
/// <see cref="IApplicationService"/> / <see cref="ICaseWorkflowService"/> — el tracking
/// es el diferenciador del dominio (research GOV.CO). El adapter real (DB / sistema de
/// la entidad) implementa la misma seam. ADR 0075.
/// </remarks>
public interface ICaseTrackingProvider
{
    /// <summary>Devuelve el expediente por id de caso o radicado, o null si no existe.</summary>
    Task<CaseDetail?> GetCaseAsync(string caseId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Devuelve la bandeja según <paramref name="role"/>: <c>citizen</c> → los
    /// expedientes cuyo solicitante es <paramref name="actor"/> (email); cualquier otro
    /// rol (<c>officer</c>/<c>admin</c>) → toda la cola. Ordenada por fecha de radicación
    /// descendente. Vacía (no lanza) si no hay coincidencias.
    /// </summary>
    Task<IReadOnlyList<CaseInboxItem>> GetInboxAsync(string actor, string role, CancellationToken cancellationToken = default);
}
