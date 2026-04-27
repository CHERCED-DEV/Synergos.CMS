namespace Synergos.CMS.Application.Configuration;

/// <summary>
/// Typed POCO bound from <c>appsettings.*.json</c> section
/// <c>Synergos:Admin</c>. Tuning del dashboard de operaciones SSR
/// (AdminController + Views/Admin).
/// </summary>
/// <remarks>
/// La intención es que el operador pueda ajustar comportamientos
/// runtime sin recompilar — TTL del cache, page size default,
/// hard caps de export, etc.
/// </remarks>
public sealed class AdminSettings
{
    /// <summary>
    /// TTL del cache del pending counter en el topbar. Default
    /// 30s. Bajar a 5s para dashboards de muchos moderators concurrent
    /// (badge fresco) o subir a 5min para sites con > 10k pending
    /// (reduce filesystem load).
    /// </summary>
    public TimeSpan PendingCountCacheTtl { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Default page size para listados (moderation comments + form
    /// submissions). El moderator puede override con ?pageSize=N.
    /// Default 25.
    /// </summary>
    public int DefaultPageSize { get; init; } = 25;

    /// <summary>
    /// Hard cap del CSV export. Submissions más allá de este número
    /// requieren múltiples export con date range. Default 5000 evita
    /// timeout/OOM en datasets grandes.
    /// </summary>
    public int CsvExportHardCap { get; init; } = 5000;

    /// <summary>
    /// Ventana de undo para bulk-reject (Ola 124). Dentro de esta
    /// ventana el moderator puede revertir el bulk action via flash
    /// message "↶ Deshacer". Default 30s.
    /// </summary>
    public TimeSpan BulkUndoWindow { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Tuning de resilience para los 12 named HttpClients de notifier
    /// channels (Ola 129). Compartido entre todos — para tuning
    /// per-canal individual, swap por Dictionary indexed by FactoryName
    /// (deferred Rule of Three).
    /// </summary>
    public WebhookResilienceSettings WebhookResilience { get; init; } = new();

    /// <summary>
    /// Retention de audit trail files (App_Data/syn-audit/{yyyy-MM-dd}.jsonl).
    /// Default 90 días. <c>0</c> = retention infinito (operador gestiona
    /// manualmente). Background service purga al boot + cada 24h.
    /// (Ola 162)
    /// </summary>
    public int AuditRetentionDays { get; init; } = 90;
}

/// <summary>
/// Configuración de resilience para los webhook channels. Top-level
/// fields son los defaults aplicados a todos los 12 named HttpClients;
/// <see cref="PerChannel"/> permite override per-canal (Olas 157-158).
/// </summary>
public sealed class WebhookResilienceSettings
{
    /// <summary>Max retry attempts (default 3 = total 4 intentos).</summary>
    public int MaxRetryAttempts { get; init; } = 3;

    /// <summary>Timeout per attempt en segundos. Default 10.</summary>
    public int AttemptTimeoutSeconds { get; init; } = 10;

    /// <summary>Timeout total del request. Default 30 segundos.</summary>
    public int TotalRequestTimeoutSeconds { get; init; } = 30;

    /// <summary>Base delay del exponential backoff. Default 2000ms.</summary>
    public int RetryBaseDelayMs { get; init; } = 2000;

    /// <summary>
    /// Overrides por canal indexados por <c>FactoryName</c>
    /// (e.g. "comment-moderation-teams", "cart-abandonment-discord").
    /// Solo los campos no-null en cada override reemplazan al default.
    /// Vacío por default — todos los canales heredan los top-level
    /// fields. (Olas 157-158)
    /// </summary>
    public Dictionary<string, WebhookResilienceChannelOverride> PerChannel { get; init; } = new();
}

/// <summary>
/// Override per-canal de los settings de resilience. Cada campo es
/// nullable — solo los no-null reemplazan al default top-level.
/// Permite tuning quirúrgico (e.g. Teams es lento → bump
/// AttemptTimeoutSeconds solo para teams) sin tocar los demás canales.
/// </summary>
public sealed class WebhookResilienceChannelOverride
{
    public int? MaxRetryAttempts { get; init; }
    public int? AttemptTimeoutSeconds { get; init; }
    public int? TotalRequestTimeoutSeconds { get; init; }
    public int? RetryBaseDelayMs { get; init; }
}
