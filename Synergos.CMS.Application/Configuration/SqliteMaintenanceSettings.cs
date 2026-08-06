namespace Synergos.CMS.Application.Configuration;

/// <summary>
/// Tuning del SQLite maintenance hosted service. Sección
/// <c>Synergos:SqliteMaintenance</c>. Cap-270 Batch D (Olas 278-279).
/// </summary>
/// <remarks>
/// Aplica solo si la connection string de Umbraco apunta a SQLite
/// (provider Microsoft.Data.Sqlite). Si Umbraco corre sobre SQL
/// Server, el service hace early-return silencioso.
///
/// El service ejecuta dos PRAGMAs periodicamente:
/// - <c>PRAGMA wal_checkpoint(TRUNCATE)</c> — colapsa el WAL file
///   back into main DB. Sin esto el WAL crece sin límite (audit
///   reveló WAL de 4MB sobre DB de 4MB).
/// - <c>PRAGMA optimize</c> — actualiza statistics para el query
///   planner. Recomendado por SQLite docs en sesiones long-running.
/// </remarks>
public sealed class SqliteMaintenanceSettings
{
    /// <summary>
    /// Si false, el hosted service nunca corre. Default true.
    /// Usar false si el operador prefiere correr maintenance manual
    /// off-line.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Intervalo entre ejecuciones del maintenance. Default 24 horas.
    /// </summary>
    public int IntervalHours { get; init; } = 24;

    /// <summary>
    /// Initial delay tras boot antes del primer run. Default 120s
    /// para dejar que Umbraco termine startup pesado sin contender
    /// el file lock.
    /// </summary>
    public int InitialDelaySeconds { get; init; } = 120;
}
