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
/// <remarks>
/// <para><b><see cref="FeeStatus"/> existe porque el expediente lo ESCRIBÍA y nadie podía
/// LEERLO</b> (HU #116). El agregado guarda el estado del cobro de la tasa desde la ADR 0116
/// fase 5, y esta proyección —la única que llega a las dos bandejas— no lo declaraba: una
/// escritura sin camino de lectura, el espejo de
/// <c>feedback_no_read_without_a_write_path</c>. Con el motor de pago en proceso daba igual,
/// porque contestaba siempre <c>Captured</c>; con <c>Api.Payments</c> detrás, un
/// <c>Unavailable</c> es exactamente el caso que alguien tendría que perseguir y quedaba
/// escrito donde no mira nadie.</para>
/// <para><b><c>null</c> es «no consta», y hay que leerlo junto a <see cref="FeeMinor"/>.</b>
/// Un trámite EXENTO (<c>FeeMinor == 0</c>) no abre sesión de cobro y por eso no tiene
/// estado; un expediente con tasa y sin estado es uno anterior a la ADR 0116 fase 5, y de
/// ése la verdad es que no se sabe. Rellenarlo con <c>Captured</c> diría que se cobró, que
/// es la afirmación que nadie hizo.</para>
/// </remarks>
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
    CaseDecision? Decision,
    string? FeeStatus = null);

/// <summary>
/// Ítem de la bandeja (cola) — la forma compacta del expediente para listar. El
/// ciudadano ve los suyos; el funcionario ve la cola de trabajo con prioridad + SLA.
/// </summary>
/// <remarks>
/// <see cref="FeeStatus"/> viaja también en la forma compacta, y no por simetría: la cola del
/// funcionario es donde se PERSIGUE una tasa que no se cobró, y una tasa pendiente que sólo
/// se ve abriendo el expediente uno por uno no la persigue nadie (#116). Misma lectura que en
/// <see cref="CaseDetail.FeeStatus"/>: <c>null</c> es «no consta».
/// </remarks>
public sealed record CaseInboxItem(
    string CaseId,
    string Radicado,
    string TramiteName,
    string CitizenName,
    CaseStatus Status,
    string CurrentStage,
    CasePriority Priority,
    int SlaDaysLeft,
    DateTimeOffset RadicadoAt,
    decimal FeeMinor = 0m,
    string? FeeStatus = null);

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
