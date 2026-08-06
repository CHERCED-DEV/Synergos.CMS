namespace Synergos.CMS.Interfaces;

/// <summary>
/// Seam de notificación de alerts del webhook telemetry scan
/// (<c>WebhookTelemetryAlertScanner</c>). Cap-260 Batch B (Olas 254-256).
/// Pareja conceptual de <see cref="ICartAbandonmentNotifier"/>,
/// <see cref="ICommentModerationNotifier"/> y
/// <see cref="IFormSubmissionNotifier"/>.
/// </summary>
/// <remarks>
/// Pattern Composite + Channel — paralelo a las 3 familias previas
/// (Rule of Three trigger):
///
/// - <see cref="IAlertNotifier"/> es la fachada que el
///   <c>WebhookTelemetryAlertScanner</c> inyecta en lugar del
///   <see cref="IEmailService"/> directo.
/// - <see cref="IAlertNotifierChannel"/> son los canales individuales
///   (email, slack, discord, teams, custom webhook).
/// - Composite default itera todos los canales con try-catch por
///   canal — un canal roto no afecta a los demás.
///
/// Distintos canales para alert vs recovery permiten formato per-tipo
/// (e.g. Slack rojo para alert, verde para recovery; Discord embed
/// con color brand alertColor vs successColor).
/// </remarks>
public interface IAlertNotifier
{
    /// <summary>
    /// Notifica que un canal cruzó el threshold de fail rate
    /// (<see cref="WebhookAlertEvent"/>). Llamado fire-and-forget por
    /// el scanner.
    /// </summary>
    Task NotifyAlertAsync(WebhookAlertEvent alert, CancellationToken cancellationToken);

    /// <summary>
    /// Notifica que un canal previamente alerting volvió a healthy
    /// (<see cref="WebhookRecoveryEvent"/>). Solo se llama si
    /// <c>WebhookTelemetryAlertSettings.RecoveryEmailEnabled</c> es true.
    /// </summary>
    Task NotifyRecoveryAsync(WebhookRecoveryEvent recovery, CancellationToken cancellationToken);
}

/// <summary>
/// Marker interface para canales individuales que componen un
/// <see cref="IAlertNotifier"/> agregado.
/// </summary>
public interface IAlertNotifierChannel : IAlertNotifier
{
}

/// <summary>
/// Snapshot de un alert disparado — payload pasado a cada channel.
/// </summary>
/// <param name="ChannelName">FactoryName del canal afectado (e.g.
///   "comment-moderation-teams").</param>
/// <param name="FailRate">Fail rate observado (0.0-1.0).</param>
/// <param name="Threshold">Threshold configurado.</param>
/// <param name="TotalCalls">Total de calls en el sample window.</param>
/// <param name="SuccessCount">Calls con statusCode 2xx.</param>
/// <param name="FailureCount">Calls fallidas.</param>
/// <param name="P50LatencyMs">Latencia mediana.</param>
/// <param name="P95LatencyMs">Latencia P95.</param>
/// <param name="P99LatencyMs">Latencia P99.</param>
/// <param name="LastObservedUtc">Última call observada.</param>
/// <param name="CooldownMinutes">Cooldown configurado entre alerts.</param>
public sealed record WebhookAlertEvent(
    string ChannelName,
    double FailRate,
    double Threshold,
    long TotalCalls,
    long SuccessCount,
    long FailureCount,
    double P50LatencyMs,
    double P95LatencyMs,
    double P99LatencyMs,
    DateTime? LastObservedUtc,
    int CooldownMinutes);

/// <summary>
/// Snapshot de un recovery disparado — channel previamente alerting
/// volvió bajo threshold con sample suficiente.
/// </summary>
/// <param name="ChannelName">FactoryName del canal recovered.</param>
/// <param name="CurrentFailRate">Fail rate actual (post-recovery).</param>
/// <param name="PriorFailRate">Fail rate al momento del último alert.</param>
/// <param name="Threshold">Threshold configurado.</param>
/// <param name="AlertingDuration">Duración del incidente
///   (FirstFiredUtc → ahora).</param>
/// <param name="FirstFiredUtc">Timestamp del primer alert del ciclo.</param>
/// <param name="LastFiredUtc">Timestamp del último alert.</param>
/// <param name="TotalCalls">Total de calls en el sample window actual.</param>
/// <param name="SuccessCount">Calls con statusCode 2xx.</param>
/// <param name="FailureCount">Calls fallidas.</param>
public sealed record WebhookRecoveryEvent(
    string ChannelName,
    double CurrentFailRate,
    double PriorFailRate,
    double Threshold,
    TimeSpan AlertingDuration,
    DateTime FirstFiredUtc,
    DateTime LastFiredUtc,
    long TotalCalls,
    long SuccessCount,
    long FailureCount);
