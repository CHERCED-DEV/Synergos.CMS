namespace Synergos.CMS.Application.Configuration;

/// <summary>
/// Typed POCO bound from <c>appsettings.*.json</c> section
/// <c>Synergos:CartAbandonment</c>. Configura el threshold y el
/// scan period del background service que detecta carts abandonados
/// (ADR 0044 — Ola 81).
/// </summary>
public sealed class CartAbandonmentSettings
{
    /// <summary>
    /// Si true, el background hosted service activa el scan periódico.
    /// Default true. Setear false para sites sin shop (no hay trackings
    /// que reportar) — el tracker sigue presente en DI pero no se hace
    /// trabajo de fondo.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Tiempo sin actividad después del cual un cart se reporta como
    /// abandonado. Default 2 horas. Para shops B2B con ciclos largos
    /// puede subirse a 24h+; para flash sales puede bajarse a 30 min.
    /// </summary>
    public TimeSpan AbandonmentThreshold { get; init; } = TimeSpan.FromHours(2);

    /// <summary>
    /// Cada cuánto el background service hace scan. Default 15 minutos.
    /// Bajar este valor aumenta carga; subirlo retrasa la detección.
    /// </summary>
    public TimeSpan ScanInterval { get; init; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Subtotal mínimo para reportar abandonment. Default 0 (todos).
    /// Setear &gt; 0 filtra carts triviales (ej. visitante exploró pero
    /// no agregó nada significativo).
    /// </summary>
    public decimal MinSubtotalToReport { get; init; } = 0m;
}
