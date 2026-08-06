namespace Synergos.CMS.Interfaces;

/// <summary>
/// Seam para registrar y consultar latencias de los outgoing webhooks
/// per-canal — usado por el admin Health view para mostrar al operador
/// qué canales son lentos / fallan más, y guía las decisiones de
/// per-channel resilience tuning (ADR 0069).
/// </summary>
/// <remarks>
/// La impl default es in-memory ring buffer (last N samples per
/// channel). No persiste a disk — los stats se reset en restart.
/// Para histórico persistente, swap por adapter sobre time-series
/// store (deferred).
/// </remarks>
public interface IWebhookTelemetryStore
{
    /// <summary>
    /// Registra el resultado de una llamada outgoing — invocado por
    /// el <c>WebhookTelemetryHandler</c>. Ola 165.
    /// </summary>
    void RecordOutcome(
        string channelName,
        TimeSpan elapsed,
        int statusCode,
        bool isSuccess);

    /// <summary>
    /// Snapshot de stats per-canal observados desde el último restart.
    /// </summary>
    IReadOnlyList<ChannelTelemetrySnapshot> GetChannelStats();
}

/// <summary>
/// Stats agregados de un canal observados desde el último restart.
/// </summary>
/// <param name="ChannelName">FactoryName del canal (e.g.
///   "comment-moderation-teams").</param>
/// <param name="TotalCalls">Total de outgoing requests registrados.</param>
/// <param name="SuccessCount">Calls con statusCode 2xx.</param>
/// <param name="FailureCount">Calls con statusCode no-2xx o exception.</param>
/// <param name="P50LatencyMs">Latencia mediana en milisegundos.</param>
/// <param name="P95LatencyMs">Latencia P95 en milisegundos.</param>
/// <param name="P99LatencyMs">Latencia P99 en milisegundos.</param>
/// <param name="LastObservedUtc">Timestamp de la última call observada.</param>
public sealed record ChannelTelemetrySnapshot(
    string ChannelName,
    long TotalCalls,
    long SuccessCount,
    long FailureCount,
    double P50LatencyMs,
    double P95LatencyMs,
    double P99LatencyMs,
    DateTime? LastObservedUtc);
