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
}
