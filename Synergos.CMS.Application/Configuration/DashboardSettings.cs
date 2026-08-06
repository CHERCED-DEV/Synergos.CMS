namespace Synergos.CMS.Application.Configuration;

/// <summary>
/// Typed POCO bound from <c>appsettings.*.json</c> section
/// <c>Synergos:Dashboard</c>. Gobierna el comportamiento del módulo de
/// métricas (ADR 0097): persistencia de la proyección y retención.
/// </summary>
/// <remarks>
/// Solo lleva ajustes de comportamiento de servidor (flush + retención).
/// La identidad/UX del panel vive en el schema uSync <c>cfgDashboardSettings</c>
/// y en la app Angular. Per ADR 0002, Application no referencia
/// <c>Microsoft.Extensions.Options</c>; el binding se hace en
/// <c>OptionsComposer</c> y el Web extrae <c>.Value</c>.
/// </remarks>
public sealed class DashboardSettings
{
    /// <summary>
    /// Si false, el hosted service de flush no corre (la proyección queda
    /// solo en memoria, efímera). Default true.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Cada cuántos segundos se flushea la proyección en memoria a JSONL.
    /// Default 300 (5 min). Mínimo efectivo 30.
    /// </summary>
    public int FlushIntervalSeconds { get; init; } = 300;

    /// <summary>
    /// Días de retención de los buckets de proyección. Más viejo que esto
    /// se descarta al rehidratar/flushear. Default 730 (2 años). 0 = nunca
    /// purgar.
    /// </summary>
    public int ProjectionRetentionDays { get; init; } = 730;
}
