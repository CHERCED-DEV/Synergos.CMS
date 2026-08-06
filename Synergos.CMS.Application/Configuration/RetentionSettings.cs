namespace Synergos.CMS.Application.Configuration;

/// <summary>
/// Tuning de retention para los stores filesystem. Sección
/// <c>Synergos:Retention</c>. Cap-270 Batch B (Olas 273-275).
/// </summary>
/// <remarks>
/// Cada setting expresado en días. <c>0</c> = nunca purgar (el
/// operador gestiona retention manualmente o el store es críticamente
/// inmutable, e.g. legal hold).
///
/// El <c>AuditRetentionDays</c> NO vive aquí — sigue en
/// <c>AdminSettings.AuditRetentionDays</c> por back-compat con
/// ADR 0070. La policy Audit lo lee del POCO original.
///
/// Valores por defecto eligidos según volumen típico + valor analítico
/// vs cost de almacenamiento. Operador puede ajustar arriba/abajo
/// según sus necesidades.
/// </remarks>
public sealed class RetentionSettings
{
    /// <summary>
    /// Días tras los cuales un comment con <c>Approved=false</c>
    /// (rejected/spam) se purga del JSON del nodo. Comments
    /// approved nunca se purgan automáticamente. Default 30 días.
    /// </summary>
    public int CommentsRejectedRetentionDays { get; init; } = 30;

    /// <summary>
    /// Días tras los cuales un form submission file se elimina del
    /// disco. Default 365 días (1 año) — generoso por compliance.
    /// Para forms con datos sensibles (DNI, healthcare) bumpear a
    /// menos según jurisdicción legal.
    /// </summary>
    public int FormSubmissionsRetentionDays { get; init; } = 365;

    /// <summary>
    /// Días tras los cuales un JSONL diario de search analytics se
    /// elimina. Default 30 días — bajado de 90 cuando la analítica pasó
    /// a persistirse a disco (ADR 0045, enmienda): lo que se guarda es
    /// TEXTO ESCRITO POR VISITANTES (puede traer nombres, direcciones,
    /// números de caso), y para "qué buscó la gente y no encontró" un
    /// mes ya da la señal editorial. Search analytics aporta valor
    /// rolling, no histórico profundo.
    /// </summary>
    public int SearchAnalyticsRetentionDays { get; init; } = 30;

    // NOTE: Cart abandonment NO tiene retention filesystem porque
    // InMemoryCartAbandonmentTracker es in-memory por diseño. Si en el
    // futuro llega un FileSystemCartAbandonmentTracker, agregar aquí
    // CartsRetentionDays + policy correspondiente.
}
