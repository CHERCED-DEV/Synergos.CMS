namespace Synergos.CMS.Application.Configuration;

/// <summary>
/// Tuning del background alert hosted service que monitorea
/// <c>IWebhookTelemetryStore</c> y dispara emails al operador cuando
/// algún canal cruza el threshold de failure rate. Olas 195-196.
/// </summary>
/// <remarks>
/// Sección <c>Synergos:Admin:WebhookTelemetryAlerts</c>.
/// Default Enabled=false — opt-in para no spammear hasta que el
/// operador configure threshold + email destinatario.
/// </remarks>
public sealed class WebhookTelemetryAlertSettings
{
    /// <summary>
    /// Si false (default), el hosted service no corre. Opt-in para
    /// activar con threshold + AlertEmail configurados.
    /// </summary>
    public bool Enabled { get; init; } = false;

    /// <summary>
    /// Intervalo entre checks en minutos. Default 5. Más bajo →
    /// alerts más rápidos pero más CPU. Más alto → menos overhead.
    /// </summary>
    public int CheckIntervalMinutes { get; init; } = 5;

    /// <summary>
    /// Umbral de fail rate (0.0 - 1.0) sobre el cual se dispara
    /// alert. Default 0.20 (20%). Calculado per canal sobre el ring
    /// buffer de 1000 samples.
    /// </summary>
    public double FailRateThreshold { get; init; } = 0.20;

    /// <summary>
    /// Mínimo de calls necesarias antes de evaluar threshold. Default
    /// 100. Evita alertar sobre stats de pocos samples
    /// estadísticamente irrelevantes.
    /// </summary>
    public int MinimumSampleSize { get; init; } = 100;

    /// <summary>
    /// Cooldown post-alert en minutos. Si un canal volvió a cruzar
    /// threshold dentro de este window post-alert previo, NO se
    /// re-alerta. Default 60 (1h). Evita spam.
    /// </summary>
    public int CooldownMinutes { get; init; } = 60;

    /// <summary>
    /// Email destinatario de los alerts. Si vacío, el service NO
    /// dispara aunque Enabled=true (no-op silencioso, log warning).
    /// </summary>
    public string AlertEmail { get; init; } = string.Empty;

    /// <summary>
    /// Si true (default), cuando un canal previamente alerting vuelve
    /// a healthy (failRate &lt; threshold con sample suficiente), se
    /// envía un email de "recovery" al mismo destinatario y se limpia
    /// el state interno. Olas 236-237 (Cap-240 Batch C). Cierra el
    /// loop alerting → resolved sin requerir consulta manual al
    /// dashboard. Set a false si el operador no quiere recibir el
    /// recovery (ej. solo le interesa la incidencia).
    /// </summary>
    public bool RecoveryEmailEnabled { get; init; } = true;

    // ─── Composite notifier channels (Cap-260 Batch B, Olas 254-256) ───
    // Patterns paralelos a Comments / Forms / Cart. Cada canal opt-in
    // por URL configurada — vacío = canal no-op silencioso.

    /// <summary>
    /// URL HTTP(S) del webhook custom para alerts (raw JSON POST).
    /// Compatible con cualquier endpoint custom. Default vacío =
    /// canal no-op.
    /// </summary>
    public string? WebhookUrl { get; init; }

    /// <summary>
    /// Bearer token opcional. Si poblado, el canal Webhook adjunta
    /// header <c>Authorization: Bearer {token}</c>.
    /// </summary>
    public string? WebhookBearerToken { get; init; }

    /// <summary>
    /// Secret compartido para firmar el body del webhook con
    /// HMAC-SHA256. Si poblado, adjunta header
    /// <c>X-Synergos-Signature: sha256={hex}</c>.
    /// </summary>
    public string? WebhookHmacSecret { get; init; }

    /// <summary>URL de Slack incoming webhook para alerts (Block Kit).</summary>
    public string? SlackWebhookUrl { get; init; }

    /// <summary>URL de Discord incoming webhook para alerts (embeds).</summary>
    public string? DiscordWebhookUrl { get; init; }

    /// <summary>URL de Microsoft Teams incoming webhook (MessageCard).</summary>
    public string? TeamsWebhookUrl { get; init; }
}
