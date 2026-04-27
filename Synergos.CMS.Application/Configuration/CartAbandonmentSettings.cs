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

    /// <summary>
    /// Dirección de email del operador que recibe el reporte cuando
    /// un cart queda abandonado (consumido por la impl default
    /// <c>EmailCartAbandonmentNotifier</c>). Default vacío = canal
    /// no-op (no envía).
    /// </summary>
    public string? NotifyEmailAddress { get; init; }

    /// <summary>
    /// URL HTTP(S) a la que el <c>WebhookCartAbandonmentNotifier</c>
    /// hace POST con el payload JSON. Compatible con Slack incoming
    /// webhooks, Discord, Teams, n8n, Zapier o cualquier endpoint
    /// custom. Default null/empty = canal no-op.
    /// </summary>
    public string? WebhookUrl { get; init; }

    /// <summary>
    /// Bearer token opcional para el webhook. Si está poblado, el
    /// canal adjunta header <c>Authorization: Bearer {token}</c>.
    /// </summary>
    public string? WebhookBearerToken { get; init; }

    /// <summary>
    /// Secret compartido para firmar el body del webhook con
    /// HMAC-SHA256. Si está poblado, el canal adjunta header
    /// <c>X-Synergos-Signature: sha256={hex}</c>.
    /// </summary>
    public string? WebhookHmacSecret { get; init; }

    /// <summary>
    /// URL de Slack incoming webhook. Si está poblada, el canal
    /// <c>SlackCartAbandonmentNotifier</c> POST con payload formato
    /// Slack Block Kit. Independiente de <see cref="WebhookUrl"/>.
    /// </summary>
    public string? SlackWebhookUrl { get; init; }

    /// <summary>URL de Discord incoming webhook (formato embeds).</summary>
    public string? DiscordWebhookUrl { get; init; }

    /// <summary>URL de Microsoft Teams incoming webhook (MessageCard).</summary>
    public string? TeamsWebhookUrl { get; init; }
}
